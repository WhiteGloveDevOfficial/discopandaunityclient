#define ENABLE_DISCOPANDA_DEBUGGING
using System;
using System.IO;
using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json;
using UnityEngine.Rendering;
using UnityEngine.Profiling;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections.LowLevel.Unsafe;

public static class DiscoPandaRecorder
{
    private static int captureFrameRate = 30;
    private static int videoResolutionWidth = Screen.width;
    private static int videoResolutionHeight = Screen.height;
    private static int videoBitRate = 1000;
    private static string tempFolderPath = "TempVideos";

    private static int screenResolutionWidth = Screen.width;
    private static int screenResolutionHeight = Screen.height;

    public static string ffmpegPath;

    private static bool isRecording;
    private static string currentSubfolderPath;
    private static string currentFramePath;

    private static DateTime recordingStartTime;
    private static long chunkStartTime;
    private static float timeSinceLastFrame;
    private static int clipFragmentIndex;

    static JobHandle imageEncoderJobHandle;
    static NativeArray<byte> ppmBytes;
    static NativeArray<byte> ppmHeader;
    static byte[] managedArray;

    struct CaptureInfo
    {
        public long startTime;
        public long endTime;
        public System.Diagnostics.Process process;
    }

    private static Queue<CaptureInfo> processesQueue = new Queue<CaptureInfo>();
    private static RenderTexture renderTexture;

    public static Camera CaptureCamera { get; set;}
    public static Action OnRecordingStarted { get; set; }
    public static Action OnRecordingStopped { get; set; }

    private static float CaptureFrameDelay => 1f / captureFrameRate;

    private static void Initialize()
    {
        Log($"Capture resolution {videoResolutionWidth} {videoResolutionHeight}");

        DeleteTempFolder();

        InitializeCommandBuffer();

        ppmBytes = new NativeArray<byte>(18 + (videoResolutionWidth * videoResolutionHeight) * 3, Allocator.Persistent);
        managedArray = new byte[ppmBytes.Length];

        var headerString = $"P6\n{videoResolutionWidth} {videoResolutionHeight}\n255\n";
        var headerBytes = System.Text.Encoding.ASCII.GetBytes(headerString);
        ppmHeader = new NativeArray<byte>(headerBytes, Allocator.Persistent);

        Directory.CreateDirectory(GetTempFolderPath());
    }

    private static void InitializeRenderTexture()
    {
        if (renderTexture == null)
        {
            RenderTextureFormat format = RenderTextureFormat.ARGB32;
            renderTexture = new RenderTexture(screenResolutionWidth, screenResolutionHeight, 24, format);
        }
    }

    private static void InitializeCommandBuffer()
    {
        if (commandBuffer != null)
            return;

        InitializeRenderTexture();

        commandBuffer = new CommandBuffer();
        commandBuffer.name = "CaptureScreen";
        commandBuffer.Blit(null, renderTexture);
        commandBuffer.RequestAsyncReadback(renderTexture, OnCompleteReadback);
    }

    private static void DeleteTempFolder()
    {
        string folderPath = GetTempFolderPath();

        if (Directory.Exists(folderPath))
            Directory.Delete(folderPath, true);
    }

    private static string GetTempFolderPath()
    {
        return Path.Combine(Application.persistentDataPath, tempFolderPath);
    }

    public static void Dispose()
    {
        imageEncoderJobHandle.Complete();

        if (ppmBytes.IsCreated)
            ppmBytes.Dispose();

        if (ppmHeader.IsCreated)
            ppmHeader.Dispose();

        if (bytes.IsCreated)
            bytes.Dispose();
    }

    private static bool InitializeCapturing()
    {
        if (CaptureCamera != null)
            return true;

        // Find camera to capture
        CaptureCamera = Camera.main;

        if (CaptureCamera == null)
            return false;

        CaptureCamera.AddCommandBuffer(CameraEvent.AfterImageEffects, commandBuffer);
        return true;
    }

    public static void SetCaptureCamera(Camera camera)
    {
        if (CaptureCamera != null)
        {
            CaptureCamera.RemoveCommandBuffer(CameraEvent.AfterImageEffects, commandBuffer);
        }

        CaptureCamera = camera;
        InitializeCommandBuffer();

        if (CaptureCamera != null)
        {
            CaptureCamera.AddCommandBuffer(CameraEvent.AfterImageEffects, commandBuffer);
        }
    }

    public static void StartRecording()
    {
        Initialize();

        if (isRecording) return;
        isRecording = true;

        VideoUpload.sessionId = Guid.NewGuid().ToString();
        Log($"VideoUpload.sessionId  {VideoUpload.sessionId}");

        recordingStartTime = DateTime.Now;

        OnRecordingStarted?.Invoke();
    }

