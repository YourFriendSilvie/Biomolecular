# Phase 11: Atmospheric & Environmental Persistence

**Status:** PENDING
**Objective:** Implement a global environment controller to synchronize weather, day/night cycles, and dynamic material properties (wetness, snow accumulation) across the voxel terrain.

### Subphase 11A: Dynamic Surface Saturation (The Wetness Map)

- **Algorithmic Goal**: Update the terrain shader to reflect real-time rainfall and soil moisture levels.
- **Implementation**:
  - Integrate a **Global Wetness Uniform** in `VoxelTerrainLit.shader` that modifies the smoothness and albedo of the triplanar textures.
  - **The Logic**: Use the **Saturation Factor** from Phase 8C to determine which areas (like the Hoh) become "glossy" and dark during rain, while alpine rock remains sharp.
  - **The Result**: A world that visually "soaks up" water during a Pacific Northwest storm.

### Subphase 11B: Procedural Snow Accumulation

- **Algorithmic Goal**: Dynamically "paint" snow onto the High Olympics based on elevation and temperature.
- **Implementation**:
  - Add a **Snow Overlap Pass** to the triplanar shader that blends a "Snow" texture onto upward-facing normals (`normalWS.y > threshold`).
  - **The Mask**: Tie the snow visibility to a global `_SnowLevel` float. Any terrain above this local Y-coordinate begins to accumulate white "Pillow Snow".
  - **The Result**: Peaks that turn white in the winter and retreat to bare basalt in the summer.

### Subphase 11C: Volumetric Fog & Light Shafts (The "Misty" Hoh)

- **Algorithmic Goal**: Capture the iconic, heavy atmosphere of the rainforest valleys.
- **Implementation**:
  - Utilize the **Biome Map** from Phase 6 to drive a localized **Volumetric Fog Density**.
  - **The Result**: Thick, low-lying mist that hugs the Hoh River basins while the high peaks remain clear and sunny above the cloud line.

---

## Acceptance Criteria

- Interactive rain triggers wetness map changes visible in the terrain shader.
- Snow accumulates above the configured elevation threshold and fades on heating.
- Fog density follows biome and river basin assignments from Phase 6.

## Files

- `Assets/Shader/VoxelTerrainLit.shader`
- `Assets/Scripts/World/VoxelTerrain/Environment/WeatherController.cs` (new)
- `Assets/Scripts/World/VoxelTerrain/Environment/MistZoneManager.cs` (new)