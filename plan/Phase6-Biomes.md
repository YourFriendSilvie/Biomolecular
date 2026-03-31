
# Phase 6: Olympic Biome & Geology System

**Status:** PENDING
**Objective:** Implement a biome system using a 3D tensor (Elevation × Moisture × Rainshadow) to assign biome IDs, drive material and flora, and support realistic geology for the Olympic Peninsula.

---

## Subphase 6A: Biome Tensor Core
**Goal:** Implement the core biome lookup and assignment system.

**Acceptance Criteria:**
- Biome IDs are assigned for all test map samples using a 3D tensor lookup.
- Unit tests verify correct biome assignment for edge cases.

**Tasks:**
- Implement `BiomeId Lookup(float elevation, float moisture, float rainshadow)` in `TerrainGenerationOperation`.
- Create and document the 3D tensor data structure in `BiomeMapper`.
- Add unit tests in `BiomeMapperTests`.

**Files:**
- Assets/Scripts/World/VoxelTerrain/TerrainGenerationOperation.cs
- Assets/Scripts/World/VoxelTerrain/Biomes/BiomeMapper.cs
- Assets/Tests/BiomeMapperTests.cs

---

## Subphase 6B: Rainshadow System
**Goal:** Integrate rainshadow gradient and penalty into biome assignment.

**Acceptance Criteria:**
- Rainshadow gradient map is generated and visualized.
- Biome assignment reflects lower moisture on lee sides of mountains.
- Unit tests confirm rainshadow effect.

**Tasks:**
- Implement `RainshadowMapGenerator` in `Biomes/RainshadowMapGenerator.cs`.
- Integrate rainshadow penalty into `BiomeId Lookup`.
- Add visualization/debug output for rainshadow gradient.
- Add unit tests for rainshadow mapping.

**Files:**
- Assets/Scripts/World/VoxelTerrain/Biomes/RainshadowMapGenerator.cs
- Assets/Scripts/World/VoxelTerrain/Biomes/BiomeMapper.cs
- Assets/Tests/RainshadowMapTests.cs

---

## Subphase 6C: Coastal Stratigraphy & Flora
**Goal:** Assign unique geology and flora to each coastline type.

**Acceptance Criteria:**
- Each coast (Western, Northern, Eastern) has distinct geology and flora in generated maps.
- Unit tests verify correct biome and material assignment for coastal regions.

**Tasks:**
- Implement coastline type detection in `BiomeMapper`.
- Assign geology/material profiles in `BiomeMaterialProfiles.cs`.
- Assign flora profiles in `BiomeMaterialProfiles.cs`.
- Add unit tests for coastline biome assignment.

**Files:**
- Assets/Scripts/World/VoxelTerrain/Biomes/BiomeMapper.cs
- Assets/Scripts/World/VoxelTerrain/Materials/BiomeMaterialProfiles.cs
- Assets/Tests/CoastalBiomeTests.cs

---

## Subphase 6D: Slope-Aware Geological Overrides
**Goal:** Override soil/vegetation based on terrain steepness.

**Acceptance Criteria:**
- Steep slopes (>45°) use weathered rock material.
- Very steep slopes (>75°) expose bedrock.
- Unit tests verify correct overrides.

**Tasks:**
- Implement steepness mask in `DetermineCellMaterialIndex` (in TerrainGenerationOperation.cs).
- Add logic for material override based on slope.
- Add unit tests for slope overrides.

**Files:**
- Assets/Scripts/World/VoxelTerrain/TerrainGenerationOperation.cs
- Assets/Tests/SlopeOverrideTests.cs

---

## Subphase 6E: Material Splatting & GPU Integration
**Goal:** Integrate biome and slope logic into GPU material blending.

**Acceptance Criteria:**
- Burst mesher respects biome and coastal boundaries in material tally.
- `VoxelTerrainLit.shader` blends textures based on biome and slope.
- Visual tests confirm correct blending.

**Tasks:**
- Update Burst mesher to tally biome/coast/slope for each voxel.
- Update `VoxelTerrainLit.shader` to use blend weights for biome transitions.
- Add visual test scenes for material blending.

**Files:**
- Assets/Scripts/World/VoxelTerrain/Meshing/BurstMesher.cs
- Assets/Shaders/VoxelTerrainLit.shader
- Assets/Tests/MaterialBlendVisualTests.cs

---

## References
- .github/research/coasts.md
- .github/research/coastal-forest.md
- .github/research/lowland-forest.md
- .github/research/hoh-rainforest.md
- .github/research/rain-shadow-forest.md
- .github/research/montane-forest.md
- .github/research/subalpine.md
- .github/research/alpine-glacial.md
