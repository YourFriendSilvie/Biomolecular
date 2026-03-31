using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

// ─────────────────────────────────────────────────────────────────────────────
// SurfaceNetsTerrain — standalone surface-nets terrain implementation.
//
// Based on Jorisar's GDVoxelTerrain (https://jorisar.github.io/).
// Attach to any empty GameObject; call Generate() from the Context Menu
// (or set generateOnAwake = true).
//
// Key differences from ProceduralVoxelTerrain that eliminate the cell-faceting
// artefact (diamond / honeycomb pattern on flat terrain):
//
//   • Normals are computed from the ANALYTICAL height-map gradient sampled at
//     `normalSampleMetres` (default 3 m) — not from per-voxel density FD.
//     This removes the 1 m noise signal from the shading calculation.
//
//   • Slope-based vertex colours are baked on the CPU: flat → moss-green,
//     steep → greywacke/basalt, near sea level → sand.  This matches Jorisar's
//     shader approach (no profile-texture lookup needed).
//
//   • Winding is determined by density sign of the primary sample — verified
//     correct for each axis by cross-product analysis.
//
//   • 1-cell halo around each chunk lets adjacent chunks share edge vertices
//     without visible seams.
// ─────────────────────────────────────────────────────────────────────────────
[AddComponentMenu("Biomolecular/Surface Nets Terrain (Standalone)")]
public class SurfaceNetsTerrain : MonoBehaviour
{
    // ── Inspector ─────────────────────────────────────────────────────────────

    [Header("Chunks")]
    [SerializeField, Min(1)] private int chunksX = 8;
    [SerializeField, Min(1)] private int chunksY = 2;
    [SerializeField, Min(1)] private int chunksZ = 8;
    [SerializeField, Min(4)] private int cellsPerAxis = 16;
    [SerializeField, Min(0.1f)] private float voxelSize = 1f;

    [Header("Height Map")]
    [SerializeField] private float baseSurfaceHeight = 20f;
    [SerializeField] private float seaLevel = 5f;
    [SerializeField] private float surfaceNoiseScale = 120f;
    [SerializeField] private float surfaceAmplitude = 30f;
    [SerializeField, Min(1)] private int noiseOctaves = 5;
    [SerializeField, Range(0f, 1f)] private float noisePersistence = 0.45f;
    [SerializeField] private float ridgeNoiseScale = 64f;
    [SerializeField] private float ridgeAmplitude = 20f;
    [SerializeField] private int noiseSeed = 0;

    [Header("Normals")]
    [Tooltip("Height-gradient sample distance in metres.  Larger = smoother normals.")]
    [SerializeField, Min(0.5f)] private float normalSampleMetres = 3f;

    [Header("Colours")]
    [SerializeField] private Color grassColor  = new Color(0.20f, 0.34f, 0.11f);
    [SerializeField] private Color rockColor   = new Color(0.25f, 0.27f, 0.24f);
    [SerializeField] private Color sandColor   = new Color(0.66f, 0.58f, 0.46f);
    [Tooltip("dot(normal, up) below this → rock colour.")]
    [SerializeField, Range(0f, 1f)] private float rockSlopeMin = 0.40f;
    [Tooltip("dot(normal, up) above this → fully grass colour.")]
    [SerializeField, Range(0f, 1f)] private float rockSlopeMax = 0.70f;
    [Tooltip("Metres above sea level where sand ends.")]
    [SerializeField] private float sandHeight = 2.0f;

    [Header("Material")]
    [SerializeField] private Material terrainMaterial;

    [Header("Auto")]
    [SerializeField] private bool generateOnAwake = false;

    // ── Runtime ───────────────────────────────────────────────────────────────

    private readonly Dictionary<Vector3Int, GameObject> chunkObjects = new();
    private Vector2 noiseOff;
    private const float IsoLevel = 0f;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    private void Awake()
    {
        if (generateOnAwake) Generate();
    }

    // ── Public API ────────────────────────────────────────────────────────────

    [ContextMenu("Generate")]
    public void Generate()
    {
        Clear();
        noiseOff = new Vector2(noiseSeed * 137.3f, noiseSeed * 98.7f);
        for (int cz = 0; cz < chunksZ; cz++)
        for (int cy = 0; cy < chunksY; cy++)
        for (int cx = 0; cx < chunksX; cx++)
            BuildChunk(new Vector3Int(cx, cy, cz));
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        foreach (var go in chunkObjects.Values)
            if (go != null) DestroyImmediate(go);
        chunkObjects.Clear();
    }

