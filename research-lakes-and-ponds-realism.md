# Realistic Lakes, Ponds, and Lakeshores in Biomolecular

*Research query: Redoing lakes and ponds to be closer to real life in a voxel game — scale, lakeshores, and freshwater generation moving forward.*

---

## Executive Summary

The current freshwater generation system is architecturally sound — carve-first pipeline, flood-fill mesh, density-based placement, rejection guards — but is tuned at a miniature scale that doesn't feel like real water. The biggest win is a size and depth overhaul: real Pacific Northwest lakes are 25–150m in radius and 4–15m deep; current settings are 8–14m and 1.5m. Beyond sizing, the highest-value additions are (1) an expanded shore zone that paints gravel/mud/sand visibly outside the waterline, (2) riparian vegetation scatter near water edges, (3) irregular basin shapes via Perlin-offset carving, and (4) a shallow littoral shelf at the basin rim. This document covers real-world grounding, what to change in the existing code, and an ordered roadmap.

---

## 1. Real-World Grounding: Pacific Northwest Freshwater

The game's biome is a coastal Pacific Northwest rainforest island (Douglas-fir, Western Red Cedar, Red Alder, Serviceberry). This is closely analogous to the Olympic Peninsula, southern Vancouver Island, or the northern Oregon coast — one of the most lake-dense temperate regions on Earth.

### 1.1 Lake Size Reference Data

| Lake Type | Typical Radius | Depth | Formation |
|---|---|---|---|
| **Beaver pond** | 8–25 m | 0.5–2 m | Dam-built, shallow, organic |
| **Prairie/kettlehole pond** | 10–50 m | 1–4 m | Glacial kettle depression |
| **Small forested lake** | 25–100 m | 3–12 m | Glacial scour, cirque, or fluvial |
| **Mid-elevation tarn** | 30–150 m | 5–20 m | Glacial cirque |
| **Coastal bog lake** | 20–80 m | 1–6 m | Sphagnum/peat-impounded |

**Key PNW examples at small scale:**
- Duck Lake, Olympic Peninsula: ~120 m radius, 5 m max depth
- Sol Duc Lake (small): ~60 m radius, 12 m depth  
- Typical roadside "lake" signs in PNW: 40–120 m radius
- Typical beaver ponds on rainforest streams: 10–30 m radius, 0.5–1.5 m

### 1.2 Current Game Scale vs. Reality

The game uses `voxelSizeMeters = 1f`, `cellsPerChunkAxis = 16` → `ChunkWorldSizeMeters = 16 m`. A 20×20-chunk terrain is **320 m × 320 m**; a 50×50 terrain is **800 m × 800 m**.

| Parameter | Current Value | Realistic PNW Range | Gap |
|---|---|---|---|
| Lake radius | 8–14 m | 25–100 m | ~5–7× too small |
| Lake depth | 1.5 m | 4–12 m | ~4–6× too shallow |
| Pond radius | 3.5–6.5 m | 8–25 m | ~3–4× too small |
| Pond depth | 0.75 m | 0.5–2 m | Reasonable |
| Shore zone paint width | ~0 m beyond basin | 2–15 m | None currently |

A 14 m radius lake on a 320 m island is 8.75% of the terrain width — visible but tiny. In reality, a forested lake on a 300 m island would occupy 10–35% of the width. The current values produce what feel like "mud puddles" rather than lakes.

### 1.3 Why Depth Matters More Than You'd Think

With `voxelSizeMeters = 1f`, depth controls **how many voxels the basin displaces**:
- Current 1.5 m depth: basin is **1–2 voxels** deep — barely a scratch
- Realistic 6 m depth: basin is **6 voxels** deep — a visible geological feature, players can stand inside it, materials layer properly (mud → gravel → sand)
- At 10 m depth, players could potentially "swim" or find shelter inside the basin

Deeper basins also make the shore zone far more visually interesting because the terrain transitions from hilltop → shore slope → shallow shelf → deep center.

---

## 2. The Current System: Architectural Summary

