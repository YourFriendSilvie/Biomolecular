# Olympic Peninsula — Subalpine Zone (Expanded)

Last updated: 2026-03-31T10:52:00Z

Executive summary

This expanded subalpine brief provides detailed ecology and process-level guidance for treeline, krummholz, and alpine transition zones on the Olympic Peninsula. The subalpine is snow-dominated, with strong wind and frost exposure, thin rocky soils, and vegetation adapted to short growing seasons (subalpine fir, mountain hemlock, krummholz mats, dwarf shrubs and sedge-meadows). Processes important for modeling include snow redistribution by wind, snowpack longevity, freeze–thaw soil disturbance, and persistent snowbeds that host late-season refugia.[^3][^2]

Vegetation & structural features
- Krummholz: wind-pruned, stunted tree forms near treeline; represent as low-height mesh clusters with dense branching and high deadwood fraction.
- Species: Abies lasiocarpa (subalpine fir) and Tsuga mertensiana dominate; at highest elevations, tree islands and krummholz patches persist in sheltered hollows.

Snow dynamics & periglacial processes
- Snow redistribution: model wind-based redistribution (ridge-to-leeward accumulation) to create persistent snow patches and late-melt refugia; these control soil moisture and seedling survival into the growing season.
- Freeze–thaw: near-surface ice lenses and frost heave produce microtopography; represent as coarse soil mixing and increased rock fraction in thin-soil class.

Soils
- Thin, rocky, coarse-textured soils with low water holding capacity; organic matter concentrated in shallow mats; decomposition rates low.

Modeling parameters (practical)
- TreelineElevationBase = 1200.0f; TreelineElevationVariance = 200.0f; // combine with aspect/snowPersistence
- KrummholzCoverageProbability = function(shelterIndex, exposure) -> higher in sheltered hollows
- SnowWindRedistributionScale = 0.03f; // fraction of snow moved per wind step in cellular simulation
- ThinSoilRockFraction = 0.6f; // 0..1

Rendering & gameplay
- Use lower-resolution low-poly shrubs + billboard clusters for krummholz patches to save draw calls; assign separate wind-sway animation curves for krummholz vs upright trees.

References
[^2]: Abies lasiocarpa — subalpine fir (Wikipedia) [https://en.wikipedia.org/wiki/Abies_lasiocarpa] (accessed 2026-03-31).
[^3]: Tsuga mertensiana — mountain hemlock (Wikipedia) [https://en.wikipedia.org/wiki/Tsuga_mertensiana] (accessed 2026-03-31).

---
