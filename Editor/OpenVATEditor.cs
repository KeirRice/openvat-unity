using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;

public class OpenVATEditor : EditorWindow
{
    private enum MaterialType
    {
        Auto,    // PBR when a basecolor texture is present, otherwise Basic
        PBR,     // openVAT_decoder: PBR texture inputs
        Basic    // openVAT_decoder_basic: VAT only
    }

    private string folderPath = "Assets/OpenVATContent";
    private MaterialType materialType = MaterialType.Auto;

    [MenuItem("Tools/OpenVAT Editor")]
    public static void ShowWindow()
    {
        GetWindow<OpenVATEditor>("OpenVAT Editor");
    }

    private void OnGUI()
    {
        GUILayout.Label("OpenVAT Content Importer", EditorStyles.boldLabel);

        folderPath = EditorGUILayout.TextField("Folder Path", folderPath);
        materialType = (MaterialType)EditorGUILayout.EnumPopup("Material Type", materialType);

        if (GUILayout.Button("Process OpenVAT Content"))
        {
            ProcessOpenVATContent();
        }
    }

    private void ProcessOpenVATContent()
    {
        if (!Directory.Exists(folderPath))
        {
            UnityEngine.Debug.LogError("Folder does not exist: " + folderPath);
            return;
        }

        // Support FBX, GLB, and GLTF
        string[] fbxFiles = Directory.GetFiles(folderPath, "*.fbx");
        string[] glbFiles = Directory.GetFiles(folderPath, "*.glb");
        string[] gltfFiles = Directory.GetFiles(folderPath, "*.gltf");
        string[] modelFiles = fbxFiles.Concat(glbFiles).Concat(gltfFiles).ToArray();

        // Support PNG and EXR textures
        string[] pngTextures = Directory.GetFiles(folderPath, "*_vat.png");
        string[] exrTextures = Directory.GetFiles(folderPath, "*_vat.exr");
        string[] vatTextures = pngTextures.Concat(exrTextures).ToArray();

        string[] jsonFiles = Directory.GetFiles(folderPath, "*.json");

        if (modelFiles.Length == 0 || vatTextures.Length == 0 || jsonFiles.Length == 0)
        {
            UnityEngine.Debug.LogError("Required files are missing in the folder.");
            return;
        }

        string modelPath = modelFiles[0];
        string vatTexturePath = vatTextures[0];
        string jsonPath = jsonFiles[0];

        // Re-import VAT and optional normal textures; OpenVATTexturePostprocessor applies their settings
        AssetDatabase.ImportAsset(vatTexturePath, ImportAssetOptions.ForceUpdate);
        string vnrmTexturePath = Directory.GetFiles(folderPath, "*_vnrm.png")
            .Concat(Directory.GetFiles(folderPath, "*_vnrm.exr"))
            .FirstOrDefault();
        if (vnrmTexturePath != null)
        {
            AssetDatabase.ImportAsset(vnrmTexturePath, ImportAssetOptions.ForceUpdate);
        }

        // Re-import PBR textures with correct settings
        string basecolorPath = GetPBRTexturePath("_basecolor", "_albedo");
        string normalPath = GetPBRTexturePath("_nrml", "_normal");
        string roughnessPath = GetPBRTexturePath("_roug", "_roughness");
        string metallicPath = GetPBRTexturePath("_metl", "_metallic");
        string aoPath = GetPBRTexturePath("_ao", "_ambient", "_occlusion");
        string emissionPath = GetPBRTexturePath("_emis", "_emission");

        if (normalPath != null)
        {
            ImportNormalMap(normalPath);
        }

        // Create the material, or update an existing one in place so it keeps its GUID and
        // every reference to it (prefabs, scenes) stays assigned
        string materialPath = Path.Combine(folderPath, Path.GetFileNameWithoutExtension(modelPath) + "_mat.mat");
        bool usePBR = materialType == MaterialType.PBR
            || (materialType == MaterialType.Auto && basecolorPath != null);
        Shader shader = Shader.Find(usePBR ? "Shader Graphs/openVAT_decoder" : "Shader Graphs/openVAT_decoder_basic");
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        bool isNewMaterial = material == null;
        if (isNewMaterial)
        {
            material = new Material(shader);
        }
        else
        {
            material.shader = shader;
        }

        material.SetTexture("_openVAT_main", AssetDatabase.LoadAssetAtPath<Texture2D>(vatTexturePath));
        // Cleared when absent so a reused material does not keep a stale normal texture
        material.SetTexture("_openVAT_nrml", vnrmTexturePath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(vnrmTexturePath) : null);
        // A separate normal texture means normals are not packed into the VAT
        material.SetFloat("_UsePackedNormals", vnrmTexturePath != null ? 0f : 1f);

        SetJSONParameters(material, jsonPath, vatTexturePath);

        if (basecolorPath != null)
        {
            material.SetTexture("_Basecolor", AssetDatabase.LoadAssetAtPath<Texture2D>(basecolorPath));
        }
        if (normalPath != null)
        {
            material.SetTexture("_Normal", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
        }
        if (roughnessPath != null)
        {
            material.SetTexture("_Roughness", AssetDatabase.LoadAssetAtPath<Texture2D>(roughnessPath));
        }
        if (metallicPath != null)
        {
            material.SetTexture("_Metallic", AssetDatabase.LoadAssetAtPath<Texture2D>(metallicPath));
        }
        if (emissionPath != null)
        {
            material.SetTexture("_Emission", AssetDatabase.LoadAssetAtPath<Texture2D>(emissionPath));
        }
        if (aoPath != null)
        {
            material.SetTexture("_AmbientOcclusion", AssetDatabase.LoadAssetAtPath<Texture2D>(aoPath));
        }

        if (isNewMaterial)
        {
            AssetDatabase.CreateAsset(material, materialPath);
        }
        else
        {
            EditorUtility.SetDirty(material);
        }
        AssetDatabase.SaveAssets();

        string prefabPath = Path.Combine(folderPath, Path.GetFileNameWithoutExtension(modelPath) + ".prefab");
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
        {
            // Update the existing prefab in place so its GUID and object IDs survive, keeping
            // scene instances and their overrides intact
            GameObject prefabRoot = PrefabUtility.LoadPrefabContents(prefabPath);
            prefabRoot.GetComponent<Renderer>().sharedMaterial = material;
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            PrefabUtility.UnloadPrefabContents(prefabRoot);
        }
        else
        {
            // Add FBX to scene and apply material
            GameObject fbxObject = Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));

            // Ensure the object is at the origin
            fbxObject.transform.position = Vector3.zero;
            //fbxObject.transform.rotation = Quaternion.identity;

            fbxObject.GetComponent<Renderer>().sharedMaterial = material;

            // Save as prefab
            PrefabUtility.SaveAsPrefabAsset(fbxObject, prefabPath);
            DestroyImmediate(fbxObject);
        }