Understanding what already exists avoids rework.

### 2.1 Pipeline (as of latest checkpoint)

```
Placement candidate found
        ↓
Slope / elevation / spacing / shoreline guards
        ↓
Beach guard (surfaceY - depth ≥ seaLevel + margin)
        ↓
Cliff rim guard (8 inner + 8 outer samples)
        ↓
Outer ring guard (8 samples at radius × 1.5)
        ↓
CarveLake() → terrain voxels modified, chunks rebuilt
        ↓
TrySolveCarvedLakeLocally() → flood-fill mesh on carved basin
        ↓
GameObject created with mesh
```

Source: `FreshwaterGenerator.cs:827–1050` (GenerateFreshwaterBodies), `WaterTerrainCarver.cs:10–119` (carve), `FreshwaterGenerator.cs:473–518` (post-carve solve).

### 2.2 Current Carve Shape

`WaterTerrainCarver.ApplyLakeCarveDensity` [^1] uses **overlapping density-sphere brushes**:
- **Central bowl**: 1 brush at `radius × 0.62`, depth × 1.15
- **Inner ring**: 8 brushes at `radius × 0.34`, placed at `radius × 0.34` outward — creates a slightly uneven floor
- **Mid ring**: 12 brushes at `radius × 0.18`, placed at `radius × 0.72` — shapes the mid-slope
- **Shoreline taper**: 16 brushes at `radius × 0.12`, placed at `radius × 0.86` — very shallow shoulder

This produces a roughly circular, smooth bowl. It looks believable at small scale but at 50+ m radius will look perfectly circular and artificial.

### 2.3 Material Painting

`WaterTerrainCarver.PaintFreshwaterBasinMaterials` (called from `CarveLake`) applies:
- `Lake Mud` at the basin floor
- `Basin Sand` at the inner shoreline
- `Basin Gravel` at the outer shoreline

Currently the paint only covers the carved area (inside `radius`). No material is painted **outside** the waterline, so there's no visible dry lakeshore. [^2]

### 2.4 What's Already Correct

- Carve-first pipeline (no pre-solve overhead)
- Post-carve flood-fill bypasses open-boundary rejection
- Beach and cliff rejection guards
- Density-based count scaling
- The `isPond` flag already bifurcates depth/radius paths

---

## 3. Recommended Changes (Ordered by Impact / Effort)

### 3.1 [HIGH IMPACT, LOW EFFORT] Size and Depth Overhaul

**The single biggest improvement.** Just change these inspector/config values:

```csharp
// ProceduralVoxelTerrainWaterSystem.cs — current vs. recommended
lakeRadiusRangeMeters = new Vector2(25f, 80f);   // was (8, 14)
lakeDepthMeters = 6f;                             // was 1.5
pondRadiusRangeMeters = new Vector2(8f, 20f);     // was (3.5, 6.5)
pondDepthMeters = 1.5f;                           // was 0.75

// Density adjustments: larger lakes → fewer needed per region
lakeDensityPer16x16Chunks = 0.8f;   // was 2.5 (larger bodies, fewer)
pondDensityPer16x16Chunks = 2.0f;   // was 4.0
```

**Also update `WaterSimulationConfig.cs`** preset [^3] to match.

**Scale math for verification (20×20 terrain, 16m chunks → 320 m):**
- 25 m radius lake = 15.6% terrain width ✓ (visible, not overwhelming)
- 80 m radius lake = 50% terrain width (only on large terrains; density scaling prevents over-spawning)
- Density 0.8 on 20×20 (1.5625 ref regions) → 1.25 → 1 lake (expected)
- Density 0.8 on 50×50 (9.77 ref regions) → 7.8 → 8 lakes (good for 800m terrain)

**Rejection guard tuning for larger lakes:**

The outer ring beach check uses `radius * 1.5`. At 80m radius, that samples 120 m out from center — could reject valid inland lakes. Recommend keeping the multiplier but checking whether the *absolute* outer ring distance (`radius * 1.5 - radius = radius * 0.5`) is reasonable. For large lakes, this 0.5× margin = 40 m of "safety zone" which is appropriate.

