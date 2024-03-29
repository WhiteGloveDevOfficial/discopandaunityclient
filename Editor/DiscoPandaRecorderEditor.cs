using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;
using UnityEditor.Callbacks;

namespace DiscoPanda
{
    [InitializeOnLoad]
    static class DiscoPandaRecorderEditor
    {
        static DiscoPandaRecorderEditor()
        {
            EditorApplication.delayCall = () => { CheckForAPIKEY(); };
            InitializeEditorSupport();

            EditorApplication.playModeStateChanged += EditorApplication_playModeStateChanged;
        }

        private static void EditorApplication_playModeStateChanged(PlayModeStateChange state)
        {
            switch (state)
            {
                case PlayModeStateChange.ExitingPlayMode:
                    DiscoPandaRecorder.StopRecording();
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    DiscoPandaRecorder.StartRecording();
                    break;
                default:
                    break;
            }
        }

        private static void CheckForAPIKEY()
        {
            if (string.IsNullOrEmpty(DiscoPandaRecorderInfo.Asset.APIKEY))
            {
                if (SessionState.GetBool("DiscoPandaRecorder.EnterAPIKeyWindowShown", false))
                    return;

                SessionState.SetBool("DiscoPandaRecorder.EnterAPIKeyWindowShown", true);

                EnterAPIKeyWindow existingWindow = Resources.FindObjectsOfTypeAll<EnterAPIKeyWindow>().FirstOrDefault();
                if (existingWindow != null)
                {
                    existingWindow.Focus();
                }
                else
                {
                    EnterAPIKeyWindow.ShowWindow();
                }
            }
        }

        private static void InitializeEditorSupport()
        {
            var recorderInfo = DiscoPandaRecorderInfo.Asset;

            if (recorderInfo == null)
            {
                Debug.LogError("DiscoPandaRecorderInfo not found in Resources folder. Please add it to the Resources folder.");
                return;
            }

            var operatingSystem = "windows";
            if (SystemInfo.operatingSystem.Contains("Ubuntu"))
            {
                operatingSystem = "ubuntu";
            }

            var assets = AssetDatabase.FindAssets("ffmpeg-" + operatingSystem);
            var assetPath = AssetDatabase.GUIDToAssetPath(assets[0]);
            DiscoPandaRecorder.ffmpegPath = Path.GetFullPath(assetPath);

            if (!File.Exists(DiscoPandaRecorder.ffmpegPath))
            {
                Debug.LogError("ffmpeg.exe not found in the package's Resources folder. Please add it to the Resources folder.");
            }

            DiscoPandaRecorder.OnRecordingStarted = () =>
            {
                //Debug.Log("DiscoPandaRecorder.OnRecordingStarted");
                EditorApplication.update += DiscoPandaRecorder.Update;
            };

            DiscoPandaRecorder.OnRecordingStopped = () =>
            {
                //Debug.Log("DiscoPandaRecorder.OnRecordingStopped");
                EditorApplication.update -= DiscoPandaRecorder.Update;
            };
        }

        [PostProcessBuild]
        public static void OnPostprocessBuild(BuildTarget target, string pathToBuiltProject)
        {
            // Copy the ffmpeg into the builds Resources folder
            var ResourcesFolderPath = Path.Combine(pathToBuiltProject.Replace(".exe", "_Data"), "Resources", "ffmpeg.exe");
            File.Copy(DiscoPandaRecorder.ffmpegPath, ResourcesFolderPath, overwrite: true);
        }
    }
}