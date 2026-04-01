# Biomolecular — Terrain Engineering Master Plan

This document tracks the completed and planned phases of the terrain engineering roadmap and records the architecture decisions that govern the system. For the overall game design plan (pillars, player loop, first-playable scope), see `plan.md`.

---

## Architecture Decisions

### Vertex Layout

Material IDs are stored as small integers packed as floats into mesh UV channels. Per-vertex layout:

| UV channel | Contents |
|---|---|
| `uv0.x` | Primary material ID |
| `uv0.y` | Secondary material ID |
| `uv0.z` | Blend weight (0 = all primary, 1 = all secondary) |
| `vertex.color.a` | Slope factor — drives rock-face blend in the shader |

### Chunk Seam Handling

Each chunk's density/material data includes an `(N+3)` halo — 1.5 voxels of overlap on all sides — so the mesher always reads consistent data across chunk boundaries without cross-chunk queries at mesh time.

A 6-bit `TransitionMask` per chunk indicates which of the six faces border a lower-LOD neighbor, triggering Transvoxel transition-cell geometry on those faces.

### Material System

Slope blending is done per-vertex in the mesher (not per-voxel) by computing a `slopeWeight` from the surface normal and packing it into `vertex.color.a`. The shader reads this value to blend toward the rock texture on steep faces. This avoids expensive per-voxel blend texture lookups and is fully GPU-side.

Texture2DArray slices for material variation are baked by an Editor utility (`Assets/Editor/`) to avoid runtime `ReadPixels` costs. If the array is absent, the material falls back to a 1×1 tint texture to prevent solid-black output.

---

## Global Acceptance Criteria

These must hold across all terrain phases before any phase is considered verified:

- No visible cracks between chunks when neighboring LODs differ (visual test: high-detail chunk adjacent to low-detail chunk along one face).
- No lighting seams at chunk borders in typical daylight scenes (central-difference normals used, halo present).
- Cliffs appear naturally weathered (slope-blended shader) rather than harshly faceted in test scenes.
- Lakes show depth-aware materials at shoreline vs. deep center in test scenes (gravel → sand → mud transitions).
- Shader uses `Texture2DArray` slices; if the array is absent, the 1×1 tint fallback prevents solid-black output.
- All Burst jobs use `Unity.Mathematics` functions only — `Mathf.*` is forbidden in Burst context.

---

## Phase Summary

### Phases 1–4: The Transvoxel Core ✅ Verified

Solidifies the Burst-compiled meshing foundation and the "single source of truth" density pipeline. Establishes the marching-cubes loop, the density evaluation contract, and the voxel-to-world coordinate mapping.

### Phase 5: The Halo & Material Tally ✅ Verified

Implements the `(N+3)` halo data expansion to fix chunk seams. Packs Primary/Secondary material IDs and blend weight into `uv0` for the terrain lit shader.

### Phase 6: Olympic Biome & Geology Map 🔲 Pending

Biome tensor (Elevation × Moisture × Rainshadow), rainshadow gradient, coastal stratigraphy, slope-aware geology overrides, and GPU material splatting. See [Phase6-Biomes.md](plan/Phase6-Biomes.md).

### Phase 7: Advanced Mountain Sculpting 🔲 Pending

Gradient dampening, domain warping, ridged multifractal peaks above treeline, and slope-aware normal sharpening in the shader. See [Phase7-Mountains.md](plan/Phase7-Mountains.md).

### Phase 8: Glacial & Rainforest Hydrology 🔲 Pending

Biome-specific basin carving (alpine cirque vs. rainforest pond), depth-aware sediment splatting, saturated marsh zones, and the emerald-silt shader pass. See [Phase8-Hydrology.md](plan/Phase8-Hydrology.md).

### Phase 9: Transvoxel LOD Transition Cells 🔲 Pending

LOD neighbor detection, 2D Transvoxel transition table meshing, material/normal continuity at LOD seams, and dynamic LOD assignment with hysteresis. See [Phase9-LOD-Transitions.md](plan/Phase9-LOD-Transitions.md).

### Phase 10: Performance & Visibility 🔲 Pending

Spiral chunk load queue, horizon occlusion culling, and frame budget tuning. See [Phase10-Performance.md](plan/Phase10-Performance.md).

### Phase 11: Atmospheric & Environmental Persistence 🔲 Pending

Global wetness map, procedural snow accumulation, and volumetric fog driven by the biome map. See [Phase11-Environment.md](plan/Phase11-Environment.md).

### Phase 12: Biotic Integration (Flora) 🔲 Pending

Six-zone ecological flora simulation: biome-driven placement, species profiles, procedural tree skeleton builder, mesh/skinning pipeline, epiphyte attachment, LOD/GPU instancing, and coastal dynamics. See [Phase12-Flora.md](plan/Phase12-Flora.md).

---

## Research Files

Background research that informed design decisions:

- [Hoh Rainforest](.github/research/hoh-rainforest.md) — flora, geology, soils, hydrology, climate
- [Coastal Forest](.github/research/coastal-forest.md) — geomorphology, botany, pedology, hydrology
- [Lowland Forest](.github/research/lowland-forest.md) — valley-bottom ecosystems, soils, hydrology
- [Montane Forest](.github/research/montane-forest.md) — silver fir zone, snowpack, species ecophysiology
- [Subalpine Zone](.github/research/subalpine.md) — treeline, krummholz, snow dynamics
- [Alpine & Glacial](.github/research/alpine-glacial.md) — cirque geology, glaciation, tundra botany
- [Coasts](.github/research/coasts.md) — Olympic western/northern coast, Puget Sound, intertidal
- [Rain-Shadow Forest](.github/research/rain-shadow-forest.md) — dry northeast, Garry oak savannah

Supplementary analysis documents in the repo root:

- [research-terrain-accuracy-soil-and-lake-geology.md](research-terrain-accuracy-soil-and-lake-geology.md) — scientific accuracy scorecard for the soil horizon stack and lake basin sediment model
- [research-lakes-and-ponds-realism.md](research-lakes-and-ponds-realism.md) — real-world lake scale reference data, current system analysis, and recommendations for size/depth overhaul



