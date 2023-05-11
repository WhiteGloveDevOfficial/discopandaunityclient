using System.IO;
using UnityEngine;

public class DiscoPandaRecorderInfo : ScriptableObject
{
    public static DiscoPandaRecorderInfo Asset
    {
        get
        {
            var asset = Resources.Load<DiscoPandaRecorderInfo>("DiscoPandaRecorderInfo");

#if UNITY_EDITOR
            if (asset == null)
            {
                var resourcesPath = Path.Combine(Application.dataPath, "Resources");
                
                if (!Directory.Exists(resourcesPath))
                    Directory.CreateDirectory(resourcesPath);

                asset = CreateInstance<DiscoPandaRecorderInfo>();
                UnityEditor.AssetDatabase.CreateAsset(asset, "Assets/Resources/DiscoPandaRecorderInfo.asset");
            }
#endif
            return asset;
        }
    }

    public string APIKEY;
}
