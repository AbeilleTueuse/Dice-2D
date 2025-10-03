using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[CreateAssetMenu(fileName = "New Die", menuName = "Dice/Die")]
public class DieSO : ScriptableObject
{
    [SerializeField]
    private GameObject _prefab;

    [SerializeField, HideInInspector]
    private string _resourcePath;

    public GameObject Prefab => _prefab;
    public int Value => _value;
    public string ResourcePath => _resourcePath;

    [SerializeField]
    private int _value;

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (_prefab != null)
        {
            string path = AssetDatabase.GetAssetPath(_prefab);

            int index = path.IndexOf("Resources/");
            if (index >= 0)
            {
                path = path.Substring(index + "Resources/".Length);
                path = System.IO.Path.ChangeExtension(path, null);
                _resourcePath = path;
            }
            else
            {
                Debug.LogWarning($"{name}: Prefab must be inside a Resources folder for Photon.");
                _resourcePath = "";
            }
        }
    }
#endif
}