The cliff guard checks `radius * 0.96` (inside) and `radius * 1.12` (outside). At 80 m radius, 1.12× = 89.6 m — still only sampling 8 points, which is very sparse for a large basin. Consider scaling sample count with radius:

```csharp
// In GenerateFreshwaterBodies, replace the fixed 8-sample rim check:
int rimSampleCount = Mathf.Clamp(Mathf.RoundToInt(radius * 0.5f), 8, 24);
for (int rimSample = 0; rimSample < rimSampleCount && !rimHasCliff; rimSample++)
```

### 3.2 [HIGH IMPACT, MEDIUM EFFORT] Visible Shore Zone — Material Painting

Currently, the shore outside the waterline looks like unmodified terrain. Real lakeshores have a **riparian zone**: wet soil, gravel, sand, mud — all distinctly colored and textured, extending several meters beyond the waterline.

**Add to `WaterTerrainCarver.PaintFreshwaterBasinMaterials`:**

Paint a ring *outside* the carved basin with:
- **Outer shore** (radius * 1.0 → radius * 1.35): `Basin Sand` or `Basin Gravel` scattered by noise
- **Transition zone** (radius * 1.35 → radius * 1.7): `Lake Mud` or organic-dampened `Topsoil`

```csharp
// After existing PaintFreshwaterBasinMaterials call, add shore zone:
public static void PaintFreshwaterShoreZone(
    ProceduralVoxelTerrain terrain,
    Vector3 center,
    float radiusMeters,
    float surfaceY,
    bool isPond)
{
    float shoreOuterRadius = radiusMeters * 1.35f;
    float transitionRadius = radiusMeters * 1.7f;
    // Sample the shoreline ring and paint gravel/sand/mud outward
    // Use terrain.PaintMaterialRadialGradient or equivalent
}
```

This requires either an existing terrain painting API that accepts a radius + material, or a new utility. The terrain already has `lakeMudIndex`, `basinSandIndex`, `basinGravelIndex` material types. [^4]

**Visual result:** A visible ring of pale sand/gravel around every lake and pond, immediately communicating "there is water here" even before the player sees the water surface.

### 3.3 [HIGH IMPACT, MEDIUM EFFORT] Riparian Vegetation Scatter

The scatter system (`ProceduralVoxelTerrainScatterer`) currently places vegetation uniformly based on biome. Real lakeshores have distinct **riparian zones** dominated by:
- Red Alder (*already in preset*) — dominant PNW riparian tree
- Black Cottonwood — tall, near permanent water
- Willows — very close to waterline
- Sedges and rushes (not yet modeled)
- Skunk cabbage, ferns — marshy edges

**Implementation approach:**

Add a new placement **affinity** to `TerrainScatterPrototype`: a `waterAffinityMode` enum:

```csharp
public enum WaterAffinityMode
{
    None,          // current behavior
    Riparian,      // prefer within X meters of lake edge
    Upland,        // avoid within X meters of lake edge (dry species)
    Aquatic        // only within the lake influence zone
}

public WaterAffinityMode waterAffinityMode = WaterAffinityMode.None;
[Min(0f)] public float waterAffinityRadiusMeters = 20f; // how far from shore to bias
[Range(0f, 1f)] public float waterAffinityStrength = 0.7f;
```

In `ScatterPrototypePlacer.PlacePrototypeInstances`, after passing the water exclusion check, add a **bias check**:

```csharp
// Existing: reject if inside water
// New: for riparian prototypes, also reject if too far from water
if (prototype.waterAffinityMode == WaterAffinityMode.Riparian &&
    prototype.waterAffinityStrength > 0f)
{
    float distToNearestWater = resolvedWaterSystem?.GetDistanceToNearestFreshwater(
        screeningSurfacePoint) ?? float.MaxValue;
    float targetDist = prototype.waterAffinityRadiusMeters;
    if (distToNearestWater > targetDist)
    {
        // Probabilistically reject based on excess distance
        float excess = (distToNearestWater - targetDist) / targetDist;
        if (random.NextDouble() < excess * prototype.waterAffinityStrength)
            continue; // reject
    }
}
```

