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

    // Profile textures populated after generation; sampled per-pixel in VoxelTerrain.shader.
    [NonSerialized] private Texture2D profileTex0;
    [NonSerialized] private Texture2D profileTex1;
    [NonSerialized] private Texture2D profileTex2;
    // One texel per XZ sample: the colorTint of the topmost solid cell at that column.
    [NonSerialized] private Texture2D cellMaterialDebugTex;
    // Fourth profile texture: tex3.r = pre-baked basin noise offset per XZ column (metres).
    // Noise formula lives only in C#; the shader reads the pre-baked value, no duplication.
    [NonSerialized] private Texture2D profileTex3;
    // CPU-accessible copy of the noise offsets, indexed [z * TotalSamplesX + x].
    [NonSerialized] internal float[] basinNoiseOffsets;

    // Shader property IDs (cached to avoid string lookups every frame).
    private static readonly int PropProfileTex0        = Shader.PropertyToID("_ProfileTex0");
    private static readonly int PropProfileTex1        = Shader.PropertyToID("_ProfileTex1");
    private static readonly int PropProfileTex2        = Shader.PropertyToID("_ProfileTex2");
    private static readonly int PropTerrainOriginX    = Shader.PropertyToID("_TerrainOriginX");
    private static readonly int PropTerrainOriginZ    = Shader.PropertyToID("_TerrainOriginZ");
    private static readonly int PropTerrainWorldSizeX  = Shader.PropertyToID("_TerrainWorldSizeX");
    private static readonly int PropTerrainWorldSizeZ  = Shader.PropertyToID("_TerrainWorldSizeZ");
    private static readonly int PropBlendHalfWidth     = Shader.PropertyToID("_BlendHalfWidth");
    private static readonly int PropDebugFlat          = Shader.PropertyToID("_DebugFlat");
    private static readonly int PropMatOrganic         = Shader.PropertyToID("_MatColor_Organic");
    private static readonly int PropMatTopsoil         = Shader.PropertyToID("_MatColor_Topsoil");
    private static readonly int PropMatEluviation      = Shader.PropertyToID("_MatColor_Eluviation");
    private static readonly int PropMatSubsoil         = Shader.PropertyToID("_MatColor_Subsoil");
    private static readonly int PropMatParent          = Shader.PropertyToID("_MatColor_Parent");
    private static readonly int PropMatWeathered       = Shader.PropertyToID("_MatColor_Weathered");
    private static readonly int PropMatBedrock         = Shader.PropertyToID("_MatColor_Bedrock");
    private static readonly int PropMatBasinSand       = Shader.PropertyToID("_MatColor_BasinSand");
    private static readonly int PropMatBasinGravel     = Shader.PropertyToID("_MatColor_BasinGravel");
    private static readonly int PropMatLakeMud         = Shader.PropertyToID("_MatColor_LakeMud");
    private static readonly int PropMatClay            = Shader.PropertyToID("_MatColor_Clay");
    private static readonly int PropCellMaterialDebugTex = Shader.PropertyToID("_CellMaterialDebugTex");
    private static readonly int PropDebugCellMaterial    = Shader.PropertyToID("_DebugCellMaterial");
    private static readonly int PropProfileTex3         = Shader.PropertyToID("_ProfileTex3");
    private static readonly int PropBasinGravelDepth     = Shader.PropertyToID("_BasinGravelDepth");
    private static readonly int PropBasinSandDepth       = Shader.PropertyToID("_BasinSandDepth");
    private static readonly int PropBasinMudDepth        = Shader.PropertyToID("_BasinMudDepth");

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

    // ---- Basin sediment noise helpers (single source of truth) ----
    // Smooth value noise on a 2D grid, range [0, 1].  Results are baked into profileTex3.r
    // during UploadProfileTexturesToMaterial so that the shader never needs to implement
    // this formula.  Changing the formula here automatically propagates everywhere.
    private static float Hash2DBasin(float x, float z)
    {
        float v = Mathf.Sin(x * 127.1f + z * 311.7f) * 43758.5453f;
        return v - Mathf.Floor(v);
    }

    private static float SmoothBasinNoise(float x, float z)
    {
        float ix = Mathf.Floor(x), fx = x - ix;
        float iz = Mathf.Floor(z), fz = z - iz;
        float ux = fx * fx * (3f - 2f * fx);
        float uz = fz * fz * (3f - 2f * fz);
        float n00 = Hash2DBasin(ix,      iz     );
        float n10 = Hash2DBasin(ix + 1f, iz     );
        float n01 = Hash2DBasin(ix,      iz + 1f);
        float n11 = Hash2DBasin(ix + 1f, iz + 1f);
        return Mathf.Lerp(Mathf.Lerp(n00, n10, ux), Mathf.Lerp(n01, n11, ux), uz);
    }

    // Build and upload three RGBA32F profile textures to the terrain material.
    // Called once after terrain generation completes. The shader samples these
    // textures per-pixel for sub-pixel smooth material transitions.
    // RETIRED: profile textures are no longer used. Terrain color is now baked into
    // vertex.color by ChunkMeshBuilder.ComputeVertexColors. This method is a no-op
    // and its callers should be removed.
    [System.Obsolete("Profile texture pipeline is retired. Terrain color is baked into vertex colors by ChunkMeshBuilder.")]
    internal void UploadProfileTexturesToMaterial(
        float[] surfaceHeightPrepass,
        ColumnMaterialProfile[] columnProfilePrepass,
        GenerationContext context,
        TerrainGenerationSettings settings,
        int totalSamplesX,
        int totalSamplesZ)
    {
        // Profile textures are no longer used — terrain color is baked into vertex.color
        // by ChunkMeshBuilder.ComputeVertexColors using the columnProfilePrepass directly.
        // Keep the method signature so call-sites do not need to change.
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

    // Returns a multi-line debug string showing the raw profile texture values and
    // computed shader state at the given world position.  Call this from the debug
    // overlay tool to verify that shader behavior matches actual material assignment.
    internal string BuildTerrainProfileDebugString(Vector3 worldPoint)
    {
        if (profileTex0 == null || profileTex1 == null || profileTex2 == null)
            return "Profile textures not yet uploaded.";
        if (!surfaceHeightPrepassReady)
            return "Surface prepass not ready.";

        Vector3 origin    = transform.position;
        float worldSizeX  = (profileTex0.width  - 1) * lastGenerationSettings.voxelSizeMeters;
        float worldSizeZ  = (profileTex0.height - 1) * lastGenerationSettings.voxelSizeMeters;
        float u = (worldPoint.x - origin.x) / worldSizeX;
        float v = (worldPoint.z - origin.z) / worldSizeZ;

        // Bilinear sample each texture (match GPU behavior).
        Color p0 = SampleBilinear(profileTex0, u, v);
        Color p1 = SampleBilinear(profileTex1, u, v);
        Color p2 = SampleBilinear(profileTex2, u, v);

        float surfaceHeight     = p0.r;
        float rawBeachFactor    = p0.g;
        float lakeSurfaceY      = p0.b;
        float oceanWaterDepth   = p0.a;

        float localY = worldPoint.y - origin.y;
        float lakeWaterDepth = (lakeSurfaceY > 0.001f) ? Mathf.Max(0f, lakeSurfaceY - localY) : 0f;
        float effectiveWaterDepth = Mathf.Max(oceanWaterDepth, lakeWaterDepth);

        float depth = surfaceHeight - localY;

        float beachBlend = Mathf.Clamp01(SmoothStep(0.12f, 0.18f, rawBeachFactor));
        beachBlend      *= Mathf.Clamp01(1f - effectiveWaterDepth / 0.08f);
        float basinBlend = Mathf.Clamp01(SmoothStep(0.0f, 0.08f, effectiveWaterDepth));

        // Dominant shader layer at this depth.
        string shaderMaterial = "Bedrock";
        if (basinBlend > 0.5f)
        {
            // Basin stack (bottom-up): Clay base → Mud → Sand → Gravel (matches shader)
            float bd = effectiveWaterDepth;
            if (bd < 0.9f)       shaderMaterial = "Basin Gravel";
            else if (bd < 2.5f)  shaderMaterial = "Basin Sand";
            else if (bd < 4.5f)  shaderMaterial = "Lake Mud";
            else                 shaderMaterial = "Clay Deposit";
        }
        else if (beachBlend > 0.5f)
        {
            shaderMaterial = depth < p2.b ? "Basin Sand (beach)" : "Basin Gravel (beach)";
        }
        else
        {
            if (depth < p1.r)                  shaderMaterial = "Organic Layer";
            else if (depth < p1.g)             shaderMaterial = "Topsoil";
            else if (depth < p1.b)             shaderMaterial = "Eluviation";
            else if (depth < p1.a)             shaderMaterial = "Subsoil";
            else if (depth < p2.r)             shaderMaterial = "Parent Material";
            else if (depth < p2.g)             shaderMaterial = "Weathered Stone";
            else                               shaderMaterial = "Bedrock";
        }

        return
            $"--- Terrain Profile Debug ---\n" +
            $"worldPos: ({worldPoint.x:F1}, {worldPoint.y:F1}, {worldPoint.z:F1})\n" +
            $"profile UV: ({u:F3}, {v:F3})\n" +
            $"surfaceHeight(local): {surfaceHeight:F2}  depth: {depth:F2}\n" +
            $"rawBeachFactor: {rawBeachFactor:F3}  lakeSurfaceY: {lakeSurfaceY:F3}  lakeWaterDepth: {lakeWaterDepth:F3}\n" +
            $"oceanWaterDepth: {oceanWaterDepth:F3}  effectiveWater: {effectiveWaterDepth:F3}\n" +
            $"beachBlend: {beachBlend:F3}  basinBlend: {basinBlend:F3}\n" +
            $"Shader shows: {shaderMaterial}\n" +
            $"Soil boundaries (depth m): org={p1.r:F2} top={p1.g:F2} elu={p1.b:F2} sub={p1.a:F2}\n" +
            $"  par={p2.r:F2} wth={p2.g:F2} bSand={p2.b:F2} bGrav={p2.a:F2}";
    }

    private static Color SampleBilinear(Texture2D tex, float u, float v)
    {
        float px = u * (tex.width  - 1);
        float pz = v * (tex.height - 1);
        int x0 = Mathf.Clamp(Mathf.FloorToInt(px), 0, tex.width  - 1);
        int x1 = Mathf.Clamp(x0 + 1,               0, tex.width  - 1);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(pz), 0, tex.height - 1);
        int z1 = Mathf.Clamp(z0 + 1,               0, tex.height - 1);
        float fx = px - x0, fz = pz - z0;
        Color c00 = tex.GetPixel(x0, z0);
        Color c10 = tex.GetPixel(x1, z0);
        Color c01 = tex.GetPixel(x0, z1);
        Color c11 = tex.GetPixel(x1, z1);
        return Color.Lerp(Color.Lerp(c00, c10, fx), Color.Lerp(c01, c11, fx), fz);
    }

    private static float SmoothStep(float lo, float hi, float x)
    {
        float t = Mathf.Clamp01((x - lo) / (hi - lo));
        return t * t * (3f - 2f * t);
    }

    private Material BuildSharedMaterials_Legacy()
    {
        return BuildSharedMaterial();
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

    // Toggle the cell material debug overlay. When enabled the shader shows the raw
    // colorTint of the CPU-assigned cellMaterialIndices value at each XZ column instead
    // of the normal profile-texture-computed colour. Call again with false to restore normal shading.
    // RETIRED: debug overlay relied on the old profile-texture shader.
    [System.Obsolete("Cell material debug overlay is retired with the profile-texture shader pipeline.")]
    public void SetCellMaterialDebugMode(bool enabled)
    {
        // Debug cell-material overlay relied on the old profile-texture shader.
        // It is a no-op with the vertex-color VoxelTerrainLit shader.
    }

    // Pending CPU-side storage for debug texture pixels when generated off the main thread.
    [NonSerialized] private Color[] cellMaterialDebugPendingPixels;
    [NonSerialized] private bool cellMaterialDebugPending = false;

    private void BuildAndUploadCellMaterialDebugTexture()
    {
        if (!HasReadySurfaceHeightPrepass || cellMaterialIndices == null)
            return;

        int w   = TotalSamplesX;
        int h   = TotalSamplesZ;
        float vs = voxelSizeMeters;

        Color[] pixels = new Color[w * h];
        int maxCellX = TotalCellsX - 1;
        int maxCellY = TotalCellsY - 1;
        int maxCellZ = TotalCellsZ - 1;

        for (int z = 0; z < h; z++)
        {
            for (int x = 0; x < w; x++)
            {
                int   sampleIdx = z * w + x;
                float surfaceH  = surfaceHeightPrepass[sampleIdx];
                int   cellX     = Mathf.Clamp(x, 0, maxCellX);
                int   cellZ     = Mathf.Clamp(z, 0, maxCellZ);
                // Target the solid cell just below the iso-surface.
                int   cellY     = Mathf.Clamp(Mathf.FloorToInt((surfaceH - vs * 0.1f) / vs), 0, maxCellY);
                int   cellIdx   = VoxelDataStore.GetCellIndex(cellX, cellY, cellZ, TotalCellsX, TotalCellsY);
                byte  matIdx    = cellMaterialIndices[cellIdx];
                Color c = (matIdx < materialDefinitions.Count && materialDefinitions[matIdx] != null)
                    ? materialDefinitions[matIdx].colorTint
                    : Color.magenta;
                pixels[sampleIdx] = c;
            }
        }

        // If we're on the main thread, create the Texture2D and upload immediately.
        // If we're running on a background thread (the mesh builder), defer creation of
        // the Texture2D until the main thread. Physics and many Graphics APIs are only
        // valid on the main thread; avoid calling Texture2D constructor off-thread.
        if (System.Threading.Thread.CurrentThread.ManagedThreadId == ProceduralVoxelTerrain.MainThreadId)
        {
            if (cellMaterialDebugTex == null || cellMaterialDebugTex.width != w || cellMaterialDebugTex.height != h)
            {
                cellMaterialDebugTex = new Texture2D(w, h, TextureFormat.RGBA32, false, true)
                {
                    name        = "CellMaterialDebug",
                    filterMode  = FilterMode.Point,
                    wrapMode    = TextureWrapMode.Clamp
                };
            }

            cellMaterialDebugTex.SetPixels(pixels);
            cellMaterialDebugTex.Apply(false, false);
            sharedTerrainMaterial.SetTexture(PropCellMaterialDebugTex, cellMaterialDebugTex);

            // Also set terrain origin and world size so the shader can map worldPos -> cell UV.
            Vector3 origin = transform.position;
            float worldSizeX = (TotalSamplesX - 1) * voxelSizeMeters;
            float worldSizeZ = (TotalSamplesZ - 1) * voxelSizeMeters;
            sharedTerrainMaterial.SetFloat(PropTerrainOriginX, origin.x);
            sharedTerrainMaterial.SetFloat(PropTerrainOriginZ, origin.z);
            sharedTerrainMaterial.SetFloat(PropTerrainWorldSizeX, worldSizeX);
            sharedTerrainMaterial.SetFloat(PropTerrainWorldSizeZ, worldSizeZ);
            sharedTerrainMaterial.SetFloat(Shader.PropertyToID("_VoxelSize"), voxelSizeMeters);
            sharedTerrainMaterial.SetFloat(Shader.PropertyToID("_TotalSamplesX"), TotalSamplesX);
            sharedTerrainMaterial.SetFloat(Shader.PropertyToID("_TotalSamplesZ"), TotalSamplesZ);

            // Turn on debug sampling; shader will sample _CellMaterialDebugTex when this is non-zero.
            sharedTerrainMaterial.SetFloat(PropDebugCellMaterial, 1f);
        }
        else
        {
            // Defer texture creation to the main thread: store pixels and set pending flag.
            cellMaterialDebugPendingPixels = pixels;
            cellMaterialDebugPending = true;
        }
    }

    // Call from main thread (e.g., CommitChunkMesh) to apply any pending debug texture.
    internal void ApplyPendingCellMaterialDebugTextureIfNeeded()
    {
        if (!cellMaterialDebugPending || cellMaterialDebugPendingPixels == null)
            return;

        int w = TotalSamplesX;
        int h = TotalSamplesZ;

        if (cellMaterialDebugTex == null || cellMaterialDebugTex.width != w || cellMaterialDebugTex.height != h)
        {
            cellMaterialDebugTex = new Texture2D(w, h, TextureFormat.RGBA32, false, true)
            {
                name        = "CellMaterialDebug",
                filterMode  = FilterMode.Point,
                wrapMode    = TextureWrapMode.Clamp
            };
        }

        cellMaterialDebugTex.SetPixels(cellMaterialDebugPendingPixels);
        cellMaterialDebugTex.Apply(false, false);
        sharedTerrainMaterial.SetTexture(PropCellMaterialDebugTex, cellMaterialDebugTex);

        Vector3 origin = transform.position;
        float worldSizeX = (TotalSamplesX - 1) * voxelSizeMeters;
        float worldSizeZ = (TotalSamplesZ - 1) * voxelSizeMeters;
        sharedTerrainMaterial.SetFloat(PropTerrainOriginX, origin.x);
        sharedTerrainMaterial.SetFloat(PropTerrainOriginZ, origin.z);
        sharedTerrainMaterial.SetFloat(PropTerrainWorldSizeX, worldSizeX);
        sharedTerrainMaterial.SetFloat(PropTerrainWorldSizeZ, worldSizeZ);
        sharedTerrainMaterial.SetFloat(Shader.PropertyToID("_VoxelSize"), voxelSizeMeters);
        sharedTerrainMaterial.SetFloat(Shader.PropertyToID("_TotalSamplesX"), TotalSamplesX);
        sharedTerrainMaterial.SetFloat(Shader.PropertyToID("_TotalSamplesZ"), TotalSamplesZ);
        sharedTerrainMaterial.SetFloat(PropDebugCellMaterial, 1f);

        // Clear pending state
        cellMaterialDebugPendingPixels = null;
        cellMaterialDebugPending = false;
    }
}
