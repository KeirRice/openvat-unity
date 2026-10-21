using UnityEngine;
using UnityEditor;

// Manual fallback for OpenVATContentPostprocessor: process every export in a folder, optionally
// with a material type other than the project default
public class OpenVATEditor : EditorWindow
{
    private string folderPath = "Assets/OpenVATContent";
    private OpenVATMaterialType materialType;

    [MenuItem("Tools/OpenVAT Editor")]
    public static void ShowWindow()
    {
        GetWindow<OpenVATEditor>("OpenVAT Editor");
    }

    private void OnEnable()
    {
        materialType = OpenVATSettings.instance.DefaultMaterialType;
    }

    private void OnGUI()
    {
        GUILayout.Label("OpenVAT Content Importer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Exports are processed automatically on import (Project Settings > OpenVAT). Use this to reprocess a folder by hand.", MessageType.Info);

        folderPath = EditorGUILayout.TextField("Folder Path", folderPath);
        using (new EditorGUI.DisabledScope(OpenVATSettings.instance.TemplateMaterial != null))
        {
            materialType = (OpenVATMaterialType)EditorGUILayout.EnumPopup("Material Type", materialType);
        }

        if (GUILayout.Button("Process OpenVAT Content"))
        {
            ProcessOpenVATContent();
        }
    }

    private void ProcessOpenVATContent()
    {
        string[] remapInfos = OpenVATContentProcessor.FindRemapInfos(folderPath);
        if (remapInfos.Length == 0)
        {
            Debug.LogError($"OpenVAT: no *{OpenVATContentProcessor.RemapInfoSuffix} found in {folderPath}");
            return;
        }

        foreach (string remapInfo in remapInfos)
        {
            OpenVATContentProcessor.Process(remapInfo, materialType, reimportTextures: true, logMissing: true);
        }
    }
}
