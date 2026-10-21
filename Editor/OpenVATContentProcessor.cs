using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;

public enum OpenVATMaterialType
{
    Auto,    // PBR when a basecolor texture is present, otherwise Basic
    PBR,     // openVAT_decoder: PBR texture inputs
    Basic    // openVAT_decoder_basic: VAT only
}

// Builds the material and prefab for one OpenVAT export. An export is keyed by its sidecar,
// <name>-remap_info.json, next to <name>.fbx/.glb/.gltf, <name>_vat and optionally <name>_vnrm.
public static class OpenVATContentProcessor
{
    public const string RemapInfoSuffix = "-remap_info.json";

    private static readonly string[] ModelExtensions = { ".fbx", ".glb", ".gltf" };
    private static readonly string[] TextureExtensions = { ".exr", ".png" };

    public static bool IsRemapInfo(string path)
    {
        return path.EndsWith(RemapInfoSuffix, System.StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsModel(string path)
    {
        return ModelExtensions.Contains(Path.GetExtension(path).ToLowerInvariant());
    }

    public static string[] FindRemapInfos(string folder)
    {
        if (!Directory.Exists(folder)) return new string[0];
        return Directory.GetFiles(folder, "*" + RemapInfoSuffix).Select(p => p.Replace('\\', '/')).ToArray();
    }

    // Returns false when the export is incomplete (files still arriving); logs why when asked
    public static bool Process(string remapInfoPath, OpenVATMaterialType materialType, bool reimportTextures = false, bool logMissing = false)
    {
        string folder = Path.GetDirectoryName(remapInfoPath).Replace('\\', '/');
        string baseName = Path.GetFileName(remapInfoPath);
        baseName = baseName.Substring(0, baseName.Length - RemapInfoSuffix.Length);

        string modelPath = FindFile(folder, baseName, ModelExtensions);
        string vatTexturePath = FindFile(folder, baseName + "_vat", TextureExtensions);
        string vnrmTexturePath = FindFile(folder, baseName + "_vnrm", TextureExtensions);
        if (modelPath == null || vatTexturePath == null)
        {
            if (logMissing)
            {
                Debug.LogError($"OpenVAT: {remapInfoPath} needs {baseName}.fbx/.glb/.gltf and {baseName}_vat.exr/.png beside it");
            }
            return false;
        }

        if (reimportTextures)
        {
            // OpenVATTexturePostprocessor applies their settings, for files imported before it existed
            AssetDatabase.ImportAsset(vatTexturePath, ImportAssetOptions.ForceUpdate);
            if (vnrmTexturePath != null)
            {
                AssetDatabase.ImportAsset(vnrmTexturePath, ImportAssetOptions.ForceUpdate);
            }
        }

        string basecolorPath = GetPBRTexturePath(folder, "_basecolor", "_albedo");
        string normalPath = GetPBRTexturePath(folder, "_nrml", "_normal");
        string roughnessPath = GetPBRTexturePath(folder, "_roug", "_roughness");
        string metallicPath = GetPBRTexturePath(folder, "_metl", "_metallic");
        string aoPath = GetPBRTexturePath(folder, "_ao", "_ambient", "_occlusion");
        string emissionPath = GetPBRTexturePath(folder, "_emis", "_emission");

        if (normalPath != null)
        {
            ImportNormalMap(normalPath);
        }

        // Create the material, or update an existing one in place so it keeps its GUID and
        // every reference to it (prefabs, scenes) stays assigned
        string materialPath = folder + "/" + baseName + "_mat.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        bool isNewMaterial = material == null;
        Material template = OpenVATSettings.instance.TemplateMaterial;
        if (template != null)
        {
            // A template supplies the shader and look; it is copied only when the material is
            // created, so later edits to a generated material survive re-exports
            if (isNewMaterial)
            {
                material = new Material(template);
            }
        }
        else
        {
            bool usePBR = materialType == OpenVATMaterialType.PBR
                || (materialType == OpenVATMaterialType.Auto && basecolorPath != null);
            Shader shader = Shader.Find(usePBR ? "Shader Graphs/openVAT_decoder" : "Shader Graphs/openVAT_decoder_basic");
            if (isNewMaterial)
            {
                material = new Material(shader);
            }
            else
            {
                material.shader = shader;
            }
        }

        material.SetTexture("_openVAT_main", AssetDatabase.LoadAssetAtPath<Texture2D>(vatTexturePath));
        // Cleared when absent so a reused material does not keep a stale normal texture
        material.SetTexture("_openVAT_nrml", vnrmTexturePath != null ? AssetDatabase.LoadAssetAtPath<Texture2D>(vnrmTexturePath) : null);
        // A separate normal texture means normals are not packed into the VAT
        material.SetFloat("_UsePackedNormals", vnrmTexturePath != null ? 0f : 1f);

        SetJSONParameters(material, remapInfoPath, vatTexturePath);
        if (material.GetFloat("_RelativeNormals") > 0f)
        {
            KeepImportedNormals(modelPath);
        }

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

        string prefabPath = folder + "/" + baseName + ".prefab";
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
            GameObject fbxObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(modelPath));

            // Ensure the object is at the origin
            fbxObject.transform.position = Vector3.zero;

            fbxObject.GetComponent<Renderer>().sharedMaterial = material;

            // Save as prefab
            PrefabUtility.SaveAsPrefabAsset(fbxObject, prefabPath);
            Object.DestroyImmediate(fbxObject);
        }

        Debug.Log($"OpenVAT: processed {baseName} into {materialPath} and {prefabPath}");
        return true;
    }

