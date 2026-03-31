Acceptance criteria

- No visible cracks between chunks when neighboring LODs differ (visual test case: high-detail chunk adjacent to low-detail chunk along one face).
- No lighting seams at chunk borders in typical daylight scene (central-difference normals used, halo present).
- Cliffs appear naturally weathered (slope-blended shader) not harshly faceted in test scenes.
- Lakes show depth-aware materials at shoreline vs deep center in test scenes (gravel→sand→mud transitions).
- Shader uses Texture2DArray slices; if the array isn't present, a 1×1 tint fallback prevents solid-black output.

Design decisions & reasoning

- Keep material IDs small ints and pack them directly into uv0.x/uv0.y as floats. Blend weight stored in uv0.z.
- Do slope blending in shader using a per-vertex slopeWeight derived from the normal and/or slope metric computed in the mesher. This avoids complex per-voxel blend painting and is performant on GPU.
- Use uint6 (6-bit mask or 6-boolean flags) per chunk to indicate which faces require transition stitching.
- Implement Texture2DArray baking as an Editor utility to avoid runtime ReadPixels cost and unreadable textures.

## Plan files
- [Phase 6: Olympic Biome & Geology Map](plan/Phase6-Biomes.md)
- [Phase 7: Advanced Mountain Sculpting](plan/Phase7-Mountains.md)
- [Phase 8: Glacial & Rainforest Hydrology](plan/Phase8-Hydrology.md)
- [Phase 9: Transvoxel LOD Transition Cells](plan/Phase9-LOD-Transitions.md)
- [Phase 10: Performance & Visibility](plan/Phase10-Performance.md)
- [Phase 11: Atmospheric & Environmental Persistence](plan/Phase11-Environment.md)
- [Phase 12: Biotic Integration (Flora)](plan/Phase12-Flora.md)

Phase 1-4: The Transvoxel Core (Verified)
Solidifies the Burst-compiled meshing foundation and the "Single Source of Truth" logic.

Phase 5: The Halo & Material Tally (Verified)
Implements the (N+3) data expansion to fix chunk seams and packs Primary/Secondary IDs into uv0 for the HD Triplanar shader.








