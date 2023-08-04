using UnityEngine;
using Unity.Collections;

public interface IRecorder
{
    void StartRecording();
    void StopRecording();
    void Update();
    event System.Action onCompleteFrameCapture;
    void SaveFrameCaptureToDisk(string screenshotPath);
    void CaptureSourceChanged(Camera camera);
}
