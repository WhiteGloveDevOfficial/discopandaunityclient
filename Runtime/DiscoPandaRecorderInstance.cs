using UnityEngine;

namespace DiscoPanda
{
    public class DiscoPandaRecorderInstance : MonoBehaviour
    {
        private static DiscoPandaRecorderInstance instance;

        public static void EnsureInstanceExists()
        {
            if (instance == null)
            {
                GameObject go = new GameObject("DiscoPandaRecorderInstance");
                instance = go.AddComponent<DiscoPandaRecorderInstance>();
                DontDestroyOnLoad(go);
            }
        }

        private void Awake()
        {
            DiscoPandaRecorder.StartRecording();
        }

        private void Update()
        {
            DiscoPandaRecorder.Update();
        }

        private void OnDestroy()
        {
            DiscoPandaRecorder.StopRecording();
        }
    }
}