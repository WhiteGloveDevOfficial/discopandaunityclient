using UnityEngine;
using UnityEditor;

public class EnterAPIKeyWindow : EditorWindow
{
    private static EnterAPIKeyWindow window;

    private string apiKey;

    [MenuItem("Window/DiscoPanda/Enter API Key")]
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
        var buttonWidth = 200;

        GUILayout.BeginVertical(); // Start Vertical Group
        EditorGUI.BeginChangeCheck();
        apiKey = EditorGUILayout.TextField("API Key:", apiKey);
        
        if (EditorGUI.EndChangeCheck()) 
        {
            var recorderInfo = DiscoPandaRecorderInfo.Asset;
            recorderInfo.APIKEY = apiKey;
            EditorUtility.SetDirty(recorderInfo);
            AssetDatabase.SaveAssets();
        }

        EditorGUILayout.Space();

        // Displaying message with clickable link to create a new account for API Key
        GUILayout.BeginHorizontal(); // Start Horizontal Group for 'Create a new account' button
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Create a new account", GUILayout.Width(buttonWidth)))
        {
            Application.OpenURL("https://discopanda.whiteglove.dev");
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal(); // End Horizontal Group for 'Create a new account' button

        EditorGUILayout.Space();

        GUILayout.BeginHorizontal(); // Start Horizontal Group for 'Join our Discord Server!' button
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Join our Discord Server!", GUILayout.Width(buttonWidth)))
        {
            Application.OpenURL("https://discord.gg/7pYEYFBA8n");
        }
        GUILayout.FlexibleSpace();
        GUILayout.EndHorizontal(); // End Horizontal Group for 'Join our Discord Server!' button

        GUILayout.EndVertical(); // End Vertical Group
    }


    private void OnDestroy()
    {
        window = null;
    }

}
