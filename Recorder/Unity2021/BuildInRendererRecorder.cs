#if !USING_URP && !USING_HDRP
using UnityEngine;
using System;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;
using static UnityEngine.GraphicsBuffer;

namespace DiscoPanda
{
    public class BuildInRendererRecorder : IRecorder
    {
        PPMEncoder ppmEncoder;
        RenderTexture sourceRT;
        RenderTexture targetRT;

        Material letterboxMaterial;

        byte[] bytes;        
        int recordingWidth = 640;
        int recordingHeight = 480;
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

            ppmEncoder = new PPMEncoder(recordingWidth, recordingHeight);
            bytes = new byte[ppmEncoder.Size];

            RenderTextureFormat format = RenderTextureFormat.ARGB32;

            Vector2 gameViewSize = GetMainGameViewSize();
            int sourceWidth = (int)gameViewSize.x;
            int sourceHeight = (int)gameViewSize.y;

            sourceRT = new RenderTexture(sourceWidth, sourceHeight, 0, format);
            targetRT = new RenderTexture(recordingWidth, recordingHeight, 0, format);

            letterboxMaterial = new Material(Shader.Find("Custom/LetterboxShader"));
            letterboxMaterial.SetVector("_SourceRes", new Vector4(sourceRT.width, sourceRT.height, 0, 0));
            letterboxMaterial.SetVector("_TargetRes", new Vector4(targetRT.width, targetRT.height, 0, 0));
        }

        public void StopRecording()
        {
            Log("StopRecording");

            isRecording = false;

            ppmEncoder.Dispose();

            if (targetRT != null) Object.Destroy(targetRT);
            if (sourceRT != null) Object.Destroy(sourceRT);
        }

        public void Update()
        {
            CaptureFrame();
        }

        Vector2 GetMainGameViewSize()
        {
            Vector2 resolution = new Vector2(Screen.width, Screen.height);

#if UNITY_EDITOR
            System.Type T = System.Type.GetType("UnityEditor.GameView,UnityEditor");
            System.Reflection.MethodInfo GetSizeOfMainGameView = T.GetMethod("GetSizeOfMainGameView", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            System.Object Res = GetSizeOfMainGameView.Invoke(null, null);
            resolution = (Vector2)Res;
#endif

            return resolution;
        }

        void CaptureFrame()
        {
            ScreenCapture.CaptureScreenshotIntoRenderTexture(sourceRT);
            Graphics.Blit(sourceRT, targetRT, letterboxMaterial);
            AsyncGPUReadback.Request(targetRT, 0, OnCompleteReadback);
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