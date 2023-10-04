#if !USING_URP && !USING_HDRP
using UnityEngine;
using System;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace DiscoPanda
{
    public class BuildInRendererRecorder : IRecorder
    {
        PPMEncoder ppmEncoder;
        RenderTexture grabRT;
        RenderTexture flipRT;

        Vector2 scale;
        Vector2 offset;

        byte[] bytes;
        int width = 640;
        int height = 480;
        bool isRecording;

        public event Action onCompleteFrameCapture;

        public void StartRecording()
        {
            Log("StartRecording");

            isRecording = true;
            Initialize();
        }

        private void Initialize()
        {
            Log("Initialize");

            ppmEncoder = new PPMEncoder(width, height);
            bytes = new byte[ppmEncoder.Size];

            scale = new Vector2(1, 1);
            offset = new Vector2(0, 0);

            RenderTextureFormat format = RenderTextureFormat.ARGB32;

            grabRT = new RenderTexture(width, height, 0, format);
            flipRT = new RenderTexture(width, height, 0, format);
        }

        public void StopRecording()
        {
            Log("StopRecording");

            isRecording = false;

            ppmEncoder.Dispose();

            if (flipRT != null) Object.Destroy(flipRT);
            if (grabRT != null) Object.Destroy(grabRT);
        }

        public void Update()
        {
            CaptureFrame();
        }

        void CaptureFrame()
        {
            ScreenCapture.CaptureScreenshotIntoRenderTexture(grabRT);
            Graphics.Blit(grabRT, flipRT, scale, offset);
            AsyncGPUReadback.Request(flipRT, 0, OnCompleteReadback);
        }

        void OnCompleteReadback(AsyncGPUReadbackRequest request)
        {
            Log($"OnCompleteReadback {Time.frameCount}");
            if (!isRecording)
                return;

            var buffer = request.GetData<byte>();

            //Log("ppmEncoder.CompleteJobs");
            ppmEncoder.CompleteJobs();
            ppmEncoder.CopyTo(bytes);

            //Log("onCompleteFrameCapture.Invoke");
            if (onCompleteFrameCapture != null)
                onCompleteFrameCapture.Invoke();

            //Log("ppmEncoder.Encode");
            ppmEncoder.Encode(buffer);
        }

        public void CaptureSourceChanged(Camera camera)
        {
        }

        public void SaveFrameCaptureToDisk(string screenshotPath)
        {
            //Debug.Log("SaveFrameCaptureToDisk");

            FileUtility.WriteBytes(bytes, screenshotPath);
        }
        static void Log(string message)
        {
#if ENABLE_DISCOPANDA_DEBUGGING
            UnityEngine.Debug.Log(message);
#endif
        }
    }
}
#endif