This requires `GetDistanceToNearestFreshwater(Vector3 pos)` on `ProceduralVoxelTerrainWaterSystem`, which can iterate `generatedLakes` and return the closest shore distance.

**Preset updates:**
- Red Alder: `waterAffinityMode = Riparian`, strong bias (0.8), radius 25m
- Western Red Cedar: mild riparian bias (0.4), radius 40m (tolerates wet but not purely riparian)
- Douglas-fir: `waterAffinityMode = Upland`, mild exclusion from very close shore

### 3.4 [MEDIUM IMPACT, MEDIUM EFFORT] Shallow Littoral Shelf

Real lakes don't go immediately from waterline to maximum depth. They have a **littoral zone** (0–3 m depth, well-lit, vegetated) that extends 5–30% of the radius inward, then drops steeply to the **profundal zone**.

Currently `ApplyLakeCarveDensity` uses a smooth bowl, so it does have some tapering, but the outer 16 brushes at `radius * 0.86` only produce `depthMeters * 0.18` = 1.1m shallowing at 6m depth — still quite steep.

**Add a true littoral shelf:**

```csharp
// In ApplyLakeCarveDensity, replace the 16-brush shoreline ring with:
// 1. A wide, very shallow annulus at the rim (0.1–0.3m depth)
float littoralInnerRadius = radiusMeters * 0.78f;  // shelf starts here
float littoralOuterRadius = radiusMeters * 0.94f;  // rim
float littoralDepth = depthMeters * 0.08f;         // ~8% depth → very shallow wading zone

for (int i = 0; i < 16; i++)
{
    float angle = (Mathf.PI * 2f * i) / 16f;
    float d = Mathf.Lerp(littoralInnerRadius, littoralOuterRadius, 0.5f);
    Vector3 pos = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * d;
    terrain.ApplyDensityBrushWorld(
        new Vector3(pos.x, surfaceY - littoralDepth * 0.5f, pos.z),
        radiusMeters * 0.18f,
        densitySign * littoralDepth * 1.2f,
        false);
}
```

Visual result: A clearly visible shallow wading zone (ankle–knee deep) around the lake perimeter before the drop to full depth.

### 3.5 [MEDIUM IMPACT, HIGH EFFORT] Non-Circular Basin Shapes

Real PNW lakes formed by:
- **Glacial scour**: elongated ellipses, often NW–SE oriented
- **Kettle lakes**: irregular, bumpy outlines from uneven ice-block melting
- **River-formed**: one end tapers where the inlet is

**Option A — Elliptical lakes (low effort):**

Store `radiusX` and `radiusZ` separately in `GeneratedLake`. In `CarveLake`, scale the x and z density brush offsets by the respective radii. Requires adding fields to `GeneratedLake` and threading the ellipse parameters through `FreshwaterGenerator` and `WaterTerrainCarver`.

```csharp
// GeneratedLake — add:
public float radiusX;  // was just `radius`
public float radiusZ;
public float orientationAngleDeg;  // rotates the ellipse
```

**Option B — Perlin-displaced radius (medium effort):**

Sample a low-frequency Perlin noise value at each shoreline angle to perturb the effective radius by ±20–35%:

```csharp
// In CarveLake, replace fixed radiusMeters with:
float PerturbedRadius(float angle)
{
    float n = Mathf.PerlinNoise(
        center.x / 50f + Mathf.Cos(angle) * 3f,
        center.z / 50f + Mathf.Sin(angle) * 3f);
    return radiusMeters * Mathf.Lerp(0.75f, 1.25f, n);
}
```

Apply this perturbation to each of the mid-ring and outer-ring brushes so the basin shape varies naturally. The center brush stays circular (to ensure a consistent deep zone).

**Option C — Concatenated lobes (highest effort, best results):**