    // ── Density & height ──────────────────────────────────────────────────────

    // Terrain height at world XZ (metres above Y=0).
    private float HeightAt(float wx, float wz)
    {
        float h    = baseSurfaceHeight;
        float amp  = surfaceAmplitude;
        float freq = 1f / surfaceNoiseScale;
        float nx   = wx * freq + noiseOff.x;
        float nz   = wz * freq + noiseOff.y;

        // Fractal Brownian Motion
        for (int i = 0; i < noiseOctaves; i++)
        {
            h   += (Mathf.PerlinNoise(nx, nz) * 2f - 1f) * amp;
            amp *= noisePersistence;
            nx  *= 2f;
            nz  *= 2f;
        }

        // Ridge noise (sharp mountain ridges via abs-folded gradient)
        float rn = Mathf.PerlinNoise(
            wx / ridgeNoiseScale + 500f + noiseOff.x,
            wz / ridgeNoiseScale + 500f + noiseOff.y);
        h += (1f - Mathf.Abs(rn * 2f - 1f)) * ridgeAmplitude;

        return h;
    }

    // Density: positive = solid (below surface), negative = air (above surface).
    private float Density(float wx, float wy, float wz) => HeightAt(wx, wz) - wy;

    // Outward surface normal computed from the height-map gradient.
    // Sampled at normalSampleMetres to suppress 1 m noise → smooth shading.
    private Vector3 SmoothNormal(float wx, float wz)
    {
        float e    = normalSampleMetres;
        float dHdx = (HeightAt(wx + e, wz) - HeightAt(wx - e, wz)) / (2f * e);
        float dHdz = (HeightAt(wx, wz + e) - HeightAt(wx, wz - e)) / (2f * e);
        // Gradient of density H(x,z)-y = (∂H/∂x, 1, ∂H/∂z) (outward, toward air)
        return new Vector3(dHdx, 1f, dHdz).normalized;
    }

    // ── Chunk builder ─────────────────────────────────────────────────────────

