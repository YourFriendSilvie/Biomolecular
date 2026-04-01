using System;
using System.Collections.Generic;
using UnityEngine;

public partial class ProceduralVoxelTerrain
{
    // Linear-space material colors for per-vertex color computation in ChunkMeshBuilder.
    [NonSerialized] internal Color[] vertexMaterialColors;

    // Generation context stored so ChunkMeshBuilder can evaluate profiles at exact vertex positions.
    [NonSerialized] internal GenerationContext lastGenerationContext;
    [NonSerialized] internal TerrainGenerationSettings lastGenerationSettings;

    // CPU-accessible copy of the basin noise offsets, indexed [z * TotalSamplesX + x].
    [NonSerialized] internal float[] basinNoiseOffsets;

    // Authoritative basin sediment depth thresholds: single source for both C# logic and
    // shader uniforms.  Changed here automatically propagates to both; no HLSL edit needed.
    internal const float BasinGravelDepth = 0.9f;
    internal const float BasinSandDepth   = 2.5f;
    internal const float BasinMudDepth    = 4.5f;

    [Header("Basin Sediment Noise")]
    [Tooltip("Spatial scale in metres per noise cycle. Larger = broader sediment blobs.")]
    [SerializeField] internal float basinSedimentNoiseScale = 4.5f;
    [Tooltip("Max depth offset in metres applied to zone boundaries (0 = perfect rings, 0.75 = organic blobs).")]
    [SerializeField] internal float basinSedimentNoiseAmplitude = 0.75f;

