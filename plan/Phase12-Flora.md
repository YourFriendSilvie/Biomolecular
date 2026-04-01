
# Phase 12: Biotic Integration (Flora)

**Status:** PENDING
**Objective:** Implement a 6-zone ecological flora simulation, including biome-driven placement, procedural tree generation, biomass, micro-flora, and LOD/instancing.

---


## Phase 12A: Biome-Driven Flora Placement
**Goal:** Distribute flora according to biome, elevation, and moisture rules.

**Acceptance Criteria:**
- `SpatialPlacementSolver` assigns correct biome for all 36 elevation × moisture test cases.
- Unit test coverage for all biome transitions and edge cases.

**Tasks:**
- Implement `SpatialPlacementSolver` with 2D lookup table (Elevation × Moisture → BiomeID).
- Encode zone constraints for Coastal, Lowland, Rainforest, Montane, Subalpine, Alpine.
- Add unit tests in `FloraPlacementTests.cs` for all biome/elevation/moisture combinations.

**Files:**
- Assets/Scripts/World/VoxelTerrain/Biomes/SpatialPlacementSolver.cs
- Assets/Tests/FloraPlacementTests.cs

---


## Phase 12B: Species Profile Definition
**Goal:** Define and test all species’ morphological rules and branching logic.

**Acceptance Criteria:**
- Each biome assigns correct `TreeGrowthCommand` arrays for its species.
- Unit tests in `SpeciesProfileTests.cs` verify all profile rules.

**Tasks:**
- Implement species profiles in `TreeGrowthCommand.cs`.
- Document rules for monopodial, sympodial, and plagiotropic branching.
- Add unit tests for profile assignment.

**Files:**
- Assets/Scripts/World/VoxelTerrain/Flora/TreeGrowthCommand.cs
- Assets/Tests/SpeciesProfileTests.cs

---

## Phase 12C: Skeleton Builder
**Goal:** Build tree skeletons using Burst jobs and species profiles.

**Acceptance Criteria:**
- `GenerateTreeSkeletonJob` outputs correct skeletons for all species.
- NativeList<TreeLineSegment> structure is validated by unit tests.

**Tasks:**
- Implement `GenerateTreeSkeletonJob` in `TreeGenerator.cs`.
- Integrate with species profiles.
- Add unit tests for skeleton output in `SkeletonBuilderTests.cs`.

**Files:**
- Assets/Scripts/World/VoxelTerrain/Flora/TreeGenerator.cs
- Assets/Tests/SkeletonBuilderTests.cs

---

## Phase 12D: Mesh & Skinning Pipeline
**Goal:** Convert skeletons to mesh, including foliage and UV mapping.

**Acceptance Criteria:**
- Cylindrical skinning and foliage quads are generated for all segments.
- UV mapping is correct for bark and leaves (verified by `TreeMeshTests.cs`).

**Tasks:**
- Implement mesh generation from skeleton in `TreeGenerator.cs`.
- Add foliage quad and UV logic.
- Add unit/visual tests for mesh output.

**Files:**
- Assets/Scripts/World/VoxelTerrain/Flora/TreeGenerator.cs
- Assets/Tests/TreeMeshTests.cs

---

## Phase 12E: Epiphyte & Moss Attachment
**Goal:** Attach moss/epiphytes to trees in rainforest biomes.

**Acceptance Criteria:**
- Moss is attached to >95% of eligible horizontal branches in the Hoh biome (visual test scene).
- Visual/unit tests confirm correct placement in `EpiphyteTests.cs`.

**Tasks:**
- Implement epiphyte pass in `TreeGenerator.cs`.
- Add logic for horizontal branch detection and moss ribbon placement.
- Add visual/unit tests for moss.

**Files:**
- Assets/Scripts/World/VoxelTerrain/Flora/TreeGenerator.cs
- Assets/Tests/EpiphyteTests.cs

---


## Phase 12F: Species-Specific Algorithms
**Goal:** Implement unique procedural algorithms for each tree species profile.

**Algorithm Descriptions:**
- **Monopodial Dominance (Douglas Fir, Sitka Spruce):**
  - Standard L-System with high apical dominance.
  - Primary axis (trunk) retains 100% vertical vector.
  - Lateral branches apply a downward pitch bias (gravitropism) of 5°–15° per iteration.
  - Branches spawn at 90°–105° angles and decay rapidly in length.
- **Space Colonization (Bigleaf Maple, Red Alder):**
  - Resource-seeking branch generation.
  - Generates a bounding volume of 'Light Points'.
  - Nodes calculate the average vector to nearby points, resulting in sympodial bifurcation (trunk splitting) and sprawling, chaotic canopies.
