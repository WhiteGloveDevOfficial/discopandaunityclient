//#define ENABLE_DISCOPANDA_DEBUGGING
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
    private static int captureFrameRate = 60;
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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        Log($"Capture resolution {videoResolutionWidth} {videoResolutionHeight}");

        // Set the capture frame rate
        Time.captureFramerate = captureFrameRate;
        // Ensure the temp folder exists
        var folderPath = Path.Combine(Application.persistentDataPath, tempFolderPath);

        // Remove old videos
        if (Directory.Exists(folderPath))
            Directory.Delete(folderPath, true);

        if (renderTexture == null)
        {
            RenderTextureFormat format = RenderTextureFormat.ARGB32;
            renderTexture = new RenderTexture(screenResolutionWidth, screenResolutionHeight, 24, format);
        }

        if (commandBuffer == null)
            commandBuffer = new CommandBuffer();

        commandBuffer.name = "CaptureScreen";
        commandBuffer.Blit(null, renderTexture);
        commandBuffer.RequestAsyncReadback(renderTexture, OnCompleteReadback);

        ppmBytes = new NativeArray<byte>(18 + (videoResolutionWidth * videoResolutionHeight) * 3, Allocator.Persistent);
        managedArray = new byte[ppmBytes.Length];

        var headerString = $"P6\n{videoResolutionWidth} {videoResolutionHeight}\n255\n";
        var headerBytes = System.Text.Encoding.ASCII.GetBytes(headerString);
        ppmHeader = new NativeArray<byte>(headerBytes, Allocator.Persistent);

        Directory.CreateDirectory(folderPath);
        StartRecording();
    }

    public static void Dispose()
    {
        if (ppmBytes.IsCreated)
            ppmBytes.Dispose();

        if (ppmHeader.IsCreated)
            ppmHeader.Dispose();
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

    public static void StartRecording()
    {
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
        var timeSinceRecordingStarted = GetCurrentMilliseconds();

        // Check if it's time to create a video from the screenshots
        if (Time.frameCount % (captureFrameRate * 10) == 0 && currentSubfolderPath != null)
        {
            var captureInfo = new CaptureInfo()
            {
                startTime = chunkStartTime,
                endTime = timeSinceRecordingStarted,
                process = CreateVideoFromScreenshots(currentSubfolderPath)
            };

            Log($"CreateVideoFromScreenshots {chunkStartTime} {timeSinceRecordingStarted}");

            processesQueue.Enqueue(captureInfo);
        }

        // Create a new subfolder for each 10-second segment
        if (currentSubfolderPath == null || Time.frameCount % (captureFrameRate * 10) == 0)
        {
            frameCount = -1;
            chunkStartTime = timeSinceRecordingStarted;
            currentSubfolderPath = CreateNewSubfolder();
            currentFramePath = currentSubfolderPath + @"\frame";

            if (renderTexture == null)
                renderTexture = new RenderTexture(screenResolutionWidth, screenResolutionHeight, 24);
        }

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

            _ = VideoUpload.UploadVideoAsync(Path.Combine(finishedSubfolderPath, "output.mp4"), captureInfo.startTime, captureInfo.endTime);
        }
    }

    private static void OnCompleteReadback(AsyncGPUReadbackRequest request)
    {
        if (request.hasError)
        {
            Debug.LogError("Failed to read GPU texture");
            return;
        }

        NativeArray<byte> bytes = request.GetData<byte>();

        if (!bytes.IsCreated || bytes.Length == 0)
        {
            Debug.LogError("Failed to get byte array from readback data");
            return;
        }

        imageEncoderJobHandle.Complete();

        if (frameCount >= 0)
        {
            string screenshotPath = GetScreenshotPath();
            WriteBytes(screenshotPath);
        }

        EncodeToPPM(bytes);
        bytes.Dispose();

        //UploadThumbnail(encodedBytes);

        frameCount++;
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
        return $"-f image2 -r {captureFrameRate} -i \"{inputPath}\" -c:v libx264 -vf \"scale={videoResolutionWidth}:{videoResolutionHeight}\" -b:v {videoBitRate}k -pix_fmt yuv420p \"{videoPath}\"";
    }

    private static string CreateNewSubfolder()
    {
        string subfolderPath = Path.Combine(Application.persistentDataPath, tempFolderPath, $"{DateTime.Now:yyyyMMdd_HHmmss}");
        subfolderPath = subfolderPath.Replace("\\", "/");
        Directory.CreateDirectory(subfolderPath);
        Log($"CreateNewSubfolder:{subfolderPath}");
        return subfolderPath;
    }

    private static void Log(string message)
    {
#if ENABLE_DISCOPANDA_DEBUGGING
        Debug.Log(message);
#endif
    }

    private static System.Diagnostics.Process CreateVideoFromScreenshots(string subfolderPath)
    {
        string videoPath = Path.Combine(subfolderPath, "output.mp4").Replace("\\", "/");
        string inputPath = Path.Combine(subfolderPath, $"frame%d.ppm").Replace("\\", "/");

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

        bool isFirst = true;
        foreach (FileInfo screenshot in screenshots)
        {
            if (isFirst)
            {
                isFirst = false;
                continue;
            }

            File.Delete(screenshot.FullName);
        }
    }
}