    internal Material BuildSharedMaterial()
    {
        // Reuse the existing material instance to avoid destroying/recreating the
        // Unity object on every regeneration.
        if (sharedTerrainMaterial == null)
        {
            Shader terrainShader = Shader.Find("Biomolecular/VoxelTerrainLit");
            if (terrainShader == null)
            {
                Debug.LogWarning("VoxelTerrain: Shader 'Biomolecular/VoxelTerrainLit' not found. Falling back to default URP Lit material.");
                Color fallback = materialDefinitions.Count > 0 ? materialDefinitions[0].colorTint : Color.gray;
                sharedTerrainMaterial = ProceduralRenderMaterialUtility.CreateOpaqueMaterial(
                    "Voxel Terrain Auto Material", fallback, 0.18f, 0f);
                Debug.Log($"VoxelTerrain: Using fallback material with shader '{sharedTerrainMaterial.shader.name}'");
                return sharedTerrainMaterial;
            }

            sharedTerrainMaterial = new Material(terrainShader) { name = "Voxel Terrain" };
            Debug.Log($"VoxelTerrain: Created sharedTerrainMaterial using shader '{terrainShader.name}'");
        }

        // Build linear-space color array used by ComputeVertexColors in ChunkMeshBuilder.
        int count = Mathf.Max(materialDefinitions.Count, 1);
        vertexMaterialColors = new Color[count];
        for (int i = 0; i < count; i++)
        {
            Color c = materialDefinitions[i] != null ? materialDefinitions[i].colorTint : Color.gray;
            vertexMaterialColors[i] = c.linear;
        }
        if (count == 0) vertexMaterialColors[0] = Color.gray;

        // Ensure a placeholder material exists for empty material slots, to avoid index 0/1..N being undefined
        Material placeholderMaterial = ProceduralRenderMaterialUtility.CreateOpaqueMaterial("VoxelTerrain Placeholder", Color.gray, 0.18f, 0f);
        for (int i = 0; i < materialDefinitions.Count; i++)
        {
            if (materialDefinitions[i] != null && materialDefinitions[i].material == null)
            {
                materialDefinitions[i].material = placeholderMaterial;
            }
        }

        // Ensure the shader has a valid _TerrainTextures Texture2DArray. Create a 1x1 per-material slice
        // filled with the material's linear tint so the triplanar shader can sample safely if no texture
        // atlas/array has been explicitly assigned by the user.
        try
        {
            int sliceCount = Mathf.Max(1, count);
            int width = 1, height = 1;
            bool hasValidFullTextures = false;
            var slicePix = new List<Color[]>(sliceCount);

            for (int i = 0; i < sliceCount; i++)
            {
                Color tint = (i < vertexMaterialColors.Length) ? vertexMaterialColors[i] : Color.gray;
                Texture2D tex2D = null;

                if (i < materialDefinitions.Count && materialDefinitions[i] != null && materialDefinitions[i].material != null)
                {
                    Material mat = materialDefinitions[i].material;
                    Texture tex = null;
                    if (mat.HasProperty("_BaseMap")) tex = mat.GetTexture("_BaseMap");
                    if (tex == null) tex = mat.mainTexture;

                    if (tex is Texture2D sourceTex)
                    {
                        if (sourceTex.isReadable)
                        {
                            tex2D = sourceTex;
                        }
                        else
                        {
                            try
                            {
                                var rt = RenderTexture.GetTemporary(sourceTex.width, sourceTex.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.Linear);
                                Graphics.Blit(sourceTex, rt);
                                var prev = RenderTexture.active;
                                RenderTexture.active = rt;
                                Texture2D copy = new Texture2D(sourceTex.width, sourceTex.height, TextureFormat.RGBA32, false, true);
                                copy.ReadPixels(new Rect(0, 0, sourceTex.width, sourceTex.height), 0, 0);
                                copy.Apply(false, false);
                                RenderTexture.active = prev;
                                RenderTexture.ReleaseTemporary(rt);
                                tex2D = copy;
                            }
                            catch
                            {
                                tex2D = null;
                            }
                        }
                    }

                    if (tex == null)
                    {
                        Debug.LogWarning($"VoxelTerrain: Material '{materialDefinitions[i].ResolveDisplayName()}' has no albedo texture; using tint fallback for index {i}.");
                    }
                    else if (tex is not Texture2D)
                    {
                        Debug.LogWarning($"VoxelTerrain: Albedo texture for material '{materialDefinitions[i].ResolveDisplayName()}' is not a Texture2D; using tint fallback for index {i}.");
                        tex2D = null;
                    }
                }
                else
                {
                    if (i >= materialDefinitions.Count || materialDefinitions[i] == null)
                        Debug.LogWarning($"VoxelTerrain: Missing material definition for index {i}; using tint fallback.");
                    else
                        Debug.LogWarning($"VoxelTerrain: Missing material object for index {i}; using tint fallback.");
                }

                if (tex2D != null)
                {
                    if (width == 1 && height == 1)
                    {
                        width = tex2D.width;
                        height = tex2D.height;
                    }

                    if (tex2D.width == width && tex2D.height == height)
                    {
                        slicePix.Add(tex2D.GetPixels());
                        hasValidFullTextures = true;
                        continue;
                    }

                    Debug.LogWarning($"VoxelTerrain: Albedo texture '{tex2D.name}' size ({tex2D.width}x{tex2D.height}) does not match expected ({width}x{height}); using tint fallback for index {i}.");
                    tex2D = null;
                }

                // fallback 1x1 tint slice
                slicePix.Add(new Color[] { tint });
            }

            var textureArray = new Texture2DArray(width, height, sliceCount, TextureFormat.RGBA32, false, false)
            {
                name = hasValidFullTextures ? "VoxelTerrain_AlbedoArray" : "VoxelTerrain_MaterialTintArray"
            };

            sharedTerrainMaterial.SetFloat("_TerrainTextureCount", sliceCount);

            for (int i = 0; i < sliceCount; i++)
            {
                Color[] pixels = slicePix[i];
                if (pixels.Length == 1)
                {
                    // tile tint to full size when this slice has only a 1x1 fallback.
                    Color fillColor = pixels[0];
                    pixels = new Color[width * height];
                    for (int pi = 0; pi < pixels.Length; pi++) pixels[pi] = fillColor;
                }

                textureArray.SetPixels(pixels, i);
            }

            textureArray.Apply(false, false);
            sharedTerrainMaterial.SetTexture("_TerrainTextures", textureArray);

            if (!hasValidFullTextures)
            {
                Debug.LogWarning("VoxelTerrain: No full-size albedo textures could be used; using 1x1 tint Texture2DArray.");
            }
        }
        catch (Exception ex)
        {
            // Preserve existing behavior: log the exception and do not attempt further recovery here.
            Debug.LogWarning($"VoxelTerrain: Failed to create Terrain Texture2DArray: {ex.Message}");
        }

        return sharedTerrainMaterial;
    }
    // One record per carved lake/pond basin. Stored during water generation so the
    // profile texture can be rebuilt in one correct pass after all carving is done.
    private struct RegisteredLakeBasin
    {
        public Vector3 center;
        public float   radius;
        public float   surfaceY;
    }
    [NonSerialized] private readonly List<RegisteredLakeBasin> registeredLakeBasins = new List<RegisteredLakeBasin>();

