using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

// The decoder shaders read the VAT UV from channel 2 (TEXCOORD2). Blender appends VAT_UV after the
// mesh's own UV maps, so it imports into channel 0 or 1 and channel 2 is empty, which makes every
// vertex sample the texture's corner. For models beside an OpenVAT sidecar, copy the last
// populated channel (VAT_UV) into channel 2.
public class OpenVATModelPostprocessor : AssetPostprocessor
{
    private const int VATChannel = 2;

    // Bumped so models imported before this existed are reimported
    public override uint GetVersion()
    {
        return 1;
    }

    private void OnPostprocessModel(GameObject root)
    {
        string folder = Path.GetDirectoryName(assetPath);
        string remapInfo = Path.Combine(folder, Path.GetFileNameWithoutExtension(assetPath) + OpenVATContentProcessor.RemapInfoSuffix);
        if (!File.Exists(remapInfo)) return;

        var uvs = new List<Vector4>();
        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = filter.sharedMesh;
            if (mesh == null) continue;

            int vatChannel = -1;
            for (int channel = 0; channel < 8; channel++)
            {
                mesh.GetUVs(channel, uvs);
                if (uvs.Count > 0) vatChannel = channel;
            }
            if (vatChannel < 0 || vatChannel == VATChannel) continue;
            if (vatChannel > VATChannel)
            {
                Debug.LogWarning($"OpenVAT: {assetPath} has more than {VATChannel + 1} UV maps; using the last as VAT_UV and overwriting channel {VATChannel}");
            }

            mesh.GetUVs(vatChannel, uvs);
            mesh.SetUVs(VATChannel, uvs);
        }
    }
}
