using System;
using System.Collections.Generic;
using UnityEngine;

public partial class ProceduralVoxelTerrain
{
    private Material[] BuildSharedMaterials()
    {
        Material[] materials = new Material[Mathf.Max(1, materialDefinitions.Count)];
        for (int i = 0; i < materials.Length; i++)
        {
            VoxelTerrainMaterialDefinition definition = i < materialDefinitions.Count ? materialDefinitions[i] : null;
            materials[i] = ResolveRenderMaterial(definition);
        }

        return materials;
    }

    private Material ResolveRenderMaterial(VoxelTerrainMaterialDefinition definition)
    {
        if (definition != null && ProceduralRenderMaterialUtility.CanUseAssignedMaterial(definition.material))
        {
            return definition.material;
        }

        Color colorTint = definition != null ? definition.colorTint : Color.gray;
        string displayName = definition != null ? definition.ResolveDisplayName() : "Voxel Terrain";
        string cacheKey = $"{displayName}_{ColorUtility.ToHtmlStringRGBA(colorTint)}";
        if (AutoMaterials.TryGetValue(cacheKey, out Material cachedMaterial) && cachedMaterial != null)
        {
            return cachedMaterial;
        }

        Material material = ProceduralRenderMaterialUtility.CreateOpaqueMaterial(
            $"{displayName} Auto Material",
            colorTint,
            0.18f,
            0f);
        if (material == null)
        {
            return null;
        }

        AutoMaterials[cacheKey] = material;
        return material;
    }

