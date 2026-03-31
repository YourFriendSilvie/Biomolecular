# Phase 10: Performance & Visibility

**Status:** PENDING
**Objective:** Add dynamic chunk loading and culling for large-scale Olympics map.

## Acceptance criteria
- prioritized chunk generation around player.
- chunk scene built with < 16 static draw calls per 100 chunks.
- occluded chunks removed before rendering.

## Tasks
- Implement spiral load queue in `ProceduralVoxelTerrain`.
- Add horizon occlusion pass or CullingGroup integration.
- Evaluate and tune chunk update budget per frame.

## Files
- `Assets/Scripts/World/VoxelTerrain/ProceduralVoxelTerrain.cs`
- `Assets/Scripts/World/VoxelTerrain/Visibility/ChunkCuller.cs` (new) 

## Phase 10: Performance & Visibility

**Status**: PENDING
**Objective**: Implement dynamic chunk loading and culling to handle the massive scale of the Olympic Peninsula without overwhelming the CPU/GPU.

### Subphase 10A: Spiral Chunk Loading

- **Algorithmic Goal**: Prioritize generating terrain immediately around the player before expanding outward.
- **Implementation**: Implement an asynchronous priority queue in `ProceduralVoxelTerrain` that evaluates chunk distances and processes generation requests in a radial, outward-expanding spiral.

### Subphase 10B: Horizon Culling

- **Algorithmic Goal**: Stop rendering chunks that are completely hidden behind the jagged Olympic mountains.
- **Implementation**: Integrate a software occlusion culling pass or leverage Unity's advanced culling groups to disable `MeshRenderers` for chunks that are occluded by higher-elevation terrain in the foreground.