using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;

// Processes OpenVAT exports automatically: whenever a sidecar, model or VAT texture is imported or
// moved, every <name>-remap_info.json in its folder is processed once the import batch finishes.
// Exports still missing files are skipped until the rest arrive.
public class OpenVATContentPostprocessor : AssetPostprocessor
{
    private static readonly HashSet<string> pending = new HashSet<string>();

    private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        if (!OpenVATSettings.instance.AutoProcess) return;

        foreach (string path in importedAssets.Concat(movedAssets))
        {
            if (!OpenVATContentProcessor.IsRemapInfo(path)
                && !OpenVATContentProcessor.IsModel(path)
                && !OpenVATTexturePostprocessor.IsVATTexture(path))
            {
                continue;
            }
            pending.UnionWith(OpenVATContentProcessor.FindRemapInfos(Path.GetDirectoryName(path)));
        }

        if (pending.Count > 0)
        {
            // Creating assets during an import is unsafe, so wait until it completes
            EditorApplication.delayCall -= ProcessPending;
            EditorApplication.delayCall += ProcessPending;
        }
    }

    private static void ProcessPending()
    {
        string[] paths = pending.ToArray();
        pending.Clear();
        foreach (string path in paths)
        {
            TryProcess(path, false);
        }
    }

    public static void ProcessAll()
    {
        foreach (string guid in AssetDatabase.FindAssets("remap_info t:TextAsset"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (OpenVATContentProcessor.IsRemapInfo(path))
            {
                TryProcess(path, true);
            }
        }
    }

    // One broken export must not stop the others
    private static void TryProcess(string path, bool manual)
    {
        try
        {
            OpenVATContentProcessor.Process(path, OpenVATSettings.instance.DefaultMaterialType, reimportTextures: manual, logMissing: manual);
        }
        catch (System.Exception exception)
        {
            Debug.LogError($"OpenVAT: failed to process {path}");
            Debug.LogException(exception);
        }
    }
}
