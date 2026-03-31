using UnityEngine;
using UnityEngine.Rendering;

public static class ProceduralRenderMaterialUtility
{
    private static readonly string[] ScriptableRenderPipelineShaderNames =
    {
        "Universal Render Pipeline/Lit",
        "Universal Render Pipeline/Simple Lit",
        "Universal Render Pipeline/Unlit",
        "Sprites/Default",
        "Unlit/Color"
    };

    private static readonly string[] BuiltInShaderNames =
    {
        "Standard",
        "Unlit/Color",
        "Sprites/Default"
    };

    public static bool CanUseAssignedMaterial(Material material)
    {
        return material != null && material.shader != null && material.shader.isSupported;
    }

    public static Material CreateOpaqueMaterial(string materialName, Color color, float smoothness, float metallic)
    {
        Material material = CreateBaseMaterial(materialName, color);
        if (material == null)
        {
            return null;
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", smoothness);
        }

        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", metallic);
        }

        return material;
    }

    public static Material CreateTransparentMaterial(string materialName, Color color, float smoothness, float metallic)
    {
        Material material = CreateBaseMaterial(materialName, color);
        if (material == null)
        {
            return null;
        }

        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", smoothness);
        }

        if (material.HasProperty("_Metallic"))
        {
            material.SetFloat("_Metallic", metallic);
        }

        ConfigureTransparency(material);
        return material;
    }

    private static Material CreateBaseMaterial(string materialName, Color color)
    {
        Shader shader = FindCompatibleShader();
        if (shader == null)
        {
            Debug.LogWarning($"Could not find a compatible shader for generated material '{materialName}'.");
            return null;
        }

        Material material = new Material(shader)
        {
            name = materialName,
            hideFlags = Application.isPlaying ? HideFlags.HideAndDontSave : HideFlags.None
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", color);
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", color);
        }

        return material;
    }

    private static void ConfigureTransparency(Material material)
    {
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
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        }

        if (material.HasProperty("_DstBlend"))
        {
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        }

        if (material.HasProperty("_ZWrite"))
        {
            material.SetFloat("_ZWrite", 0f);
        }

        if (material.HasProperty("_Mode"))
        {
            material.SetFloat("_Mode", 3f);
        }

        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.DisableKeyword("_ALPHATEST_ON");
        material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        material.DisableKeyword("_ALPHAMODULATE_ON");
        material.renderQueue = (int)RenderQueue.Transparent;
    }

    private static Shader FindCompatibleShader()
    {
        string[] shaderNames = GraphicsSettings.currentRenderPipeline != null || QualitySettings.renderPipeline != null
            ? ScriptableRenderPipelineShaderNames
            : BuiltInShaderNames;

        Shader firstFoundShader = null;
        for (int i = 0; i < shaderNames.Length; i++)
        {
            Shader shader = Shader.Find(shaderNames[i]);
            if (shader == null)
            {
                continue;
            }

            firstFoundShader ??= shader;
            if (shader.isSupported)
            {
                return shader;
            }
        }

        return firstFoundShader;
    }
}