Real PNW lakes are often multi-lobed. A lake could be two overlapping ellipses at slightly different heights. This would require generating 2–3 sub-basins and merging them — more like the existing `BuildMergeTestRidge` utility in `WaterTerrainCarver.cs:121`.

**Recommendation:** Start with Option B (Perlin displacement) as it's largely self-contained in `WaterTerrainCarver` and requires no domain model changes.

### 3.6 [MEDIUM IMPACT, MEDIUM EFFORT] Wetlands / Marshes (New Feature)

Wetlands are where shallow water meets land — the transition zone. In the PNW, any low-lying flat area near the coast or at the foot of a slope can become a marsh.

**Characteristics:**
- Water depth: 0–0.5 m (barely submerged or saturated soil)
- No basin carving needed — water sits at terrain surface
- Dense sedge/rush vegetation at the wetland boundary
- `Lake Mud` / `Peat` / `Organic` material profile
- Visually distinct from open water (should look green, not blue)

**Implementation sketch:**
1. Add `WaterBodyType.Marsh` as a third category (alongside lakes and ponds)
2. Marsh placement: flat terrain (slope < 5°), above sea level, adjacent to lower terrain or existing water
3. No `CarveLake` call — instead, flood-fill at terrain surface and render with a different material/shader
4. Dedicated riparian scatter (sedges as sprites, rushes, skunk cabbage)

This is a full feature but high gameplay impact — players can forage wetland plants, find frogs, collect peat.

### 3.7 [LOW EFFORT, HIGH VISUAL IMPACT] Depth-Driven Lake Size Classes

Instead of the binary lake/pond, introduce **three size classes** that produce different experience profiles:

| Class | Radius | Depth | Visual Character |
|---|---|---|---|
| **Seep/Pool** | 5–12 m | 0.3–1.0 m | Clear, still, spring-fed |
| **Pond** | 10–30 m | 1.0–3.0 m | Beaver pond feel |
| **Lake** | 30–120 m | 4–14 m | Major terrain feature |

In code, this could be done by:
1. Keeping `isPond` bool for the pond/seep case
2. Adding `waterSizeClass` enum to `GeneratedLake` (purely informational)
3. Using the class to drive different shore zone width, material profiles, and scatter biases

No architecture changes needed — just parameterize the existing two-tier system slightly better and adjust the config values.

---

## 4. Architectural Gaps to Address

### 4.1 `WaterSimulationConfig.cs` Is Out of Sync

The `WaterSimulationConfig` ScriptableObject still uses the old `lakeCount`/`pondCount` int fields [^3] while `ProceduralVoxelTerrainWaterSystem` was updated to density-based floats. These should be aligned: update `WaterSimulationConfig` to expose `lakeDensityPer16x16Chunks` and `pondDensityPer16x16Chunks` instead of `lakeCount`/`pondCount`.

### 4.2 Lake Influence Bounds Are Under-Sized for Large Radii

`updateLakeInfluenceBounds` is called from `TrySolveCarvedLakeLocally` and from `TryInitializeLakeState`. For large lakes (80m radius), the influence bounds need to be large enough to prevent incorrect water-over-terrain rendering. The current padding uses `waterUpdatePaddingMeters` (default 2m), which is fine for 14m lakes but may need to scale: consider `Mathf.Max(waterUpdatePaddingMeters, radius * 0.1f)`.

### 4.3 Mesh Complexity at Large Radii

A lake of 80m radius × 40 outline samples at 1m voxel resolution will produce a mesh with ~5000–15000 triangles. The current `WaterMeshGenerator.BuildLakeMesh` already handles `UInt32` index format for >65535 vertices [^5], so there's no hard limit — but terrain patch building (`TryBuildLakeTerrainPatch`, which iterates mesh triangles from all affected chunks) gets proportionally slower.

**Mitigation already in place:**  
- The `lakeDynamicExpansionMeters` + `waterUpdatePaddingMeters` limits how many chunks are scanned
- The `LakeTriangleFilterJob` (Unity Jobs) already parallelizes triangle classification [^6]
- At 80m radius with 1m voxels across a 16-chunk terrain, ~25 chunks need scanning — manageable