    private static string FindFile(string folder, string name, string[] extensions)
    {
        return extensions.Select(ext => folder + "/" + name + ext).FirstOrDefault(File.Exists);
    }

    // Relative VAT normals are offsets from the mesh's own normals, so Unity must keep the
    // exported normals rather than recalculate them
    private static void KeepImportedNormals(string modelPath)
    {
        ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null || importer.importNormals == ModelImporterNormals.Import) return;

        importer.importNormals = ModelImporterNormals.Import;
        importer.SaveAndReimport();
    }

    private static void ImportNormalMap(string path)
    {
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null || importer.textureType == TextureImporterType.NormalMap) return;

        importer.textureType = TextureImporterType.NormalMap;
        importer.SaveAndReimport();
    }

    private static string GetPBRTexturePath(string folder, params string[] keywords)
    {
        foreach (var keyword in keywords)
        {
            string[] paths = Directory.GetFiles(folder, "*" + keyword + ".png");
            if (paths.Length > 0)
            {
                return paths[0].Replace('\\', '/');
            }
        }
        return null;
    }

    private static void SetJSONParameters(Material material, string jsonPath, string vatTexturePath)
    {
        string jsonString = File.ReadAllText(jsonPath);
        jsonString = jsonString.Replace("os-remap", "os_remap");  // Replace hyphen with underscore

        RemapData remapData = JsonUtility.FromJson<RemapData>(jsonString);

        // Unity mirrors X when it imports the mesh. Unity-space exports already store positions and
        // normals in that mirrored frame; for Blender-space data, negating the X range mirrors the
        // decoded X to match
        bool unitySpace = remapData.space == "unity";
        Vector3 mirror = new Vector3(unitySpace ? 1f : -1f, 1f, 1f);
        Vector3 minValues = Vector3.Scale(ToVector3(remapData.os_remap.Min), mirror);
        Vector3 maxValues = Vector3.Scale(ToVector3(remapData.os_remap.Max), mirror);
        if (unitySpace)
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
        // Normals decode within the exported range, mirrored like positions. Relative normals are
        // offsets from the mesh normal; older exports have no "normals" entry and are absolute -1..1
        bool hasNormalRange = remapData.normals != null && remapData.normals.Min != null && remapData.normals.Min.Length == 3;
        bool relativeNormals = hasNormalRange && remapData.normals.Basis == "relative";
        Vector3 normalMin = hasNormalRange ? ToVector3(remapData.normals.Min) : -Vector3.one;
        Vector3 normalMax = hasNormalRange ? ToVector3(remapData.normals.Max) : Vector3.one;
        material.SetFloat("_RelativeNormals", relativeNormals ? 1f : 0f);
        material.SetVector("_nrmMinValues", Vector3.Scale(normalMin, mirror));
        material.SetVector("_nrmMaxValues", Vector3.Scale(normalMax, mirror));

        material.SetFloat("_UseTime", 1f);
        material.SetFloat("_speed", framerate / 30f);
    }

    private static Vector3 ToVector3(float[] values)
    {
        return new Vector3(values[0], values[1], values[2]);
    }

    [System.Serializable]
    private class RemapData
    {
        public OsRemap os_remap;
        public string space; // "blender" or "unity"; absent in older exports, which are Blender space
        public NormalRemap normals; // absent in older exports and when normals are not encoded
    }

    [System.Serializable]
    private class NormalRemap
    {
        public string Basis; // "absolute" or "relative"
        public float[] Min;
        public float[] Max;
    }

    [System.Serializable]
    private class OsRemap
    {
        public float[] Min;
        public float[] Max;
        public float Frames;
    }
}