    public static void StopRecording()
    {
        if (!isRecording) return;
        isRecording = false;
        Dispose();

        OnRecordingStopped?.Invoke();
    }

    static int frameCount = -1;

    private static long GetCurrentMilliseconds()
    {
        var interval = DateTime.Now - recordingStartTime;
        return interval.Days * 24 * 60 * 60 * 1000 +
               interval.Hours * 60 * 60 * 1000 +
               interval.Minutes * 60 * 1000 +
               interval.Seconds * 1000 +
               interval.Milliseconds;
    }

    static CommandBuffer commandBuffer;

    public static void CaptureFrames()
    {
        if (!isRecording) return;
        if (!Application.isPlaying) return;
        if (!InitializeCapturing()) return;

        //frameCount++;
        //EncodeScreenshotsToVideo();

        // Check if it's time to send a thumbnail
        //if (Time.frameCount % captureFrameRate == 0 && currentSubfolderPath != null)
        //{
        //    Texture2D screenshot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGBA32, false);
        //    screenshot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
        //    byte[] bytes = screenshot.EncodeToPNG();
        //    _ = VideoUpload.UploadThumbnailAsync(bytes, timeSinceRecordingStarted);
        //}

        // If there's a finished process, delete the corresponding screenshots
        while (processesQueue.Count > 0 && processesQueue.Peek().process.HasExited)
        {
            var captureInfo = processesQueue.Dequeue();

            System.Diagnostics.Process finishedProcess = captureInfo.process;
            string finishedSubfolderPath = Path.GetDirectoryName(finishedProcess.StartInfo.Arguments.Split('"')[1]);
            DeleteScreenshots(finishedSubfolderPath);

            string apiKey = DiscoPandaRecorderInfo.Asset.APIKEY;

            Task.Run(async () =>
            {
                await VideoUpload.UploadVideoAsync(Path.Combine(finishedSubfolderPath, "output.mp4"), captureInfo.startTime, captureInfo.endTime, apiKey);
            });             
        }
    }

    private static void EncodeScreenshotsToVideo()
    {
        var timeSinceRecordingStarted = GetCurrentMilliseconds();

        // Check if it's time to create a video from the screenshots
        if (frameCount > 0 && frameCount % (captureFrameRate * 10) == 0 && currentSubfolderPath != null)
        {
            var captureInfo = new CaptureInfo()
            {
                startTime = clipFragmentIndex * 10000,
                endTime = ++clipFragmentIndex * 10000,
                process = CreateVideoFromScreenshots(currentSubfolderPath)
            };

            processesQueue.Enqueue(captureInfo);
        }

        // Create a new subfolder for each 10-second segment
        if (currentSubfolderPath == null || frameCount % (captureFrameRate * 10) == 0)
        {
            frameCount = -1;
            chunkStartTime = timeSinceRecordingStarted;
            currentSubfolderPath = CreateNewSubfolder();
            currentFramePath = Path.Combine(currentSubfolderPath, "frame");

            if (renderTexture == null)
                renderTexture = new RenderTexture(screenResolutionWidth, screenResolutionHeight, 24);
        }
    }

    static NativeArray<byte> bytes;
    private static void OnCompleteReadback(AsyncGPUReadbackRequest request)
    {
        if (!isRecording)
            return;

        timeSinceLastFrame += Time.deltaTime;

        if (request.hasError)
        {
            Debug.LogError("Failed to read GPU texture");
            return;
        }

        if (bytes.IsCreated)
            bytes.Dispose();

        bytes = request.GetData<byte>();

        if (!bytes.IsCreated || bytes.Length == 0)
        {
            Debug.LogError("Failed to get byte array from readback data");
            return;
        }

        imageEncoderJobHandle.Complete();

        if (timeSinceLastFrame >= CaptureFrameDelay && frameCount >= 0)
        {
            while (timeSinceLastFrame >= CaptureFrameDelay)
            {
                Log($"Time.frameCount:{Time.frameCount} timeSinceLastFrame:{timeSinceLastFrame} CaptureFrameDelay:{CaptureFrameDelay}");

                string screenshotPath = GetScreenshotPath();
                WriteBytes(screenshotPath);

                timeSinceLastFrame -= CaptureFrameDelay;
                frameCount++;

                EncodeScreenshotsToVideo();
            }
        }

        EncodeToPPM(bytes);

        if (frameCount < 0)
            frameCount = 0;
    }

    static void WriteBytes(string path)
    {
        ppmBytes.CopyTo(managedArray);

        Task.Run(async () =>
        {
            await SaveByteArrayToFileAsync(managedArray, path);
        });
    }

    public static async Task SaveByteArrayToFileAsync(byte[] bytes, string path)
    {
        const int BufferSize = 65536;
        await using var fileStream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, FileOptions.Asynchronous);