**If performance is an issue at large radii:**  
- Pre-filter terrain patches by distance before the full solver  
- Cache terrain patches across regeneration (invalidate only on terrain edit)

### 4.4 Rejection Rate Will Spike for Large Lakes

With `lakeRadiusRangeMeters = (25, 80)`, a lake sampled at a random position has a much larger exclusion footprint. On a 320m terrain, an 80m-radius lake needs its center at least 80m from any border (after applying the 22% margin), leaving only a ~100m × 100m valid center zone — 10% of total terrain area. The current acceptance rate will drop significantly.

**Fix:** Adjust `inlandSamplingMargin` to scale with lake radius rather than a fixed percentage of terrain:

```csharp
// In GenerateFreshwaterBodies, replace the fixed margin fraction:
float inlandSamplingMargin = Mathf.Lerp(
    isPond ? 0.24f : 0.22f, 0.18f, attemptProgress * 0.75f);

// Better: use absolute pixel margin based on radius
float inlandAbsMarginMeters = radius * 1.15f;  // lake needs radius + small buffer
float inlandSamplingMargin = Mathf.Clamp01(inlandAbsMarginMeters / bounds.size.x);
```

Also increase the max attempts per target lake:

```csharp
// FreshwaterGenerationStats constructor is called with (isPond ? "Ponds" : "Lakes", targetCount, targetCount * 24)
// → targetCount * 24 is the maxAttempts
// For large lakes, use 48–72 instead:
FreshwaterGenerationStats stats = new FreshwaterGenerationStats(
    isPond ? "Ponds" : "Lakes",
    targetCount,
    targetCount * (isPond ? 24 : 48));  // more attempts for larger, harder-to-place lakes
```

---

## 5. Recommended Roadmap

### Phase 1: Scale Overhaul (1 day)
- Increase radius/depth ranges in `ProceduralVoxelTerrainWaterSystem` and `WaterSimulationConfig`
- Adjust `inlandSamplingMargin` to absolute-radius-based
- Increase max attempts for lakes
- Scale rim sample count with radius
- **Expected result:** Lakes look like actual lakes. Most visible improvement.

### Phase 2: Shore Zone (2–3 days)
- Add `PaintFreshwaterShoreZone` in `WaterTerrainCarver`
- Apply Basin Sand → transition to organic soil moving outward from waterline
- Add littoral shelf via a wider shallow outer brush ring
- **Expected result:** Visible pale shore ring around every lake, shallow wading zone

### Phase 3: Riparian Vegetation (2–3 days)
- Add `waterAffinityMode` + `waterAffinityRadiusMeters` to `TerrainScatterPrototype`
- Add `GetDistanceToNearestFreshwater` to `ProceduralVoxelTerrainWaterSystem`
- Update `ScatterPrototypePlacer` to apply riparian affinity check
- Configure Red Alder as riparian dominant
- **Expected result:** Alder and cedar clusters near water's edge

### Phase 4: Basin Shape Variation (2–4 days)
- Implement Perlin-displaced radius in `WaterTerrainCarver.ApplyLakeCarveDensity`
- Or implement elliptical lakes with orientation field in `GeneratedLake`
- **Expected result:** No two lakes look the same

### Phase 5: Wetlands / Marshes (1–2 weeks)
- New water body type (no-carve, surface-level)
- Dedicated scatter biome (sedges, rushes)
- Material profile (peat, organic mud)
- **Expected result:** Marshy lowlands, biological distinctness from open water

---

## 6. Quick Reference: Config Values to Use Today

These values can be applied immediately via the inspector or `WaterSimulationConfig` preset, with no code changes, and will produce a dramatically more realistic world:

```csharp
// WaterSimulationConfig.cs / ProceduralVoxelTerrainWaterSystem.cs

// Lakes: genuine forest lakes
lakeRadiusRangeMeters    = new Vector2(25f, 80f);   // was (8, 14)
lakeDepthMeters          = 6f;                       // was 1.5
lakeDensityPer16x16Chunks = 0.8f;                   // was 2.5 (fewer, larger)

// Ponds: beaver ponds and kettle ponds
pondRadiusRangeMeters    = new Vector2(8f, 20f);    // was (3.5, 6.5)
pondDepthMeters          = 1.5f;                     // was 0.75
pondDensityPer16x16Chunks = 2.0f;                   // was 4.0

// Dynamic expansion: larger lakes need larger expansion budget
lakeDynamicExpansionMeters = 12f;                   // was 8

// Render quality: more outline samples for larger bodies
lakeOutlineSampleCount   = 48;                      // was 40
```

---

## 7. Confidence Assessment

| Claim | Confidence | Basis |
|---|---|---|
| Current lake radius 8–14m, depth 1.5m | High | Inspector field values in `ProceduralVoxelTerrainWaterSystem.cs:44–50` |
| VoxelSizeMeters default = 1m, ChunkWorldSizeMeters = 16m | High | `ProceduralVoxelTerrain.cs:51,150` |
| 20×20 terrain = 320m × 320m | High | `chunkCounts * cellsPerChunkAxis * voxelSizeMeters` |
| PNW kettle/forested lake sizes 25–150m radius | High | Real-world limnology (Olympic Peninsula, Cascades); well-documented |
| Shore zone paint doesn't exist beyond waterline | High | `WaterTerrainCarver.cs` — no external radius painting confirmed |
| Perlin-displacement approach is self-contained | Medium | Would require tracing all callers of `CarveLake` for the perturbedRadius |
| Riparian affinity architecture as described | Medium | Describes a new feature; exact API shape may need adjustment based on scatter system internals |
| Mesh complexity for 80m radius is manageable | Medium | Extrapolated from observed timings; exact triangle counts depend on terrain resolution |
| `WaterSimulationConfig` out-of-sync with density refactor | High | `WaterSimulationConfig.cs:30,36` still uses `lakeCount`/`pondCount` int fields |

---

## Footnotes

[^1]: `Assets\Scripts\World\VoxelWater\TerrainEditing\WaterTerrainCarver.cs:64–119` — `ApplyLakeCarveDensity`, multi-brush circular carve pattern

[^2]: `Assets\Scripts\World\VoxelWater\TerrainEditing\WaterTerrainCarver.cs` — `PaintFreshwaterBasinMaterials` method only paints the interior basin, confirmed by absence of outer-radius painting calls

[^3]: `Assets\Scripts\World\VoxelWater\Config\WaterSimulationConfig.cs:30–38` — `lakeCount` and `pondCount` int fields; preset at `ApplyCoastalRainforestPreset` (line 157) still assigns `lakeCount = 2`, `pondCount = 3`

[^4]: `Assets\Scripts\World\VoxelTerrain\Generation\VoxelTerrainGenerator.cs:39–43` — `GenerationMaterialIndices` struct confirming `basinSandIndex`, `basinGravelIndex`, `lakeMudIndex` are registered material types

[^5]: `Assets\Scripts\World\VoxelWater\Rendering\WaterMeshGenerator.cs:40` — `indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16`

[^6]: `Assets\Scripts\World\VoxelWater\Hydrology\LakeTriangleFilterJob.cs` — Unity Jobs-based triangle filter for parallel classification

[^7]: `Assets\Scripts\World\VoxelWater\Generation\FreshwaterGenerator.cs:827–848` — `GenerateFreshwaterBodies` — `maxAttempts = targetCount * 24`; placement area margin at `inlandSamplingMargin` fraction of bounds

[^8]: `Assets\Scripts\World\VoxelWater\Generation\FreshwaterGenerator.cs:460–471` — `CreateFreshwaterCandidateSeed` uses `effectiveRadius = radiusMeters * 0.96f` and `captureRadius = effectiveRadius + lakeDynamicExpansionMeters`
