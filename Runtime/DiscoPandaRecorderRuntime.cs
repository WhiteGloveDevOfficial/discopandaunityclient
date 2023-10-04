using System.IO;
using UnityEngine;

namespace DiscoPanda
{
    public static class DiscoPandaRecorderRuntime
    {
#if !UNITY_EDITOR
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    public static void Initialize()
    {
        DiscoPandaRecorder.ffmpegPath = Path.Combine(Application.dataPath, "Resources", "ffmpeg.exe");
        DiscoPandaRecorderInstance.EnsureInstanceExists();
    }
#endif
    }
}