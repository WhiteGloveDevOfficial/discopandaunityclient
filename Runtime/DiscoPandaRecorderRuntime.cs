using System.IO;
using UnityEngine;

public static partial class DiscoPandaRecorderRuntime
{
#if !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void Initialize()
    {
        DiscoPandaRecorder.ffmpegPath = Path.Combine(Application.dataPath, "Resources", "ffmpeg-windows.exe");

        DiscoPandaRecorder.OnRecordingStarted = () =>
        {
            DiscoPandaRecorderInstance.EnsureInstanceExists();
        };
    }
#endif
}