    private void BuildChunk(Vector3Int coord)
    {
        int   N   = cellsPerAxis;
        int   N2  = N + 2;   // extended dimension with 1-cell halo on each side
        float vs  = voxelSize;

        // World-space origin of this chunk's corner (cell 0,0,0).
        Vector3 origin = new Vector3(
            coord.x * N * vs,
            coord.y * N * vs,
            coord.z * N * vs);

        // Extended vertex-index table.  -1 = no vertex.
        // Local coordinates lx ∈ [-1, N] map to array index lx+1 ∈ [0, N+1].
        int[] vi = new int[N2 * N2 * N2];
        for (int i = 0; i < vi.Length; i++) vi[i] = -1;

        var verts  = new List<Vector3>();
        var norms  = new List<Vector3>();
        var colors = new List<Color32>();
        var tris   = new List<int>();

        // Helper: compute flat array index with halo offset.
        int VIdx(int lx, int ly, int lz) =>
            (lx + 1) + N2 * ((ly + 1) + N2 * (lz + 1));

        // Corner offsets for the 8 corners of a unit cube.
        // Order: 0=(0,0,0) 1=(1,0,0) 2=(1,0,1) 3=(0,0,1)
        //        4=(0,1,0) 5=(1,1,0) 6=(1,1,1) 7=(0,1,1)
        Vector3[] CC = {
            new(0,0,0), new(1,0,0), new(1,0,1), new(0,0,1),
            new(0,1,0), new(1,1,0), new(1,1,1), new(0,1,1)
        };

        // 12 cube edges: [edgeIdx, {cornerA, cornerB}]
        // X-edges 0-3, Y-edges 4-7, Z-edges 8-11
        int[,] E = {
            {0,1},{3,2},{4,5},{7,6},
            {0,4},{1,5},{2,6},{3,7},
            {0,3},{1,2},{4,7},{5,6}
        };

        float[] cd = new float[8]; // corner densities

        // ── Stage 1: one vertex per surface cell (interior + 1-cell halo) ─────
        for (int lz = -1; lz <= N; lz++)
        for (int ly = -1; ly <= N; ly++)
        for (int lx = -1; lx <= N; lx++)
        {
            float wx0 = origin.x + lx * vs;
            float wy0 = origin.y + ly * vs;
            float wz0 = origin.z + lz * vs;

            bool anySolid = false, anyAir = false;
            for (int c = 0; c < 8; c++)
            {
                cd[c] = Density(
                    wx0 + CC[c].x * vs,
                    wy0 + CC[c].y * vs,
                    wz0 + CC[c].z * vs);
                if (cd[c] > IsoLevel) anySolid = true; else anyAir = true;
            }
            if (!anySolid || !anyAir) continue; // cell does not cross the surface

            // Place vertex at the centroid of interpolated edge-crossing positions.
            Vector3 posSum = Vector3.zero;
            int crossCount = 0;
            for (int e = 0; e < 12; e++)
            {
                float dA = cd[E[e, 0]], dB = cd[E[e, 1]];
                if ((dA > IsoLevel) == (dB > IsoLevel)) continue;
                float t = (IsoLevel - dA) / (dB - dA);
                posSum += new Vector3(
                    wx0 + Mathf.Lerp(CC[E[e,0]].x, CC[E[e,1]].x, t) * vs,
                    wy0 + Mathf.Lerp(CC[E[e,0]].y, CC[E[e,1]].y, t) * vs,
                    wz0 + Mathf.Lerp(CC[E[e,0]].z, CC[E[e,1]].z, t) * vs);
                crossCount++;
            }
            if (crossCount == 0) continue;

            Vector3 worldPos  = posSum / crossCount;
            Vector3 localPos  = worldPos - origin;   // relative to chunk
            Vector3 normal    = SmoothNormal(worldPos.x, worldPos.z);
            Color32 vertColor = BakeColor(normal, worldPos.y);

            int idx = verts.Count;
            verts.Add(localPos);
            norms.Add(normal);
            colors.Add(vertColor);
            vi[VIdx(lx, ly, lz)] = idx;
        }

        if (verts.Count == 0) return;

        // ── Stage 2: one quad per crossing edge (chunk's own edges only) ───────
        //
        // For each axis, the chunk "owns" crossing edges whose PRIMARY sample
        // index is in the chunk's local range [0, N-1].  Halo cells (index -1)
        // supply vertices for quads at the negative-face boundary.
        //
        // Verified winding rules (density sign → outward normal direction):
        //   X-crossing: flip = !(d0 > IsoLevel)   → +X when solid is at −X side
        //   Y-crossing: flip =  (d0 > IsoLevel)   → +Y when solid is below
        //   Z-crossing: flip = !(d0 > IsoLevel)   → +Z when solid is at −Z side

        for (int lz = 0; lz < N; lz++)
        for (int ly = 0; ly < N; ly++)
        for (int lx = 0; lx < N; lx++)
        {
            float wx = origin.x + lx * vs;
            float wy = origin.y + ly * vs;
            float wz = origin.z + lz * vs;

            // ── X-crossing ───────────────────────────────────────────────────
            {
                float d0 = Density(wx,      wy, wz);
                float d1 = Density(wx + vs, wy, wz);
                if ((d0 > IsoLevel) != (d1 > IsoLevel))
                {
                    int v0 = vi[VIdx(lx,   ly,   lz  )];
                    int v1 = vi[VIdx(lx,   ly-1, lz  )];
                    int v2 = vi[VIdx(lx,   ly-1, lz-1)];
                    int v3 = vi[VIdx(lx,   ly,   lz-1)];
                    if (v0 >= 0 && v1 >= 0 && v2 >= 0 && v3 >= 0)
                        EmitQuad(v0, v1, v2, v3, !(d0 > IsoLevel), verts, tris);
                }
            }

            // ── Y-crossing ───────────────────────────────────────────────────
            {
                float d0 = Density(wx, wy,      wz);
                float d1 = Density(wx, wy + vs, wz);
                if ((d0 > IsoLevel) != (d1 > IsoLevel))
                {
                    int v0 = vi[VIdx(lx,   ly, lz  )];
                    int v1 = vi[VIdx(lx-1, ly, lz  )];
                    int v2 = vi[VIdx(lx-1, ly, lz-1)];
                    int v3 = vi[VIdx(lx,   ly, lz-1)];
                    if (v0 >= 0 && v1 >= 0 && v2 >= 0 && v3 >= 0)
                        EmitQuad(v0, v1, v2, v3, (d0 > IsoLevel), verts, tris);
                }
            }

            // ── Z-crossing ───────────────────────────────────────────────────
            {
                float d0 = Density(wx, wy, wz     );
                float d1 = Density(wx, wy, wz + vs);
                if ((d0 > IsoLevel) != (d1 > IsoLevel))
                {
                    int v0 = vi[VIdx(lx,   ly,   lz)];
                    int v1 = vi[VIdx(lx-1, ly,   lz)];
                    int v2 = vi[VIdx(lx-1, ly-1, lz)];
                    int v3 = vi[VIdx(lx,   ly-1, lz)];
                    if (v0 >= 0 && v1 >= 0 && v2 >= 0 && v3 >= 0)
                        EmitQuad(v0, v1, v2, v3, !(d0 > IsoLevel), verts, tris);
                }
            }
        }

        if (tris.Count < 3) return;

        // ── Build Unity mesh ─────────────────────────────────────────────────
        var mesh = new Mesh
        {
            name        = $"SNT_{coord.x}_{coord.y}_{coord.z}",
            indexFormat = IndexFormat.UInt32
        };
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0, false);
        mesh.SetNormals(norms);
        mesh.SetColors(colors);
        mesh.RecalculateBounds();