        EditorSceneManager.MarkAllScenesDirty();
    }

    private void ImportNormalMap(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null) return;

        importer.textureType = TextureImporterType.NormalMap;
        importer.SaveAndReimport();
    }

    private string GetPBRTexturePath(params string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            string[] paths = Directory.GetFiles(folderPath, "*" + keyword + ".png");
            if (paths.Length > 0)
            {
                return paths[0];
            }
        }
        return null;
    }

    private void SetJSONParameters(Material material, string jsonPath, string vatTexturePath)
    {
        string jsonString = File.ReadAllText(jsonPath);
        jsonString = jsonString.Replace("os-remap", "os_remap");  // Replace hyphen with underscore

        RemapData remapData = JsonUtility.FromJson<RemapData>(jsonString);

        // X is swapped to mirror Blender's right-handed data into Unity's left-handed space
        Vector3 minValues = new Vector3(remapData.os_remap.Max[0], remapData.os_remap.Min[1], remapData.os_remap.Min[2]);
        Vector3 maxValues = new Vector3(remapData.os_remap.Min[0], remapData.os_remap.Max[1], remapData.os_remap.Max[2]);
        if (remapData.space == "unity")
        {
            // The decoder scales positions by 0.01 to suit Blender-space FBX meshes, which Unity
            // imports in centimetres. Unity-space meshes import in metres, so cancel that scale.
            minValues *= 100f;
            maxValues *= 100f;
        }
        float frameCount = remapData.os_remap.Frames;

        material.SetVector("_minValues", minValues);
        material.SetVector("_maxValues", maxValues);
        material.SetFloat("_frames", frameCount);
        material.SetFloat("_resolutionY", AssetDatabase.LoadAssetAtPath<Texture2D>(vatTexturePath).height);

        // Play the animation by default. The decoder advances 30 frames per second of Time at speed 1,
        // so scale speed to the exported framerate. "animations" is keyed by clip name, which
        // JsonUtility cannot deserialize, so read the first clip's framerate directly.
        float framerate = 30f;
        Match framerateMatch = Regex.Match(jsonString, "\"framerate\"\\s*:\\s*([0-9.]+)");
        if (framerateMatch.Success)
        {
            framerate = float.Parse(framerateMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        material.SetFloat("_UseTime", 1f);
        material.SetFloat("_speed", framerate / 30f);
    }

    [System.Serializable]
    private class RemapData
    {
        public OsRemap os_remap;
        public string space; // "blender" or "unity"; absent in older exports, which are Blender space
    }

    [System.Serializable]
    private class OsRemap
    {
        public float[] Min;
        public float[] Max;
        public float Frames;
    }
}