    // Called by WaterTerrainCarver after each lake/pond is carved so the basin is
    // tracked for the deferred profile-texture rebuild (see RebuildLakeProfileData).
    internal void RegisterLakeBasin(Vector3 center, float radiusMeters, float worldSurfaceY)
    {
        registeredLakeBasins.Add(new RegisteredLakeBasin
        {
            center   = center,
            radius   = radiusMeters,
            surfaceY = worldSurfaceY,
        });
        // Write lake surface Y into columnProfilePrepass so ComputeVertexColors
        // produces correct lake-basin sediment colors (gravel → sand → mud → clay by depth).
        UpdateColumnPrepassLakeSurface(center, radiusMeters, worldSurfaceY);
    }

    // Remove all registered basins (called when water is cleared or terrain regenerated).
    internal void ClearRegisteredLakeBasins()
    {
        registeredLakeBasins.Clear();
        // Reset lake surface Y for all column profiles so removed lakes don't persist.
        if (columnProfilePrepass != null)
        {
            for (int i = 0; i < columnProfilePrepass.Length; i++)
                columnProfilePrepass[i].lakeSurfaceY = 0f;
        }
    }

    // Re-apply lake surface Y to every profile-texture column that belongs to a carved
    // basin.  Called ONCE after ALL lakes and ponds have been carved, so every basin
    // (including wall columns at the carved edge) is handled with the correct radius.
    //
    // This replaces the previous per-lake RefreshProfileTextureForWaterBody calls which
    // used a 0.99× radius that consistently missed the outermost wall columns.
    internal void RebuildLakeProfileData()
    {
        // No-op: lake basin data is now written directly to columnProfilePrepass
        // via UpdateColumnPrepassLakeSurface / RegisterLakeBasin.
        // Profile textures are retired in the VoxelTerrainLit vertex-color shader.
    }

    // Patches the columnProfilePrepass lakeSurfaceY for a single carved basin.
    // Used by runtime paths such as terrain deformation.
    internal void RefreshProfileTextureForWaterBody(
        Vector3 worldCenter, float radiusMeters, float worldSurfaceY)
    {
        UpdateColumnPrepassLakeSurface(worldCenter, radiusMeters, worldSurfaceY);
    }


