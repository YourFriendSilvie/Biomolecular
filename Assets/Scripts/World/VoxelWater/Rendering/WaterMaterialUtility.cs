using UnityEngine;

/// <summary>
/// Static helpers for resolving and configuring water materials.
/// Owns the process-lifetime material cache so multiple systems can share it.
/// </summary>
internal static class WaterMaterialUtility
{
    internal static Material CachedFreshWaterMaterial;
    internal static Material CachedSaltWaterMaterial;

    public static Material ResolveWaterMaterial(Material assignedMaterial, ref Material cachedMaterial, Color color, string materialName)
    {
        if (ProceduralRenderMaterialUtility.CanUseAssignedMaterial(assignedMaterial))
        {
            if (cachedMaterial == null || cachedMaterial.shader != assignedMaterial.shader)
            {
                cachedMaterial = new Material(assignedMaterial)
                {
                    name = materialName,
                    hideFlags = Application.isPlaying ? HideFlags.HideAndDontSave : HideFlags.None
                };
            }

            ApplyWaterMaterialAppearance(cachedMaterial, color);
            return cachedMaterial;
        }

        if (cachedMaterial == null)
        {
            cachedMaterial = ProceduralRenderMaterialUtility.CreateTransparentMaterial(
                materialName,
                color,
                0.9f,
                0f);
        }
        else
        {
            ApplyWaterMaterialAppearance(cachedMaterial, color);
        }

        return cachedMaterial;
    }

    public static void ApplyWaterMaterialAppearance(Material material, Color color)
    {
        if (material == null)
        {
            return;
        }

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.9f);
        }

        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", 0f);
        }

        if (material.HasProperty("_Surface"))
        {
            material.SetFloat("_Surface", 1f);
        }

        if (material.HasProperty("_Blend"))
        {
            material.SetFloat("_Blend", 0f);
        }

        if (material.HasProperty("_AlphaClip"))
        {
            material.SetFloat("_AlphaClip", 0f);
        }

        if (material.HasProperty("_SrcBlend"))
        {
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 3f);
        }

        if (material.HasProperty("_Cull"))
        {
            material.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
        }

        if (material.HasProperty("_CullMode"))
        {
            material.SetFloat("_CullMode", (float)UnityEngine.Rendering.CullMode.Off);
        }

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHAMODULATE_ON");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }
}
