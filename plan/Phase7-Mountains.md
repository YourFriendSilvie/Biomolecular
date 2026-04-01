# Phase 7: Advanced Mountain Sculpting

**Status:** PENDING
**Objective:** Implement non-linear noise transformations and gradient-aware dampening to simulate geological erosion and sharp structural ridges without the performance cost of a full hydraulic simulation.
**Description** Phase 7 is where we move past basic noise and start using the advanced "Mathematical Chisels" we learned from Josh and Sebastian Lague.

### Subphase 7A: The Gradient Trick (Simulated Erosion)

- **Algorithmic Goal:** Mimic the effect of water smoothing out fine sediment on steep slopes while keeping flat valley floors detailed.
- **Implementation:**
  - Utilize the **Halo Data** (N+3) in the `TerrainGenerationOperation` to calculate the **Gradient Magnitude** (steepness) for every voxel cell.
  - Apply a **Dampening Function** to the high-frequency octaves of the surface noise based on this gradient.
  - **The Math:** If `gradient > threshold`, multiply the amplitude of the detail noise layers by `(1.0 - saturate(gradient))`.
  - **The Result:** Sharp, clean cliff faces that look "swept" by gravity, contrasting with highly detailed, rocky valley floors.

### Subphase 7B: Domain Warping (Tectonic Folding)

- **Algorithmic Goal:** Break the "grid-like" look of standard Perlin noise to create organic, swirling rock formations that look like folded tectonic plates.
- **Implementation:**
  - Update `TerrainGenerationMath.cs` to implement **Recursive Domain Warping**.
  - **The Logic:** Offset the XZ sampling coordinates of the primary noise function by the output of a secondary, low-frequency noise function.
  - **The Result:** Mountain ranges that "twist" and "meander" organically, perfectly mimicking the subduction-zone geology of the Olympic Peninsula.

### Subphase 7C: Ridged Multifractal Peaks

- **Algorithmic Goal:** Generate the iconic "knife-edge" ridges found in the High Olympics (e.g., Mt. Olympus, The Brothers).
- **Implementation:**
  - Integrate a **Ridged Multifractal** noise layer into the `EvaluateSurfaceHeight` function.
  - **The Math:** Use the `1.0 - abs(noise)` transformation to turn "rounded hills" into "sharp peaks".
  - **The Blend:** Use an **Elevation Gate** so these sharp ridges only appear above the "Tree Line" (approx. 1800m), ensuring the rainforest valleys remain soft and traversable.

### Subphase 7D: Slope-Aware Normal Sharpening

- **Algorithmic Goal:** Enhance the visual "ruggedness" of the rock faces in the triplanar shader.
- **Implementation:**
  - Modify `VoxelTerrainLit.shader` to increase the **Normal Map Contribution** based on the `slopeFactor`.
  - **The Result:** Vertical basalt cliffs will show deep, shadowy cracks and textures, while flat mossy areas remain soft and smooth underfoot.

---

## Acceptance Criteria

- Mountain ridges and valleys appear more natural than previous basic noise.
- Performance target: no more than 10% longer generation for the same chunk volume.
- Ridged peaks only appear above the configured treeline elevation (default ~1,800 m).

## Files

- `Assets/Scripts/World/VoxelTerrain/Generation/TerrainGenerationMath.cs`
- `Assets/Scripts/World/VoxelTerrain/Generation/TerrainGenerationOperation.cs`
- `Assets/Shader/VoxelTerrainLit.shader`