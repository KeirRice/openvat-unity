using UnityEngine;
using UnityEditor;

// Project-wide OpenVAT import settings, shown under Project Settings > OpenVAT
[FilePath("ProjectSettings/OpenVATSettings.asset", FilePathAttribute.Location.ProjectFolder)]
public class OpenVATSettings : ScriptableSingleton<OpenVATSettings>
{
    [SerializeField] private bool autoProcess = true;
    [SerializeField] private OpenVATMaterialType defaultMaterialType = OpenVATMaterialType.Auto;
    // Stored by GUID so the settings file stays valid when the material moves or is missing
    [SerializeField] private string templateMaterialGuid = "";

    public bool AutoProcess => autoProcess;
    public OpenVATMaterialType DefaultMaterialType => defaultMaterialType;

    public Material TemplateMaterial
    {
        get
        {
            if (string.IsNullOrEmpty(templateMaterialGuid)) return null;
            return AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(templateMaterialGuid));
        }
    }

    [SettingsProvider]
    private static SettingsProvider CreateProvider()
    {
        return new SettingsProvider("Project/OpenVAT", SettingsScope.Project)
        {
            guiHandler = _ => instance.DrawGUI(),
            keywords = new[] { "OpenVAT", "VAT", "vertex animation", "template material" }
        };
    }

    private void DrawGUI()
    {
        EditorGUIUtility.labelWidth = 200f;
        EditorGUI.BeginChangeCheck();

        autoProcess = EditorGUILayout.Toggle(
            new GUIContent("Auto Process", "Build the material and prefab whenever an OpenVAT export (<name>-remap_info.json beside its model and _vat texture) is imported or updated"),
            autoProcess);

        Material template = (Material)EditorGUILayout.ObjectField(
            new GUIContent("Template Material", "Copied for each new OpenVAT material, then given the VAT textures and parameters. Edits to a generated material survive re-exports. Leave empty to use the built-in decoder shaders"),
            TemplateMaterial, typeof(Material), false);

        using (new EditorGUI.DisabledScope(template != null))
        {
            defaultMaterialType = (OpenVATMaterialType)EditorGUILayout.EnumPopup(
                new GUIContent("Default Material Type", "Built-in decoder shader when no template is set. Auto picks PBR when a basecolor texture is present, otherwise Basic"),
                defaultMaterialType);
        }

        if (EditorGUI.EndChangeCheck())
        {
            templateMaterialGuid = template != null ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(template)) : "";
            Save(true);
        }

        if (template != null && !template.HasProperty("_openVAT_main"))
        {
            EditorGUILayout.HelpBox("The template's shader has no _openVAT_main property, so it will not play VAT animation. Base it on an OpenVAT decoder shader.", MessageType.Warning);
        }

        EditorGUILayout.Space();
        if (GUILayout.Button("Reprocess All OpenVAT Content", GUILayout.Width(240f)))
        {
            OpenVATContentPostprocessor.ProcessAll();
        }
    }
}