        // Write the bytes to the file
        await fileStream.WriteAsync(bytes.AsMemory(0, bytes.Length));
    }

    private static void UploadThumbnail(byte[] encodedBytes)
    {
        // If it's time to send a thumbnail, do it now
        if (Time.frameCount % captureFrameRate == 0 && currentSubfolderPath != null)
        {
            _ = VideoUpload.UploadThumbnailAsync(encodedBytes, GetCurrentMilliseconds());
        }
    }

    private static string GetScreenshotPath()
    {
        return currentFramePath + frameCount + ".ppm";
    }

    private static void EncodeToPPM(NativeArray<byte> bytes)
    {
        var pixelCount = bytes.Length / 4;
        var headerLength = ppmHeader.Length;

        var headerJob = new CopyArrayJob()
        {
            input = ppmHeader,
            output = ppmBytes
        }.Schedule();

        imageEncoderJobHandle = new PPMEncoderJob()
        {
            headerLength = headerLength,
            input = bytes,
            output = ppmBytes
        }.Schedule(pixelCount, 64, headerJob);
    }

    [BurstCompile]
    public struct CopyArrayJob : IJob
    {
        [ReadOnly] public NativeArray<byte> input;
        [WriteOnly] public NativeArray<byte> output;

        public void Execute()
        {
            for (int i = 0; i < input.Length; i++)
            {
                output[i] = input[i];
            }
        }
    }

    [BurstCompile]
    public struct PPMEncoderJob : IJobParallelFor
    {
        [ReadOnly] public int headerLength;
        [ReadOnly, NativeDisableParallelForRestriction] public NativeArray<byte> input;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<byte> output;

        public void Execute(int i)
        {
            int inputIndex = (i * 4);
            int outputIndex = (i * 3) + headerLength;

            output[outputIndex] = input[inputIndex];
            output[outputIndex + 1] = input[inputIndex + 1];
            output[outputIndex + 2] = input[inputIndex + 2];
        }
    }

    private static string GetFFmpegArguments(string videoPath, string inputPath)
    {
        return $"-f image2 " +
               $"-r {captureFrameRate} " +
               $"-i \"{inputPath}\" " +
               $"-c:v libx264 " +
               $"-vf \"scale={videoResolutionWidth}:{videoResolutionHeight}\" " +
               $"-b:v {videoBitRate}k " +
               $"-pix_fmt yuv420p " +
               $"-movflags frag_keyframe+empty_moov+default_base_moof " +
               $"\"{videoPath}\"";
    }

    private static string CreateNewSubfolder()
    {
        string subfolderPath = Path.Combine(Application.persistentDataPath, tempFolderPath, $"{DateTime.Now:yyyyMMdd_HHmmss}");
        subfolderPath = subfolderPath.Replace("\\", "/");
        Directory.CreateDirectory(subfolderPath);
        Log($"CreateNewSubfolder:{subfolderPath}");
        return subfolderPath;
    }

    public static void Log(string message)
    {
#if ENABLE_DISCOPANDA_DEBUGGING
        Debug.Log(message);
#endif
    }

    private static System.Diagnostics.Process CreateVideoFromScreenshots(string subfolderPath)
    {
        string videoPath = Path.Combine(subfolderPath, "output.mp4").Replace("\\", "/");
        string inputPath = Path.Combine(subfolderPath, "frame%d.ppm").Replace("\\", "/");
        string ffmpegArguments = GetFFmpegArguments(videoPath, inputPath);

        System.Diagnostics.ProcessStartInfo startInfo = new System.Diagnostics.ProcessStartInfo(ffmpegPath, ffmpegArguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        System.Diagnostics.Process process = new System.Diagnostics.Process
        {
            StartInfo = startInfo
        };

        process.ErrorDataReceived += FFmpegLogHandler;
        process.OutputDataReceived += FFmpegLogHandler;

        process.Start();

        process.BeginErrorReadLine();
        process.BeginOutputReadLine();

        return process;
    }

    private static void FFmpegLogHandler(object sender, System.Diagnostics.DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
        {
            Log($"FFmpeg: {e.Data}");
        }
    }

    private static void DeleteScreenshots(string subfolderPath)
    {
        DirectoryInfo directoryInfo = new DirectoryInfo(subfolderPath);
        FileInfo[] screenshots = directoryInfo.GetFiles("*.ppm");

        foreach (FileInfo screenshot in screenshots)
        {
            File.Delete(screenshot.FullName);
        }
    }
}


public static class VideoUpload
{
    const string sessionVersion = "V1";

    public static string _sessionId;
    public static string sessionId
    {
        get { return sessionVersion + _sessionId; }
        set { _sessionId = value; }
    }

    private static string apiUrlVideoUpload = "https://fox2fi7x68.execute-api.eu-west-1.amazonaws.com/prod/video-upload";
    private static string apiUrlThumbnailUpload = "https://fox2fi7x68.execute-api.eu-west-1.amazonaws.com/prod/thumbnail-upload";

