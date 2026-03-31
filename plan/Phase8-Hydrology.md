# Phase 8: Glacial & Rainforest Hydrology

**Status:** PENDING
**Objective:** Upgrade water system to support biome-specific shoreline shapes and depth-aware sediment distribution.

## Acceptance criteria
- Lake basins follow alpine vs rainforest style.
- depth-aware material splatting is validated in unit tests (0-1-2.5m bands).
- halo seams at water edges are eliminated.

## Tasks
- Implement `WaterTerrainCarver` modes for alpine and rainforest.
- Integrate vertical lake sediment layering into `DetermineCellMaterialIndex`.
- Add saturated marsh zone override in `BuildColumnMaterialProfile`.
- Add water shader depth gradient pass.

## Files
- `Assets/Scripts/World/VoxelTerrain/Water/WaterTerrainCarver.cs`
- `Assets/Scripts/World/VoxelTerrain/Materials/WaterMaterialUtility.cs`
- `Assets/Scripts/World/VoxelTerrain/Meshing/VoxelTerrainLit.shader`

## Phase 8: Glacial & Rainforest Hydrology

**Status**: PENDING
**Objective**: Upgrade the current water system to support biome-specific basin shapes, organic shoreline silhouettes, and depth-aware sediment distribution that reflects the distinct character of Olympic alpine and rainforest lakes.

### Subphase 8A: Biome-Specific Basin Carving

- **Algorithmic Goal**: Create distinct "Geological Fingerprints" for lakes based on their elevation and biome.
- **Implementation**:
  - **Alpine Cirques**: Use the **Quartic Bowl** formula in `WaterTerrainCarver.cs` to create deep, smooth, U-shaped basins typical of glacial erosion.
  - **Rainforest Ponds**: Implement **Fractal Rim Noise** to perturb the radius of the basin, creating "jagged," irregular shorelines that mimic overhanging moss and fallen logs.
  - **Data Flow**: Ensure the `WaterTerrainCarver` updates the $(N+3)$ Halo in the density field to prevent mesh seams at the water's edge.

### Subphase 8B: Depth-Aware Sediment Splatting

- **Algorithmic Goal**: Automatically assign material IDs based on the water's depth to simulate natural silting processes.
- **Implementation**:
  - Update `DetermineCellMaterialIndex` to use the `oceanWaterDepth` and `lakeSurfaceY` values as a vertical material mask.
  - **Material Stack**:
    - **0m to 1m**: `BasinGravelIndex` (Shoreline wash).
    - **1m to 2.5m**: `BasinSandIndex` (Shallow shelf).
    - **2.5m+**: `LakeMudIndex` or `ClayDepositIndex` (Deep forest silt).

### Subphase 8C: Saturated "Marsh" Zones

- **Algorithmic Goal**: Simulate the high moisture content of soil surrounding rainforest water bodies.
- **Implementation**:
  - In `BuildColumnMaterialProfile`, calculate a **Saturation Factor** based on the distance to the nearest `RegisteredLakeBasin`.
  - **The Logic**: If a column is within a specific "Splash Zone" radius, override the standard `OrganicLayerIndex` with a "Saturated Muck" or "Peat" material ID.
  - **The Result**: Dark, muddy earth surrounding Hoh lakes that transitions naturally into the lush forest floor.

### Subphase 8D: Emerald Silt Shader Pass

- **Algorithmic Goal**: Match the "Glacial Flour" emerald tint seen in Olympic waters.
- **Implementation**:
  - Modify the water shader in `WaterMaterialUtility.cs` to implement a **Depth-Based Color Gradient**.
  - **The Palette**: Shift from clear teal in the shallows to a dense, milky emerald-green in the deeps to simulate suspended silicates.