- **Plagiotropic Planar Branching (Western Red Cedar):**
  - Constrained L-System.
  - Tertiary branches are strictly clamped to the localized cross-product plane of their parent branch.
  - Instead of full cylindrical skinning, terminal nodes generate flat 'Cross-Quads' mapped with dense, sweeping frond textures to mimic cedar foliage.

**Acceptance Criteria:**
- Each algorithm produces correct tree forms for its species (visual and unit tests).
- Tests confirm correct branching and meshing in `SpeciesAlgorithmTests.cs`.

**Tasks:**
- Implement algorithm selection in `TreeGenerator.cs`.
- Add constraints and meshing for each profile as described above.
- Add unit/visual tests for each species.

**Files:**
- Assets/Scripts/World/VoxelTerrain/Flora/TreeGenerator.cs
- Assets/Tests/SpeciesAlgorithmTests.cs

---

## Phase 12G: Biomass Estimation & Harvesting
**Goal:** Calculate and apply biomass/harvest logic for trees.

**Acceptance Criteria:**
- Volume and mass are calculated for all tree segments.
- Harvested trees break into physics-enabled logs with correct mass (tested in `BiomassTests.cs`).

**Tasks:**
- Implement volume calculation (truncated cone) in `TreeGenerator.cs`.
- Apply species density factors.
- Add harvest logic and physics integration.
- Add unit/physics tests for biomass and harvesting.

**Files:**
- Assets/Scripts/World/VoxelTerrain/Flora/TreeGenerator.cs
- Assets/Tests/BiomassTests.cs

---

## Phase 12H: Micro-Flora & Soil Integration
**Goal:** Integrate moss, lichen, and soil visuals into terrain.

**Acceptance Criteria:**
- Moss/lichen appear on correct surfaces in the Hoh biome (visual test scene).
- Soil color/texture matches biome and flora (tested in `MicroFloraTests.cs`).

**Tasks:**
- Update `VoxelTerrainLit.shader` for moss/lichen blending.
- Sync soil horizon with biome/placement data.
- Add visual/unit tests for micro-flora and soil.

**Files:**
- Assets/Shader/VoxelTerrainLit.shader
- Assets/Scripts/World/VoxelScatter/Placement/SpatialPlacementSolver.cs
- Assets/Tests/MicroFloraTests.cs

---

## Phase 12I: LOD & Instancing for Flora
**Goal:** Implement LOD transitions and GPU instancing for trees and understory.

**Acceptance Criteria:**
- Trees and ferns use 3+ LODs (full mesh, billboard, impostor) with smooth transitions.
- GPU instancing is used for dense groundcover (ferns, moss) and distant trees.
- Visual tests confirm no popping or performance issues in `FloraLODTests.cs`.

**Tasks:**
- Implement LOD logic for trees and ferns in `TreeGenerator.cs` and `UnderstoryGenerator.cs`.
- Use `Graphics.DrawMeshInstancedIndirect` for groundcover.
- Add cross-fade/dither transitions for LODs.
- Add visual/performance tests for LOD and instancing.

**Files:**
- Assets/Scripts/World/VoxelTerrain/Flora/TreeGenerator.cs
- Assets/Scripts/World/VoxelTerrain/Flora/UnderstoryGenerator.cs
- Assets/Tests/FloraLODTests.cs

---

## Phase 12J: Coastal Forest Dynamics & Understory Instancing
**Goal:** Simulate coastal stressors and render dense understory efficiently.

**Acceptance Criteria:**
- Wind-flagged trees and driftwood appear in coastal biomes.
- Ferns/understory are GPU-instanced with correct placement.
- Visual/unit tests confirm correct coastal/understory features in `CoastalUnderstoryTests.cs`.

**Tasks:**
- Inject global wind vector into tree generation for coastal biomes.
- Implement driftwood scatter pass for beaches.
- Render ferns using `Graphics.DrawMeshInstancedIndirect`.
- Add visual/unit tests for coastal/understory features.

**Files:**
- Assets/Scripts/World/VoxelTerrain/Flora/TreeGenerator.cs
- Assets/Scripts/World/VoxelTerrain/Flora/UnderstoryGenerator.cs
- Assets/Tests/CoastalUnderstoryTests.cs

---

## References
- https://tympanus.net/codrops/2025/01/27/fractals-to-forests-creating-realistic-3d-trees-with-three-js/
- https://caner-milko.github.io/posts/procedural-tree-generation/
- https://github.com/caner-milko/TreeGen