    private static List<VoxelTerrainMaterialDefinition> BuildDefaultMaterialDefinitions()
    {
        return new List<VoxelTerrainMaterialDefinition>
        {
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Organic Layer",
                compositionItemName = "Organic Layer",
                colorTint = new Color(0.14f, 0.11f, 0.08f, 1f),
                depthRangeMeters = new Vector2(0f, 0.5f),
                normalizedHeightRange = new Vector2(0f, 1f),
                distributionNoiseScaleMeters = 34f,
                distributionNoiseThreshold = 0f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Topsoil",
                compositionItemName = "Topsoil",
                colorTint = new Color(0.24f, 0.17f, 0.1f, 1f),
                depthRangeMeters = new Vector2(0.1f, 1.4f),
                normalizedHeightRange = new Vector2(0f, 1f),
                distributionNoiseScaleMeters = 42f,
                distributionNoiseThreshold = 0f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Eluviation Layer",
                compositionItemName = "Eluviation Layer",
                colorTint = new Color(0.66f, 0.62f, 0.56f, 1f),
                depthRangeMeters = new Vector2(0.25f, 2.4f),
                normalizedHeightRange = new Vector2(0f, 1f),
                distributionNoiseScaleMeters = 38f,
                distributionNoiseThreshold = 0.46f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Subsoil",
                compositionItemName = "Subsoil",
                colorTint = new Color(0.48f, 0.3f, 0.18f, 1f),
                depthRangeMeters = new Vector2(0.75f, 6f),
                normalizedHeightRange = new Vector2(0f, 1f),
                distributionNoiseScaleMeters = 36f,
                distributionNoiseThreshold = 0.42f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Parent Material",
                compositionItemName = "Parent Material",
                colorTint = new Color(0.54f, 0.47f, 0.38f, 1f),
                depthRangeMeters = new Vector2(2f, 10f),
                normalizedHeightRange = new Vector2(0f, 1f),
                distributionNoiseScaleMeters = 34f,
                distributionNoiseThreshold = 0.48f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Weathered Surface Stone",
                compositionItemName = "Weathered Surface Stone",
                colorTint = new Color(0.47f, 0.44f, 0.4f, 1f),
                depthRangeMeters = new Vector2(5f, 16f),
                normalizedHeightRange = new Vector2(0f, 1f),
                distributionNoiseScaleMeters = 58f,
                distributionNoiseThreshold = 0.58f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Basin Sand",
                compositionItemName = "Basin Sand",
                colorTint = new Color(0.63f, 0.56f, 0.39f, 1f),
                depthRangeMeters = new Vector2(0f, 3.5f),
                normalizedHeightRange = new Vector2(0f, 0.5f),
                distributionNoiseScaleMeters = 24f,
                distributionNoiseThreshold = 0.34f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Basin Gravel",
                compositionItemName = "Basin Gravel",
                colorTint = new Color(0.43f, 0.4f, 0.37f, 1f),
                depthRangeMeters = new Vector2(0f, 4f),
                normalizedHeightRange = new Vector2(0f, 0.5f),
                distributionNoiseScaleMeters = 19f,
                distributionNoiseThreshold = 0.46f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Lake Mud",
                compositionItemName = "Lake Mud",
                colorTint = new Color(0.25f, 0.21f, 0.15f, 1f),
                depthRangeMeters = new Vector2(0f, 3f),
                normalizedHeightRange = new Vector2(0f, 0.42f),
                distributionNoiseScaleMeters = 21f,
                distributionNoiseThreshold = 0.38f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Clay Deposit",
                compositionItemName = "Clay Deposit",
                colorTint = new Color(0.56f, 0.39f, 0.29f, 1f),
                depthRangeMeters = new Vector2(1f, 7f),
                normalizedHeightRange = new Vector2(0f, 0.5f),
                distributionNoiseScaleMeters = 18f,
                distributionNoiseThreshold = 0.68f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Iron Vein",
                compositionItemName = "Iron-Rich Stone",
                colorTint = new Color(0.56f, 0.28f, 0.21f, 1f),
                depthRangeMeters = new Vector2(4f, 48f),
                normalizedHeightRange = new Vector2(0.05f, 0.95f),
                distributionNoiseScaleMeters = 16f,
                distributionNoiseThreshold = 0.76f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Copper Vein",
                compositionItemName = "Copper-Rich Stone",
                colorTint = new Color(0.47f, 0.33f, 0.2f, 1f),
                depthRangeMeters = new Vector2(8f, 48f),
                normalizedHeightRange = new Vector2(0.05f, 0.95f),
                distributionNoiseScaleMeters = 20f,
                distributionNoiseThreshold = 0.8f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Silicate Bedrock",
                compositionItemName = "Stone (Silicate-Rich)",
                colorTint = new Color(0.56f, 0.58f, 0.62f, 1f),
                isFallbackMaterial = true,
                depthRangeMeters = new Vector2(0f, 128f),
                normalizedHeightRange = new Vector2(0f, 1f),
                distributionNoiseScaleMeters = 24f,
                distributionNoiseThreshold = 0f
            }
        };
    }

    private void EnsureDefaultMaterialDefinitionsPresent()
    {
        if (materialDefinitions == null || materialDefinitions.Count == 0 || !UsesDefaultOlympicMaterialStack())
        {
            return;
        }

        List<VoxelTerrainMaterialDefinition> defaultDefinitions = BuildDefaultMaterialDefinitions();
        if (UsesLegacyOlympicMaterialStack())
        {
            materialDefinitions = defaultDefinitions;
            return;
        }

        for (int i = 0; i < defaultDefinitions.Count; i++)
        {
            VoxelTerrainMaterialDefinition defaultDefinition = defaultDefinitions[i];
            int existingIndex = FindMaterialDefinitionIndexByAny(defaultDefinition.displayName, defaultDefinition.compositionItemName);
            if (existingIndex < 0)
            {
                materialDefinitions.Insert(Mathf.Min(i, materialDefinitions.Count), CloneMaterialDefinition(defaultDefinition));
            }
        }

        int bedrockIndex = FindMaterialDefinitionIndexByAny("Silicate Bedrock", "Stone (Silicate-Rich)");
        if (bedrockIndex >= 0 && bedrockIndex < materialDefinitions.Count)
        {
            materialDefinitions[bedrockIndex].isFallbackMaterial = true;
        }
    }

    private GenerationMaterialIndices BuildGenerationMaterialIndices()
    {
        GenerationMaterialIndices indices = new GenerationMaterialIndices
        {
            fallbackIndex = 0,
            organicLayerIndex = FindMaterialDefinitionIndexByAny("Organic Layer"),
            topsoilIndex = FindMaterialDefinitionIndexByAny("Topsoil"),
            eluviationLayerIndex = FindMaterialDefinitionIndexByAny("Eluviation Layer"),
            subsoilIndex = FindMaterialDefinitionIndexByAny("Subsoil"),
            parentMaterialIndex = FindMaterialDefinitionIndexByAny("Parent Material"),
            weatheredStoneIndex = FindMaterialDefinitionIndexByAny("Weathered Surface Stone"),
            basinSandIndex = FindMaterialDefinitionIndexByAny("Basin Sand"),
            basinGravelIndex = FindMaterialDefinitionIndexByAny("Basin Gravel"),
            lakeMudIndex = FindMaterialDefinitionIndexByAny("Lake Mud"),
            clayDepositIndex = FindMaterialDefinitionIndexByAny("Clay Deposit", "Alluvial Clay", "Clay Subsoil"),
            ironVeinIndex = FindMaterialDefinitionIndexByAny("Iron Vein", "Iron Seam", "Iron-Rich Stone"),
            copperVeinIndex = FindMaterialDefinitionIndexByAny("Copper Vein", "Copper-Rich Stone"),
            bedrockIndex = FindMaterialDefinitionIndexByAny("Silicate Bedrock", "Stone (Silicate-Rich)")
        };

        for (int i = 0; i < materialDefinitions.Count; i++)
        {
            if (materialDefinitions[i] != null && materialDefinitions[i].isFallbackMaterial)
            {
                indices.fallbackIndex = (byte)i;
                break;
            }
        }

        return indices;
    }

    private bool UsesLegacyOlympicMaterialStack()
    {
        return FindMaterialDefinitionIndexByAny("Alluvial Clay") >= 0 ||
               FindMaterialDefinitionIndexByAny("Clay Subsoil") >= 0 ||
               FindMaterialDefinitionIndexByAny("Iron Seam") >= 0;
    }

    private bool UsesDefaultOlympicMaterialStack()
    {
        return UsesLegacyOlympicMaterialStack() ||
               FindMaterialDefinitionIndexByAny("Organic Layer") >= 0 ||
               FindMaterialDefinitionIndexByAny("Parent Material") >= 0 ||
               FindMaterialDefinitionIndexByAny("Lake Mud") >= 0 ||
               FindMaterialDefinitionIndexByAny("Silicate Bedrock", "Stone (Silicate-Rich)") >= 0;
    }

    private int FindMaterialDefinitionIndex(string displayName)
    {
        return FindMaterialDefinitionIndexByAny(displayName);
    }

    private int FindMaterialDefinitionIndexByAny(params string[] aliases)
    {
        if (aliases == null || aliases.Length == 0 || materialDefinitions == null)
        {
            return -1;
        }

        for (int i = 0; i < materialDefinitions.Count; i++)
        {
            VoxelTerrainMaterialDefinition definition = materialDefinitions[i];
            if (definition == null)
            {
                continue;
            }

            string displayName = definition.ResolveDisplayName();
            string compositionName = definition.compositionItemName;
            for (int aliasIndex = 0; aliasIndex < aliases.Length; aliasIndex++)
            {
                string alias = aliases[aliasIndex];
                if (string.IsNullOrWhiteSpace(alias))
                {
                    continue;
                }

                if (string.Equals(displayName, alias, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(compositionName, alias, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }
        }

        return -1;
    }

    private static VoxelTerrainMaterialDefinition CloneMaterialDefinition(VoxelTerrainMaterialDefinition source)
    {
        if (source == null)
        {
            return null;
        }

        return new VoxelTerrainMaterialDefinition
        {
            displayName = source.displayName,
            compositionItemName = source.compositionItemName,
            compositionOverride = source.compositionOverride,
            material = source.material,
            colorTint = source.colorTint,
            isFallbackMaterial = source.isFallbackMaterial,
            depthRangeMeters = source.depthRangeMeters,
            normalizedHeightRange = source.normalizedHeightRange,
            distributionNoiseScaleMeters = source.distributionNoiseScaleMeters,
            distributionNoiseThreshold = source.distributionNoiseThreshold
        };
    }
}
