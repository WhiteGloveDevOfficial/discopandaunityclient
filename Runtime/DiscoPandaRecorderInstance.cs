using UnityEngine;

public class DiscoPandaRecorderInstance : MonoBehaviour
{
    private static DiscoPandaRecorderInstance _instance;

    public static void EnsureInstanceExists()
    {
        if (_instance == null)
        {
            GameObject go = new GameObject("DiscoPandaRecorderInstance");
            _instance = go.AddComponent<DiscoPandaRecorderInstance>();
            DontDestroyOnLoad(go);
        }
    }

    private void Update()
    {
        DiscoPandaRecorder.CaptureFrames();
    }
}
