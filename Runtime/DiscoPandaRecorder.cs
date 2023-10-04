//#define ENABLE_DISCOPANDA_DEBUGGING
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Collections;
using System.Reflection;

namespace DiscoPanda
{
    public static class DiscoPandaRecorder
    {
        public static string inputFileExtension = "ppm";
        static int captureFrameRate = 30;
        static int videoResolutionWidth = 640;
        static int videoResolutionHeight = 480;
        static int videoBitRate = 1000;
        static string tempFolderPath = "TempVideos";

        static Queue<CaptureInfo> processesQueue = new Queue<CaptureInfo>();
        static IRecorder recorder;

        static bool isRecording;
        static string currentSubfolderPath;
        static string currentFramePath;

        static float timeSinceLastFrame;
        static int clipFragmentIndex;
        static int frameCount = -1;

        public static string ffmpegPath;

        struct CaptureInfo
        {
            public long startTime;
            public long endTime;
            public Process process;
        }

        public static Camera CaptureCamera { get; set; }
        public static Action OnRecordingStarted { get; set; }
        public static Action OnRecordingStopped { get; set; }

        static float CaptureFrameDelay => 1f / captureFrameRate;

        public static void StartRecording()
        {
            Initialize();

            if (isRecording) return;
            isRecording = true;

            VideoUploader.sessionId = Guid.NewGuid().ToString();

            OnRecordingStarted?.Invoke();
            recorder.StartRecording();
            StartCapturingNewClip();
        }

        public static void Update()
        {
            if (!isRecording) return;
            if (!Application.isPlaying) return;
            if (!InitializeCapturing()) return;

            // If there's a finished process, delete the corresponding screenshots
            while (processesQueue.Count > 0 && processesQueue.Peek().process.HasExited)
            {
                var captureInfo = processesQueue.Dequeue();

                Process finishedProcess = captureInfo.process;
                string finishedSubfolderPath = Path.GetDirectoryName(finishedProcess.StartInfo.Arguments.Split('"')[1]);
                DeleteScreenshots(finishedSubfolderPath);

                string apiKey = DiscoPandaRecorderInfo.Asset.APIKEY;

                Task.Run(async () =>
                {
                    await VideoUploader.UploadVideoAsync(Path.Combine(finishedSubfolderPath, "output.mp4"), captureInfo.startTime, captureInfo.endTime, apiKey);
                });
            }

            recorder.Update();
        }

        public static void StopRecording()
        {
            if (!isRecording) return;
            isRecording = false;

            recorder.StopRecording();
            OnRecordingStopped?.Invoke();
        }

        public static void SetCaptureCamera(Camera camera)
        {
            CaptureCamera = camera;
            recorder.CaptureSourceChanged(camera);
        }

        static void Initialize()
        {
            DeleteTempFolder();

            recorder = CreateRecorderInstance();
            recorder.onCompleteFrameCapture += OnCompleteFrameCapture;

            Directory.CreateDirectory(GetTempFolderPath());
        }

        static IRecorder CreateRecorderInstance()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type type in assembly.GetTypes())
                {
                    if (!type.IsInterface && !type.IsAbstract && typeof(IRecorder).IsAssignableFrom(type))
                    {
                        // Find a constructor that has no parameters
                        ConstructorInfo constructor = type.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null);
                        if (constructor != null)
                        {
                            return (IRecorder)constructor.Invoke(null);
                        }
                    }
                }
            }

            throw new InvalidOperationException("No class implementing IRecorder found.");
        }

        static bool InitializeCapturing()
        {
            if (CaptureCamera != null)
                return true;

            if (Camera.main == null)
                return false;

            SetCaptureCamera(Camera.main);
            return true;
        }

        static void OnCompleteFrameCapture()
        {
            Log("OnCompleteFrameCapture");
            timeSinceLastFrame += Time.deltaTime;

            if (timeSinceLastFrame >= CaptureFrameDelay && frameCount >= 0)
            {
                while (timeSinceLastFrame >= CaptureFrameDelay)
                {
                    string screenshotPath = GetScreenshotPath();

                    //Log($"Save frame {screenshotPath}");

                    recorder.SaveFrameCaptureToDisk(screenshotPath);
                    EncodeScreenshotsToVideo();

                    timeSinceLastFrame -= CaptureFrameDelay;
                    frameCount++;
                }
            }

            if (frameCount < 0)
                frameCount = 0;
        }

        static void StartCapturingNewClip()
        {
            Log("StartCapturingNewClip");
            // Create a new subfolder for each 10-second segment
            if (currentSubfolderPath == null || frameCount % (captureFrameRate * 10) == 0)
            {
                frameCount = -1;
                currentSubfolderPath = CreateNewSubfolder();
                currentFramePath = Path.Combine(currentSubfolderPath, "frame");
            }
        }

        static void EncodeScreenshotsToVideo()
        {
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
                StartCapturingNewClip();
            }
        }

        static Process CreateVideoFromScreenshots(string subfolderPath)
        {
            string videoPath = Path.Combine(subfolderPath, "output.mp4").Replace("\\", "/");
            string inputPath = Path.Combine(subfolderPath, $"frame%d.{inputFileExtension}").Replace("\\", "/");
            string ffmpegArguments = GetFFmpegArguments(videoPath, inputPath);

            ProcessStartInfo startInfo = new ProcessStartInfo(ffmpegPath, ffmpegArguments)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            Process process = new Process
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

        static string GetFFmpegArguments(string videoPath, string inputPath)
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

        static void FFmpegLogHandler(object sender, System.Diagnostics.DataReceivedEventArgs e)
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                Log($"FFmpeg: {e.Data}");
            }
        }

        static string GetScreenshotPath()
        {
            return currentFramePath + frameCount + "." + inputFileExtension;
        }

        static string CreateNewSubfolder()
        {
            string subfolderPath = Path.Combine(Application.persistentDataPath, tempFolderPath, $"{DateTime.Now:yyyyMMdd_HHmmss}");
            subfolderPath = subfolderPath.Replace("\\", "/");
            Directory.CreateDirectory(subfolderPath);
            Log($"CreateNewSubfolder:{subfolderPath}");
            return subfolderPath;
        }

        static void DeleteScreenshots(string subfolderPath)
        {
            DirectoryInfo directoryInfo = new DirectoryInfo(subfolderPath);
            FileInfo[] screenshots = directoryInfo.GetFiles($"*.{inputFileExtension}");

            foreach (FileInfo screenshot in screenshots)
            {
                File.Delete(screenshot.FullName);
            }
        }

        static void DeleteTempFolder()
        {
            string folderPath = GetTempFolderPath();

            if (Directory.Exists(folderPath))
                Directory.Delete(folderPath, true);
        }

        static string GetTempFolderPath()
        {
            return Path.Combine(Application.persistentDataPath, tempFolderPath);
        }

        static void Log(string message)
        {
#if ENABLE_DISCOPANDA_DEBUGGING
            UnityEngine.Debug.Log(message);
#endif
        }
    }
}