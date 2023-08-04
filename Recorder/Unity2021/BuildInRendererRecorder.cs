#if !USING_URP && !USING_HDRP
using UnityEngine.Rendering;
using UnityEngine;
using Unity.Collections;
using Unity.Jobs;
using System.IO;
using System;
using System.Threading.Tasks;

namespace DiscoPanda
{
    public class BuildInRendererRecorder : IRecorder
    {
        int screenResolutionWidth = Screen.width;
        int screenResolutionHeight = Screen.height;

        int videoResolutionWidth = Screen.width;
        int videoResolutionHeight = Screen.height;

        PPMEncoder ppmEncoder;
        CommandBuffer commandBuffer;
        RenderTexture renderTexture;
        Camera CaptureCamera;

        NativeArray<byte> bytes;

        byte[] managedArray;
        bool isRecording;

        public event Action<NativeArray<byte>> onCompleteFrameCapture;

        public void StartRecording()
        {
            isRecording = true;
            Initialize();
        }

        public void StopRecording()
        {
            isRecording = false;
            Dispose();
        }

        public void CaptureSourceChanged(Camera camera)
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

        public void SaveFrameCaptureToDisk(string screenshotPath)
        {
            ppmEncoder.CopyTo(managedArray);
            FileUtility.WriteBytes(managedArray, screenshotPath);
        }

        void Initialize()
        {
            InitializeCommandBuffer();

            ppmEncoder = new PPMEncoder(videoResolutionWidth, screenResolutionHeight);
            managedArray = new byte[ppmEncoder.Size];

        }

        void InitializeCommandBuffer()
        {
            if (commandBuffer != null)
                return;

            InitializeRenderTexture();

            commandBuffer = new CommandBuffer();
            commandBuffer.name = "CaptureScreen";
            commandBuffer.Blit(null, renderTexture);
            commandBuffer.RequestAsyncReadback(renderTexture, OnCompleteReadback);
        }

        void InitializeRenderTexture()
        {
            if (renderTexture == null)
            {
                RenderTextureFormat format = RenderTextureFormat.ARGB32;
                renderTexture = new RenderTexture(screenResolutionWidth, screenResolutionHeight, 24, format);
            }
        }

        void Dispose()
        {
            ppmEncoder.Dispose();

            if (bytes.IsCreated)
                bytes.Dispose();
        }

        void OnCompleteReadback(AsyncGPUReadbackRequest request)
        {
            if (!isRecording)
                return;

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

            ppmEncoder.CompleteJobs();

            if (onCompleteFrameCapture != null)
                onCompleteFrameCapture.Invoke(bytes);

            ppmEncoder.Encode(bytes);
        }
    }
}
#endif