public static class VideoUpload
{
    public static string sessionId;
    private static string apiUrlVideoUpload = "https://fox2fi7x68.execute-api.eu-west-1.amazonaws.com/prod/video-upload";
    private static string apiUrlThumbnailUpload = "https://fox2fi7x68.execute-api.eu-west-1.amazonaws.com/prod/thumbnail-upload";

    public static async Task UploadVideoAsync(string videoPath, long startTime, long endTime)
    {
        Debug.Log($"UploadVideoAsync. {videoPath}");

        try
        {
            string presignedUploadUrl = await GetPresignedVideoUploadUrlAsync(startTime, endTime);
            if (presignedUploadUrl == null)
            {
                Debug.LogError("Error getting presigned URL.");
                return;
            }

            Debug.Log($"presignedUploadUrl. {presignedUploadUrl}");

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
                Debug.Log("Video uploaded successfully.");
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
    }

    public static async Task UploadThumbnailAsync(byte[] fileData, long time)
    {
        Debug.Log($"UploadThumbnailAsync.");

        try
        {
            string presignedUploadUrl = await GetPresignedThumbnailUploadUrlAsync(time);
            if (presignedUploadUrl == null)
            {
                Debug.LogError("Error getting presigned URL.");
                return;
            }

            Debug.Log($"presignedUploadUrl. {presignedUploadUrl}");

            bool uploadSuccess = await UploadPNGataAsync(presignedUploadUrl, fileData);
            if (uploadSuccess)
            {
                Debug.Log("File uploaded successfully.");
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

    private static async Task<string> GetPresignedVideoUploadUrlAsync(long startTime, long endTime)
    {
        string apiKey = DiscoPandaRecorderInfo.Asset.APIKEY;

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
        Debug.Log(responseBody);
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
        Debug.Log(responseBody);
        string presignedUploadUrl = jsonResponse.Url;

        return presignedUploadUrl;
    }

    private static async Task<bool> UploadVideoDataAsync(string presignedUploadUrl, byte[] videoData)
    {
        Debug.Log($"UploadVideoDataAsync. {presignedUploadUrl} {videoData.Length}");

        using HttpClient httpClient = new HttpClient();
        using ByteArrayContent content = new ByteArrayContent(videoData);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("video/mp4");
        HttpResponseMessage response = await httpClient.PutAsync(presignedUploadUrl, content);

        return response.IsSuccessStatusCode;
    }

    private static async Task<bool> UploadPNGataAsync(string presignedUploadUrl, byte[] data)
    {
        Debug.Log($"UploadPNGataAsync. {presignedUploadUrl} {data.Length}");

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