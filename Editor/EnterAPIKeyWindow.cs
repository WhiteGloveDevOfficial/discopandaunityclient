using UnityEngine;
using UnityEditor;

public class EnterAPIKeyWindow : EditorWindow
{
    private static EnterAPIKeyWindow window;

    private string apiKey;

    [MenuItem("DiscoPanda/Enter API Key")]
    public static void ShowWindow()
    {
        if (window == null)
        {
            window = EditorWindow.GetWindow<EnterAPIKeyWindow>();
            window.titleContent = new GUIContent("DiscoPanda Info");
        }
        else
        {
            window.Focus();
        }
    }

    private void OnGUI()
    {
        apiKey = EditorGUILayout.TextField("API Key:", apiKey);

        if (GUILayout.Button("Save"))
        {
            var recorderInfo = DiscoPandaRecorderInfo.Asset;
            recorderInfo.APIKEY = apiKey;
            EditorUtility.SetDirty(recorderInfo);
            AssetDatabase.SaveAssets();
            Close();
        }
    }

    private void OnDestroy()
    {
        window = null;
    }

}
