
# Phase 9: Transvoxel LOD Transition Cells

**Status:** PENDING
**Objective:** Implement transition cells and LOD logic to ensure seamless mesh stitching between chunks of different detail levels, eliminating cracks and T-junctions.

---

## Subphase 9A: LOD Neighbor Analysis
**Goal:** Detect chunk faces that border lower-resolution neighbors and generate a transition mask.

**Acceptance Criteria:**
- Each chunk correctly identifies which faces require transition geometry.
- 6-bit `TransitionMask` is generated and unit-tested for all face combinations.

**Tasks:**
- Implement neighbor LOD check in `TerrainGenerationOperation.StepChunkObjects()`.
- Generate and document the 6-bit `TransitionMask`.
- Pass mask to `TransvoxelMesherJob`.
- Add unit tests for mask generation.

**Files:**
- Assets/Scripts/World/VoxelTerrain/Core/TerrainGenerationOperation.cs
- Assets/Tests/TransitionMaskTests.cs

---

## Subphase 9B: Transition Cell Meshing
**Goal:** Generate correct geometry for transition faces using Transvoxel tables.

**Acceptance Criteria:**
- Transition faces use 2D Transvoxel lookup tables instead of standard marching cubes.
- No visible cracks or T-junctions at LOD boundaries in test scenes.

**Tasks:**
- In `TransvoxelMesherJob.Execute()`, branch to transition cell logic for marked faces.
- Integrate 2D Transvoxel transition tables.
- Add visual and unit tests for all face/edge/corner cases.

**Files:**
- Assets/Scripts/World/VoxelTerrain/Meshing/TransvoxelMesherJob.cs
- Assets/Scripts/World/VoxelTerrain/Meshing/TransvoxelTables.cs
- Assets/Tests/TransitionCellTests.cs

---

## Subphase 9C: Material & Normal Continuity
**Goal:** Ensure stitched geometry has correct material and smooth normals.

**Acceptance Criteria:**
- Transition cell vertices have correct primary/secondary material assignment.
- Normals are smooth and lighting is continuous across LOD seams.
- Tests confirm no visible shading or material discontinuities.

**Tasks:**
- Apply material tally logic to transition cell vertices.
- Use halo data or neighbor sampling for normal smoothing.
- Add visual and unit tests for continuity.

**Files:**
- Assets/Scripts/World/VoxelTerrain/Meshing/TransvoxelMesherJob.cs
- Assets/Scripts/World/VoxelTerrain/Materials/BiomeMaterialProfiles.cs
- Assets/Tests/TransitionMaterialTests.cs

---

## Subphase 9D: Dynamic LOD Assignment
**Goal:** Assign LODs to chunks based on player distance, with stable transitions.

**Acceptance Criteria:**
- LODs update smoothly as player moves, with no flicker or popping.
- Buffer zone (hysteresis) prevents rapid LOD switching.
- Unit tests verify correct LOD assignment and stability.

**Tasks:**
- Implement radial LOD system in `ProceduralVoxelTerrain.cs`.
- Add buffer zone logic for LOD switching.
- Add unit tests for LOD assignment and hysteresis.

**Files:**
- Assets/Scripts/World/VoxelTerrain/ProceduralVoxelTerrain.cs
- Assets/Tests/LODTransitionTests.cs

---

## References
- Eric Lengyel, "Transvoxel Algorithm" (https://transvoxel.org/)