        var go = new GameObject($"Chunk {coord.x}-{coord.y}-{coord.z}");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = origin;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = terrainMaterial;

        if (mesh.vertexCount >= 3)
        {
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
        }

        chunkObjects[coord] = go;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Slope-based vertex colour (Jorisar's shader approach, baked on CPU).
    //   flat (dot→1)  → grass/soil
    //   steep (dot→0) → exposed rock
    //   near sea level → sand
    private Color32 BakeColor(Vector3 normal, float worldY)
    {
        float slopeUp  = Mathf.Clamp01(Vector3.Dot(normal, Vector3.up));
        float rockBlend = 1f - Mathf.SmoothStep(rockSlopeMin, rockSlopeMax, slopeUp);
        Color c = Color.Lerp(grassColor, rockColor, rockBlend);

        // Sand: blend in near sea level.
        float sandBlend = 1f - Mathf.SmoothStep(seaLevel - 1f, seaLevel + sandHeight, worldY);
        c = Color.Lerp(c, sandColor, Mathf.Clamp01(sandBlend));

        return new Color32(
            (byte)Mathf.RoundToInt(Mathf.Clamp01(c.r) * 255f),
            (byte)Mathf.RoundToInt(Mathf.Clamp01(c.g) * 255f),
            (byte)Mathf.RoundToInt(Mathf.Clamp01(c.b) * 255f),
            255);
    }

    // Emit two triangles for a quad (v0,v1,v2,v3), splitting on the shorter diagonal.
    // flip=true reverses winding so the outward normal matches the surface direction.
    private static void EmitQuad(
        int v0, int v1, int v2, int v3, bool flip,
        List<Vector3> verts, List<int> tris)
    {
        Vector3 p0 = verts[v0], p1 = verts[v1], p2 = verts[v2], p3 = verts[v3];

        // Shorter diagonal is the shared edge — reduces non-planar shading artefacts.
        bool useAC = (p0 - p2).sqrMagnitude <= (p1 - p3).sqrMagnitude;

        if (useAC)
        {
            if (!flip)
            {
                tris.Add(v0); tris.Add(v1); tris.Add(v2);
                tris.Add(v0); tris.Add(v2); tris.Add(v3);
            }
            else
            {
                tris.Add(v0); tris.Add(v2); tris.Add(v1);
                tris.Add(v0); tris.Add(v3); tris.Add(v2);
            }
        }
        else
        {
            if (!flip)
            {
                tris.Add(v0); tris.Add(v1); tris.Add(v3);
                tris.Add(v1); tris.Add(v2); tris.Add(v3);
            }
            else
            {
                tris.Add(v0); tris.Add(v3); tris.Add(v1);
                tris.Add(v1); tris.Add(v3); tris.Add(v2);
            }
        }
    }

    // ── Editor preset ─────────────────────────────────────────────────────────

    [ContextMenu("Apply Olympic Peninsula Preset")]
    public void ApplyOlympicPreset()
    {
        chunksX             = 10;
        chunksY             = 3;
        chunksZ             = 10;
        cellsPerAxis        = 16;
        voxelSize           = 1f;
        baseSurfaceHeight   = 22f;
        seaLevel            = 5f;
        surfaceNoiseScale   = 140f;
        surfaceAmplitude    = 32f;
        noiseOctaves        = 6;
        noisePersistence    = 0.45f;
        ridgeNoiseScale     = 70f;
        ridgeAmplitude      = 25f;
        normalSampleMetres  = 4f;
        sandHeight          = 2.5f;
    }
}
