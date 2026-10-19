using System.IO;
using UnityEditor;
using UnityEngine;

// Forces exact, data-preserving import settings on OpenVAT textures (*_vat / *_vnrm, PNG or EXR)
// every time Unity imports them, including when exported files are dropped over existing ones.
// VAT texels are addressed per vertex and per frame, so any filtering, gamma conversion,
// compression, mipmapping or resizing corrupts the decoded positions and normals.
public class OpenVATTexturePostprocessor : AssetPostprocessor
{
    // Platform overrides would replace the uncompressed Default settings below, so they are cleared.
    private static readonly string[] PlatformOverrides =
    {
        "Standalone", "Server", "iPhone", "tvOS", "VisionOS", "Android", "WebGL",
        "Windows Store Apps", "PS4", "PS5", "XboxOne", "GameCoreXboxOne", "GameCoreScarlett", "Switch"
    };

    public static bool IsVATTexture(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension != ".png" && extension != ".exr")
            return false;

        string name = Path.GetFileNameWithoutExtension(path);
        return name.EndsWith("_vat") || name.EndsWith("_vnrm");
    }

    private void OnPreprocessTexture()
    {
        if (!IsVATTexture(assetPath))
            return;

        TextureImporter importer = (TextureImporter)assetImporter;
        bool isHDR = Path.GetExtension(assetPath).ToLowerInvariant() == ".exr";

        importer.textureType = TextureImporterType.Default;
        importer.textureShape = TextureImporterShape.Texture2D;
        importer.sRGBTexture = false;
        importer.mipmapEnabled = false;
        importer.filterMode = FilterMode.Point;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 16384;

        TextureImporterPlatformSettings settings = importer.GetDefaultPlatformTextureSettings();
        settings.format = isHDR ? TextureImporterFormat.RGBAFloat : TextureImporterFormat.RGBA32;
        settings.textureCompression = TextureImporterCompression.Uncompressed;
        settings.maxTextureSize = 16384;
        importer.SetPlatformTextureSettings(settings);

        foreach (string platform in PlatformOverrides)
            importer.ClearPlatformTextureSettings(platform);
    }
}
