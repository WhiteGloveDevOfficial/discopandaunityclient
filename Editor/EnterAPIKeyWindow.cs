using UnityEngine;
using UnityEditor;

public class EnterAPIKeyWindow : EditorWindow
{
    private static EnterAPIKeyWindow window;
    private static bool hasApiKeyChanged;

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
        var marginSize = 20; // Define your desired margin size
        var contentSpacing = 15; // Calculate the spacing between GUI sections

        GUIStyle labelStyle = new GUIStyle(GUI.skin.label); // Create a new GUIStyle based on the default label style.
        labelStyle.wordWrap = true; // Enable word wrapping.

        // Vertical Margin at the top
        GUILayout.Space(marginSize);

        GUILayout.BeginHorizontal();
        GUILayout.Space(marginSize);
        GUILayout.Label("Welcome to using DiscoPanda!", EditorStyles.boldLabel);
        GUILayout.Space(marginSize);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Space(marginSize);
        GUILayout.Label("If you have already created an account on our website, please copy and paste the API key from the website header here.", labelStyle);
        GUILayout.Space(marginSize);
        GUILayout.EndHorizontal();

        GUILayout.Space(contentSpacing);

        // API Key
        GUILayout.BeginHorizontal();
        GUILayout.Space(marginSize);
        EditorGUI.BeginChangeCheck();
        EditorGUIUtility.labelWidth = 70;
        var newKey = EditorGUILayout.TextField("API Key:", DiscoPandaRecorderInfo.Asset.APIKEY);
        if (EditorGUI.EndChangeCheck())
        {
            hasApiKeyChanged = true;
        }
        EditorGUI.BeginDisabledGroup(!hasApiKeyChanged);
        if (GUILayout.Button("Save", GUILayout.Width(80))) // You can adjust the width as needed
        {
            DiscoPandaRecorderInfo.Asset.APIKEY = newKey;
            EditorUtility.SetDirty(DiscoPandaRecorderInfo.Asset);
            AssetDatabase.SaveAssets();
            hasApiKeyChanged = false;
        }
        EditorGUI.EndDisabledGroup();
        GUILayout.Space(marginSize);
        GUILayout.EndHorizontal();

        GUILayout.Space(contentSpacing);
        GUILayout.Space(contentSpacing);

        GUILayout.BeginHorizontal();
        GUILayout.Space(marginSize);
        GUILayout.Label("No account yet?", EditorStyles.boldLabel);
        GUILayout.Space(marginSize);
        GUILayout.EndHorizontal();

        // Create new account
        GUILayout.BeginHorizontal();
        GUILayout.Space(marginSize);
        if (GUILayout.Button("Create a new account"))
        {
            Application.OpenURL("https://discopanda.whiteglove.dev");
        }
        GUILayout.Space(marginSize);
        GUILayout.EndHorizontal();

        GUILayout.Space(contentSpacing);

        GUILayout.BeginHorizontal();
        GUILayout.Space(marginSize);
        GUILayout.Label("Need help?", EditorStyles.boldLabel);
        GUILayout.Space(marginSize);
        GUILayout.EndHorizontal();

        // Create new account
        GUILayout.BeginHorizontal();
        GUILayout.Space(marginSize);
        if (GUILayout.Button("Join our Discord Server!!"))
        {
            Application.OpenURL("https://discord.gg/7pYEYFBA8n");
        }
        GUILayout.Space(marginSize);
        GUILayout.EndHorizontal();
    }

    private void OnDestroy()
    {
        window = null;
    }

}
