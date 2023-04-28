using UnityEngine;

//[CreateAssetMenu(fileName = "DiscoPandaRecorderInfo", menuName = "DiscoPanda/Recorder Info", order = 1)]
public class DiscoPandaRecorderInfo : ScriptableObject
{
    public static DiscoPandaRecorderInfo Asset => Resources.Load<DiscoPandaRecorderInfo>("DiscoPandaRecorderInfo");

    public string APIKEY;
}
