//#define ENABLE_DISCOPANDA_DEBUGGING
using System;
using System.IO;
using UnityEngine;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Net.Http;
using Newtonsoft.Json;

public static class DiscoPandaRecorder
{
    private static int captureFrameRate = 60;
    private static int videoResolutionWidth = 640;
    private static int videoResolutionHeight = 480;
    private static int videoBitRate = 1000;
    private static string tempFolderPath = "TempVideos";
    public static string ffmpegPath;

    private static bool isRecording;
    private static string currentSubfolderPath;

    private static DateTime recordingStartTime;
    private static long chunkStartTime;

    struct CaptureInfo
    { 
        public long startTime;
        public long endTime;
        public System.Diagnostics.Process process;
    }

    private static Queue<CaptureInfo> processesQueue = new Queue<CaptureInfo>();
    private static RenderTexture renderTexture;

    public static System.Action OnRecordingStarted { get; set; }
    public static System.Action OnRecordingStopped { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        // Set the capture frame rate
        Time.captureFramerate = captureFrameRate;
        // Ensure the temp folder exists
        var folderPath = Path.Combine(Application.persistentDataPath, tempFolderPath);

        // Remove old videos
        if (Directory.Exists(folderPath))
            Directory.Delete(folderPath, true);

        Directory.CreateDirectory(folderPath);
        StartRecording();
    }

    public static void StartRecording()
    {
        if (isRecording) return;
        isRecording = true;

        VideoUpload.sessionId = Guid.NewGuid().ToString();
        Debug.Log($"VideoUpload.sessionId  {VideoUpload.sessionId}");

        recordingStartTime = DateTime.Now;

        OnRecordingStarted?.Invoke();
    }

    public static void StopRecording()
    {
        if (!isRecording) return;
        isRecording = false;

        OnRecordingStopped?.Invoke();
    }

    static int frameCount = 0;

    private static long GetCurrentMilliseconds()
    {
        var interval = DateTime.Now - recordingStartTime;
        return interval.Days * 24 * 60 * 60 * 1000 +
               interval.Hours * 60 * 60 * 1000 +
               interval.Minutes * 60 * 1000 +
               interval.Seconds * 1000 +
               interval.Milliseconds;
    }

    public static void CaptureFrames()
    {
        if (!isRecording) return;

        if (!Application.isPlaying) return;

        frameCount++;
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

            Debug.Log($"CreateVideoFromScreenshots {chunkStartTime} {timeSinceRecordingStarted}");

            processesQueue.Enqueue(captureInfo);
        }

        // Create a new subfolder for each 10-second segment
        if (currentSubfolderPath == null || Time.frameCount % (captureFrameRate * 10) == 0)
        {
            frameCount = 0;
            chunkStartTime = timeSinceRecordingStarted;
            currentSubfolderPath = CreateNewSubfolder();
            renderTexture = new RenderTexture(videoResolutionWidth, videoResolutionHeight, 24);
        }

        // Set the camera's target texture to the RenderTexture
        Camera.main.targetTexture = renderTexture;

        // Render the scene using the camera
        Camera.main.Render();

        // Read the pixels from the RenderTexture and save them to the subfolder
        Texture2D screenshot = new Texture2D(videoResolutionWidth, videoResolutionHeight, TextureFormat.RGB24, false);
        RenderTexture.active = renderTexture;
        screenshot.ReadPixels(new Rect(0, 0, videoResolutionWidth, videoResolutionHeight), 0, 0);
        byte[] bytes = screenshot.EncodeToPNG();
        string screenshotPath = Path.Combine(currentSubfolderPath, $"frame{frameCount}.png");
        File.WriteAllBytes(screenshotPath, bytes);

        // Reset the camera's target texture
        Camera.main.targetTexture = null;

        // Check if it's time to send a thumbnail
        if (Time.frameCount % captureFrameRate == 0 && currentSubfolderPath != null)
        {
            _ = VideoUpload.UploadThumbnailAsync(bytes, timeSinceRecordingStarted);
        }

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

    private static string CreateNewSubfolder()
    {
        string subfolderPath = Path.Combine(Application.persistentDataPath, tempFolderPath, $"{DateTime.Now:yyyyMMdd_HHmmss}");
        subfolderPath = subfolderPath.Replace("\\", "/");
        Directory.CreateDirectory(subfolderPath);
        return subfolderPath;
    }

    private static System.Diagnostics.Process CreateVideoFromScreenshots(string subfolderPath)
    {
        string videoPath = Path.Combine(subfolderPath, "output.mp4").Replace("\\", "/");
        string inputPath = Path.Combine(subfolderPath, "frame%d.png").Replace("\\", "/");
        string ffmpegArguments = $"-r {captureFrameRate} -i \"{inputPath}\" -c:v libx264 -vf \"scale={videoResolutionWidth}:{videoResolutionHeight}\" -b:v {videoBitRate}k -pix_fmt yuv420p -movflags frag_keyframe+empty_moov+default_base_moof \"{videoPath}\"";

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
#if ENABLE_DISCOPANDA_DEBUGGING
        if (!string.IsNullOrEmpty(e.Data))
        {
            Debug.Log($"FFmpeg: {e.Data}");
        }
#endif
    }

    private static void DeleteScreenshots(string subfolderPath)
    {
        DirectoryInfo directoryInfo = new DirectoryInfo(subfolderPath);
        FileInfo[] screenshots = directoryInfo.GetFiles("*.png");

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