    public static async Task UploadVideoAsync(string videoPath, long startTime, long endTime, string apiKey)
    {
        DiscoPandaRecorder.Log($"UploadVideoAsync. {videoPath}");

        try
        {
            string presignedUploadUrl = await GetPresignedVideoUploadUrlAsync(startTime, endTime, apiKey);
            if (presignedUploadUrl == null)
            {
                Debug.LogError("Error getting presigned URL.");
                return;
            }

            DiscoPandaRecorder.Log($"presignedUploadUrl. {presignedUploadUrl}");

            string videoFilePath = $"{videoPath}";
            byte[] videoData;
            try
            {
                videoData = File.ReadAllBytes(videoFilePath);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error reading video file: {e.Message}");
                return;
            }

            bool uploadSuccess = await UploadVideoDataAsync(presignedUploadUrl, videoData);
            if (uploadSuccess)
            {
                DiscoPandaRecorder.Log("Video uploaded successfully.");
            }
            else
            {
                Debug.LogError("Error uploading video.");
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
        finally
        {
#if !ENABLE_DISCOPANDA_DEBUGGING
            if (File.Exists(videoPath))
                File.Delete(videoPath);

            string videoFolderPath = Path.GetDirectoryName(videoPath);
            if (Directory.Exists(videoFolderPath))
                Directory.Delete(videoFolderPath);
#endif
        }
    }

    public static async Task UploadThumbnailAsync(byte[] fileData, long time)
    {
        DiscoPandaRecorder.Log($"UploadThumbnailAsync.");

        try
        {
            string presignedUploadUrl = await GetPresignedThumbnailUploadUrlAsync(time);
            if (presignedUploadUrl == null)
            {
                Debug.LogError("Error getting presigned URL.");
                return;
            }

            DiscoPandaRecorder.Log($"presignedUploadUrl. {presignedUploadUrl}");

            bool uploadSuccess = await UploadPNGataAsync(presignedUploadUrl, fileData);
            if (uploadSuccess)
            {
                DiscoPandaRecorder.Log("File uploaded successfully.");
            }
            else
            {
                Debug.LogError("Error uploading file.");
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private static async Task<string> GetPresignedVideoUploadUrlAsync(long startTime, long endTime, string apiKey)
    {
        string url = $"{apiUrlVideoUpload}?api_key={apiKey}&session_id={sessionId}&start={startTime}&end={endTime}";
        using HttpClient httpClient = new HttpClient();
        HttpResponseMessage response = await httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            var responseString = await response.Content.ReadAsStringAsync();
            Debug.LogError($"Response error: {responseString} url: {url}");
            return null;
        }

        string responseBody = await response.Content.ReadAsStringAsync();
        var jsonResponse = JsonConvert.DeserializeObject<PresignedUrlData>(responseBody);
        DiscoPandaRecorder.Log(responseBody);
        string presignedUploadUrl = jsonResponse.Url;

        return presignedUploadUrl;
    }

    private static async Task<string> GetPresignedThumbnailUploadUrlAsync(long time)
    {
        string apiKey = DiscoPandaRecorderInfo.Asset.APIKEY;

        string url = $"{apiUrlThumbnailUpload}?api_key={apiKey}&session_id={sessionId}&time={time}";
        using HttpClient httpClient = new HttpClient();
        HttpResponseMessage response = await httpClient.GetAsync(url);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string responseBody = await response.Content.ReadAsStringAsync();
        var jsonResponse = JsonConvert.DeserializeObject<PresignedUrlData>(responseBody);
        DiscoPandaRecorder.Log(responseBody);
        string presignedUploadUrl = jsonResponse.Url;

        return presignedUploadUrl;
    }

    private static async Task<bool> UploadVideoDataAsync(string presignedUploadUrl, byte[] videoData)
    {
        DiscoPandaRecorder.Log($"UploadVideoDataAsync. {presignedUploadUrl} {videoData.Length}");

        using HttpClient httpClient = new HttpClient();
        using ByteArrayContent content = new ByteArrayContent(videoData);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("video/mp4");
        HttpResponseMessage response = await httpClient.PutAsync(presignedUploadUrl, content);

        return response.IsSuccessStatusCode;
    }

    private static async Task<bool> UploadPNGataAsync(string presignedUploadUrl, byte[] data)
    {
        DiscoPandaRecorder.Log($"UploadPNGataAsync. {presignedUploadUrl} {data.Length}");

        using HttpClient httpClient = new HttpClient();
        using ByteArrayContent content = new ByteArrayContent(data);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        HttpResponseMessage response = await httpClient.PutAsync(presignedUploadUrl, content);

        return response.IsSuccessStatusCode;
    }

    private class PresignedUrlData
    {
        public string Url { get; set; }
    }
}