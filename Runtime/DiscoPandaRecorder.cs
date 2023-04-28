using System;
using System.IO;
using UnityEngine;
using System.Collections.Generic;

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

    private static Queue<System.Diagnostics.Process> processesQueue = new Queue<System.Diagnostics.Process>();
    private static RenderTexture renderTexture;

    public static System.Action OnRecordingStarted { get; set; }
    public static System.Action OnRecordingStopped { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        // Set the capture frame rate
        Time.captureFramerate = captureFrameRate;
        // Ensure the temp folder exists
        Directory.CreateDirectory(Path.Combine(Application.dataPath, tempFolderPath));
        StartRecording();
    }

    public static void StartRecording()
    {
        if (isRecording) return;
        isRecording = true;

        OnRecordingStarted?.Invoke();
    }


    public static void StopRecording()
    {
        if (!isRecording) return;
        isRecording = false;

        OnRecordingStopped?.Invoke();
    }

    public static void CaptureFrames()
    {
        if (!isRecording) return;

        if (!Application.isPlaying) return;

        // Check if it's time to create a video from the screenshots and delete them
        if (Time.frameCount % (captureFrameRate * 10) == 0 && currentSubfolderPath != null)
        {
            System.Diagnostics.Process process = CreateVideoFromScreenshots(currentSubfolderPath);
            processesQueue.Enqueue(process);
        }

        // Create a new subfolder for each 10-second segment
        if (currentSubfolderPath == null || Time.frameCount % (captureFrameRate * 10) == 0)
        {
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
        string screenshotPath = Path.Combine(currentSubfolderPath, $"frame{Time.frameCount}.png");
        File.WriteAllBytes(screenshotPath, bytes);

        // Reset the camera's target texture
        Camera.main.targetTexture = null;

        // If there's a finished process, delete the corresponding screenshots
        while (processesQueue.Count > 0 && processesQueue.Peek().HasExited)
        {
            System.Diagnostics.Process finishedProcess = processesQueue.Dequeue();
            string finishedSubfolderPath = Path.GetDirectoryName(finishedProcess.StartInfo.Arguments.Split('"')[1]);
            DeleteScreenshots(finishedSubfolderPath);
        }
    }

    private static string CreateNewSubfolder()
    {
        string subfolderPath = Path.Combine(Application.dataPath, tempFolderPath, $"{DateTime.Now:yyyyMMdd_HHmmss}");
        subfolderPath = subfolderPath.Replace("\\", "/");
        Directory.CreateDirectory(subfolderPath);
        return subfolderPath;
    }

    private static System.Diagnostics.Process CreateVideoFromScreenshots(string subfolderPath)
    {
        string videoPath = Path.Combine(subfolderPath, "output.mp4").Replace("\\", "/");
        string inputPath = Path.Combine(subfolderPath, "frame%d.png").Replace("\\", "/");
        string ffmpegArguments = $"-r {captureFrameRate} -i \"{inputPath}\" -c:v libx264 -vf \"scale={videoResolutionWidth}:{videoResolutionHeight}\" -b:v {videoBitRate}k -pix_fmt yuv420p \"{videoPath}\"";

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

        foreach (FileInfo screenshot in screenshots)
        {
            File.Delete(screenshot.FullName);
        }
    }
}