    // Updates columnProfilePrepass lakeSurfaceY for all cells within the basin.
    // Called from RegisterLakeBasin and RefreshProfileTextureForWaterBody so that
    // ComputeVertexColors shows correct lake-basin sediment colors.
    private void UpdateColumnPrepassLakeSurface(Vector3 worldCenter, float radiusMeters, float worldSurfaceY)
    {
        if (columnProfilePrepass == null || voxelSizeMeters <= 0f) return;

        Vector3 origin     = transform.position;
        float localCX      = worldCenter.x - origin.x;
        float localCZ      = worldCenter.z - origin.z;
        float storeY       = Mathf.Max(worldSurfaceY - origin.y, 0.01f);
        float paintRadius  = radiusMeters + voxelSizeMeters;
        float paintRSq     = paintRadius * paintRadius;

        int minX = Mathf.Max(0,              Mathf.FloorToInt((localCX - paintRadius) / voxelSizeMeters));
        int maxX = Mathf.Min(TotalCellsX - 1, Mathf.CeilToInt((localCX + paintRadius) / voxelSizeMeters));
        int minZ = Mathf.Max(0,              Mathf.FloorToInt((localCZ - paintRadius) / voxelSizeMeters));
        int maxZ = Mathf.Min(TotalCellsZ - 1, Mathf.CeilToInt((localCZ + paintRadius) / voxelSizeMeters));

        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                float dx = (x * voxelSizeMeters) - localCX;
                float dz = (z * voxelSizeMeters) - localCZ;
                if (dx * dx + dz * dz > paintRSq) continue;
                int idx = z * TotalCellsX + x;
                if (storeY > columnProfilePrepass[idx].lakeSurfaceY)
                    columnProfilePrepass[idx].lakeSurfaceY = storeY;
            }
        }
    }


    internal string GetTerrainProfileDebugInfo_Internal(Vector3 worldPoint)
        => BuildTerrainProfileDebugString(worldPoint);

    // Returns a multi-line debug string showing the column profile data at the given world position,
    // reading directly from columnProfilePrepass (the vertex-color pipeline's single source of truth).
    internal string BuildTerrainProfileDebugString(Vector3 worldPoint)
    {
        if (columnProfilePrepass == null || columnProfilePrepass.Length == 0)
            return "Column profile prepass not ready.";
        if (!surfaceHeightPrepassReady)
            return "Surface prepass not ready.";

        Vector3 origin = transform.position;
        float localX = worldPoint.x - origin.x;
        float localZ = worldPoint.z - origin.z;
        float localY = worldPoint.y - origin.y;

        int cellX = Mathf.Clamp(Mathf.FloorToInt(localX / voxelSizeMeters), 0, TotalCellsX - 1);
        int cellZ = Mathf.Clamp(Mathf.FloorToInt(localZ / voxelSizeMeters), 0, TotalCellsZ - 1);
        ColumnMaterialProfile profile = columnProfilePrepass[cellZ * TotalCellsX + cellX];

        float depth = profile.surfaceHeight - localY;
        float lakeWaterDepth = (profile.lakeSurfaceY > 0.001f) ? Mathf.Max(0f, profile.lakeSurfaceY - localY) : 0f;
        float effectiveWaterDepth = Mathf.Max(profile.oceanWaterDepth, lakeWaterDepth);

        // Per-column boundary noise offset — same pre-baked value used by DetermineCellMaterialIndex.
        float bj = profile.materialBoundaryNoise;

        // Dominant layer — matches the decision tree in DetermineCellMaterialIndex,
        // including boundary noise offset (bj) and slope/cliff overrides.
        // Note: ore-vein detection requires 3D noise that is not pre-baked, so veins
        // are not reported here; use the "Actual cellMaterial" line below for those.
        string dominantLayer;
        if (profile.isOceanFloor)
        {
            float owd = profile.oceanWaterDepth;
            if (owd < BasinGravelDepth)      dominantLayer = "Basin Gravel";
            else if (owd < BasinSandDepth)   dominantLayer = "Basin Sand";
            else if (owd < BasinMudDepth)    dominantLayer = "Lake Mud";
            else                             dominantLayer = "Clay Deposit";
        }
        else if (profile.isBeachLand && depth <= profile.beachSandBoundary + bj)   dominantLayer = "Beach Sand";
        else if (profile.isBeachLand && depth <= profile.beachGravelBoundary + bj)  dominantLayer = "Beach Gravel";
        else
        {
            float slopeF = profile.slopeFactor;
            bool isVeryCliff     = slopeF >= 0.75f;
            bool isModerateCliff = slopeF >= 0.5f && slopeF < 0.75f;

            if (isVeryCliff && depth <= profile.weatheredBoundary + bj)
            {
                dominantLayer = "Weathered Stone";
            }
            else if (isModerateCliff &&
                     (depth <= profile.organicThickness + bj || depth <= profile.topsoilBoundary + bj))
            {
                dominantLayer = "Weathered Stone";
            }
            else if (depth <= profile.organicThickness + bj)    dominantLayer = "Organic Layer";
            else if (depth <= profile.topsoilBoundary + bj)     dominantLayer = "Topsoil";
            else if (depth <= profile.eluviationBoundary + bj)  dominantLayer = "Eluviation";
            else if (depth <= profile.subsoilBoundary + bj)     dominantLayer = "Subsoil";
            else if (depth <= profile.parentBoundary + bj)      dominantLayer = "Parent Material";
            else if (depth <= profile.weatheredBoundary + bj)   dominantLayer = "Weathered Stone";
            else                                                 dominantLayer = "Bedrock";
        }

        return
            $"--- Terrain Profile Debug ---\n" +
            $"worldPos: ({worldPoint.x:F1}, {worldPoint.y:F1}, {worldPoint.z:F1})\n" +
            $"cellXZ: ({cellX}, {cellZ})\n" +
            $"surfaceHeight(local): {profile.surfaceHeight:F2}  depth: {depth:F2}  bj: {bj:F3}\n" +
            $"isBeach: {profile.isBeachLand}  isOcean: {profile.isOceanFloor}  slope: {profile.slopeFactor:F3}\n" +
            $"lakeSurfaceY: {profile.lakeSurfaceY:F3}  lakeWaterDepth: {lakeWaterDepth:F3}\n" +
            $"oceanWaterDepth: {profile.oceanWaterDepth:F3}  effectiveWater: {effectiveWaterDepth:F3}\n" +
            $"Profile shows: {dominantLayer}\n" +
            $"Soil boundaries (depth m, +bj): org={profile.organicThickness + bj:F2} top={profile.topsoilBoundary + bj:F2} elu={profile.eluviationBoundary + bj:F2} sub={profile.subsoilBoundary + bj:F2}\n" +
            $"  par={profile.parentBoundary + bj:F2} wth={profile.weatheredBoundary + bj:F2} bSand={profile.beachSandBoundary + bj:F2} bGrav={profile.beachGravelBoundary + bj:F2}";
    }

    private static List<VoxelTerrainMaterialDefinition> BuildDefaultMaterialDefinitions()
    {
        return new List<VoxelTerrainMaterialDefinition>
        {
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Organic Layer",
                compositionItemName = "Organic Layer",
                colorTint = new Color(0.07f, 0.06f, 0.04f, 1f),
                depthRangeMeters = new Vector2(0f, 0.5f),
                normalizedHeightRange = new Vector2(0f, 1f),
                distributionNoiseScaleMeters = 34f,
                distributionNoiseThreshold = 0f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Topsoil",
                compositionItemName = "Topsoil",
                colorTint = new Color(0.20f, 0.14f, 0.08f, 1f),
                depthRangeMeters = new Vector2(0.1f, 1.4f),
                normalizedHeightRange = new Vector2(0f, 1f),
                distributionNoiseScaleMeters = 42f,
                distributionNoiseThreshold = 0f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Eluviation Layer",
                compositionItemName = "Eluviation Layer",
                colorTint = new Color(0.55f, 0.48f, 0.36f, 1f),
                depthRangeMeters = new Vector2(0.25f, 2.4f),
                normalizedHeightRange = new Vector2(0f, 1f),
                distributionNoiseScaleMeters = 38f,
                distributionNoiseThreshold = 0.46f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Subsoil",
                compositionItemName = "Subsoil",
                colorTint = new Color(0.38f, 0.30f, 0.22f, 1f),
                depthRangeMeters = new Vector2(0.75f, 6f),
                normalizedHeightRange = new Vector2(0f, 1f),
                distributionNoiseScaleMeters = 36f,
                distributionNoiseThreshold = 0.42f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Parent Material",
                compositionItemName = "Parent Material",
                colorTint = new Color(0.36f, 0.33f, 0.28f, 1f),
                depthRangeMeters = new Vector2(2f, 10f),
                normalizedHeightRange = new Vector2(0f, 1f),
                distributionNoiseScaleMeters = 34f,
                distributionNoiseThreshold = 0.48f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Weathered Surface Stone",
                compositionItemName = "Weathered Surface Stone",
                colorTint = new Color(0.28f, 0.29f, 0.27f, 1f),
                depthRangeMeters = new Vector2(5f, 16f),
                normalizedHeightRange = new Vector2(0f, 1f),
                distributionNoiseScaleMeters = 58f,
                distributionNoiseThreshold = 0.58f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Basin Sand",
                compositionItemName = "Basin Sand",
                colorTint = new Color(0.58f, 0.52f, 0.38f, 1f),
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
                colorTint = new Color(0.55f, 0.18f, 0.08f, 1f),
                depthRangeMeters = new Vector2(4f, 48f),
                normalizedHeightRange = new Vector2(0.05f, 0.95f),
                distributionNoiseScaleMeters = 16f,
                distributionNoiseThreshold = 0.72f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Copper Vein",
                compositionItemName = "Copper-Rich Stone",
                colorTint = new Color(0.15f, 0.45f, 0.28f, 1f),
                depthRangeMeters = new Vector2(8f, 48f),
                normalizedHeightRange = new Vector2(0.05f, 0.95f),
                distributionNoiseScaleMeters = 20f,
                distributionNoiseThreshold = 0.82f
            },
            new VoxelTerrainMaterialDefinition
            {
                displayName = "Silicate Bedrock",
                compositionItemName = "Stone (Silicate-Rich)",
                colorTint = new Color(0.22f, 0.25f, 0.22f, 1f),
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
        if (materialDefinitions == null || materialDefinitions.Count == 0)
        {
            materialDefinitions = BuildDefaultMaterialDefinitions();
            return;
        }

        if (!UsesDefaultOlympicMaterialStack())
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

        // Ensure fallback index points to a valid material definition (and ideally a real Material object).
        if (indices.fallbackIndex >= materialDefinitions.Count ||
            indices.fallbackIndex < 0 ||
            materialDefinitions[indices.fallbackIndex] == null ||
            materialDefinitions[indices.fallbackIndex].material == null)
        {
            if (indices.bedrockIndex >= 0 && indices.bedrockIndex < materialDefinitions.Count &&
                materialDefinitions[indices.bedrockIndex] != null &&
                materialDefinitions[indices.bedrockIndex].material != null)
            {
                indices.fallbackIndex = (byte)indices.bedrockIndex;
            }
            else
            {
                for (int i = 0; i < materialDefinitions.Count; i++)
                {
                    if (materialDefinitions[i] != null && materialDefinitions[i].material != null)
                    {
                        indices.fallbackIndex = (byte)i;
                        break;
                    }
                }
            }

            // If still invalid, fallback to first available index
            if (indices.fallbackIndex >= materialDefinitions.Count ||
                materialDefinitions[indices.fallbackIndex] == null)
            {
                indices.fallbackIndex = 0;
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
