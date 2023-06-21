using System.IO;
using UnityEngine;

public static class DiscoPandaRecorderRuntime
{
#if !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Initialize()
    {
        DiscoPandaRecorder.ffmpegPath = Path.Combine(Application.dataPath, "Resources", "ffmpeg-windows.exe");
        DiscoPandaRecorderInstance.EnsureInstanceExists();
    }
#endif
}