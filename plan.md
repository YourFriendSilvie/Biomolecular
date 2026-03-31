# Biomolecular project plan

## Project framing

**Biomolecular** is a single-player 3D survival, crafting, and automation game in Unity.

The player is an alien survivor on an alien world whose ecology and geology were shaped by an ancient human terraforming project. The game is set millennia after that intervention, so the world now feels alien first, but its biomes, vegetation, mineral logic, and resource chains are still grounded in Washington-derived ecological and geological patterns.

The long-term fantasy is not just “stay alive.” It is:

- survive by understanding matter
- transform biomass and minerals through chemistry-grounded processes
- industrialize those processes through automation
- build the infrastructure and components needed to leave the planet by starship

## Confirmed direction

- **Setting:** alien world using Washington-derived ecology and geology, with ancient human terraforming as the in-universe reason for that resemblance
- **Scope strategy:** preserve the full long-term vision, but tie every major system to a small, convincing first-playable version
- **Multiplayer stance:** single-player first; co-op is optional future work and must not distort the initial architecture
- **First-playable scope:** one biome and one body archetype first
- **Chemistry direction:** keep the current composition system as a core foundation rather than replacing it with a coarse abstract resource model
- **Realism target:** science-inspired and chemistry-rich, but still game-scaled and playable rather than fully simulationist

## Design pillars

### 1. Matter transformation is the core fantasy

The heart of the game is harvesting matter, understanding what it is made of, and converting it into more useful forms. Biomass and minerals should not feel like generic loot tiers. They should feel like materials with chemistry, mass, and processing value.

### 2. Scientific believability over perfect realism

Systems should take real chemistry, biology, and geology seriously, but they should be simplified where needed for pacing, readability, and scope. The game should feel believable to someone who cares about science without becoming a laboratory simulator.

### 3. Embodied survival

The alien body should matter. Movement, terrain interaction, metabolism, carrying efficiency, and harvesting feel should all reinforce that the player is a living organism adapting to a difficult environment, not a floating camera with a backpack.

### 4. Procedural wilderness with regional logic

Terrain, plants, water, and minerals should be generated with recognizable ecological structure. The goal is not Earth replication, but a plausible, coherent wilderness shaped by Washington-inspired environmental logic and alienized history.

### 5. Automation supports escape

The factory layer is not separate from the survival layer. It exists to deepen the material transformation fantasy and to support the starship endgame. Every major industrial chain should eventually ladder into propulsion, structure, life support, power, or navigation needs.

## Core player loop

### Early game

- explore the starting biome
- harvest biomass, water, and basic mineral resources
- stabilize food, energy, and basic material needs
- convert raw matter into useful intermediate materials
- craft the first tools and simple processing devices

### Mid game

- expand harvesting into more deliberate extraction and cultivation
- build machines for separation, extraction, storage, and transport
- establish repeatable processing chains for increasingly valuable compounds
- use automation to remove survival friction and increase throughput

### Late game

- refine advanced materials and fuels
- build industrial infrastructure at scale
- assemble starship subsystems and supporting logistics
- complete launch preparation and leave the planet

## Simulation model

### Body and metabolism

Survival should be modeled as a matter-and-energy problem, not just a hunger bar. The body archetypes should differ in meaningful ways such as intake rate, mobility, terrain handling, durability, carrying efficiency, and long-term energy economy.

For the first playable, only one body archetype needs full implementation. The other archetypes should stay in design/planning until the first one feels good.

### Chemistry and resource composition

The current composition system should remain a foundational mechanic.

That means:

- items carry **mass in grams**
- harvested materials preserve **composition data**
- stacks merge by compatible identity and compatible composition/resource sets
- extraction and crafting should transform the composition of matter, not just swap one item type for another without continuity

The game can still introduce player-facing shortcuts and categories for readability, but those should sit on top of the composition system rather than replacing it.

The intended direction is:

- chemistry-rich harvesting and crafting
- processing chains that operate on real material differences
- recipes and machines that care about what a material is made of
- progressive UI that can expose more chemistry depth as the player advances

### Ecology, seasons, and growth

Plants should not only differ cosmetically. Species, season, maturity, and plant part should change what is harvested and what chemistry is present.

Key ecological goals:

- seasonal composition changes
- flowering and fruiting windows
- biomass estimates grounded in generated geometry and species data
- future support for dynamic growth rather than purely static spawned plants

The current seasonal serviceberry work is a good first step, but the long-term direction should be a generic phenology and growth framework rather than species-specific one-offs.

### Energy

Energy progression should follow a believable ladder:

- raw biomass and food as low-density starting resources
- processed biofuels and improved thermal/chemical storage in mid game
- larger industrial power systems later, potentially including electrical, geothermal, and advanced chemical storage

The purpose is not strict textbook energy accounting. The purpose is to make matter conversion and industrial scaling feel grounded and meaningful.

## World and setting

### World basis

The planet is alien, but its resource logic is legible because of the ancient human terraforming project. Washington-derived geology, hydrology, climate cues, and plant communities act as the scientific backbone for worldbuilding, biome design, and resource placement.

This gives the project three strengths at once:

- grounded real-world research inputs
- freedom to stylize and alienize the results
- a lore reason for recognizable-but-not-identical ecosystems

### Biomes and resources

Biomes should be generated from coherent environmental rules instead of hand-placed decorative sets. Vegetation, water, surface rock, and deeper resources should all emerge from those rules.

The first playable only needs one biome, but that biome should already demonstrate:

- believable terrain structure
- a readable plant community
- meaningful water/resource constraints
- at least one compact harvesting-to-processing-to-automation chain

## First playable target

The first playable should stay deliberately narrow.

### Must include

- one biome
- one fully implemented body archetype
- first-person or third-person embodied exploration
- harvesting of plants, water, and simple mineral resources
- the current composition-aware inventory and item system
- a basic metabolism or survival pressure loop
- one small automation chain
- one early progression milestone that clearly points toward the ship objective

### Must not include yet

- co-op or networking constraints
- multiple biomes
- all body archetypes at once
- full endgame industry
- exhaustive chemistry coverage
- a full dynamic-growth forest simulation

## Technical implementation strategy

### Systems to preserve and build on

- the current composition-aware inventory system
- runtime effective composition snapshots on items
- stack merging by item identity plus compatible composition/resource sets
- `IHarvestable` as the common harvest contract
- season/calendar foundations
- geometry-driven biomass estimation

These systems already support the intended chemistry-heavy direction and should be refined, not discarded.

### Architecture direction to favor

The next technical steps should move toward clear separation between:

1. **plant state and species data**
2. **geometry generation**
3. **biomass and composition calculation**
4. **harvest and inventory output**

That separation is especially important if the project later adopts article-inspired dynamic plant growth. The current procedural tree and shrub work is a good vertical-slice base, but future growth systems should evolve from it into a more data-driven plant-state architecture rather than staying as tightly coupled generator components forever.

### Unity planning areas

- character controller and terrain-reactive locomotion
- camera/input structure for embodied exploration and later build mode
- procedural terrain and biome generation
- scalable plant rendering and spawning
- species and ecology data assets
- automation simulation architecture for machines, transport, storage, and power
- save/load for a persistent procedural world

## Production roadmap

### Phase A - Foundation and preproduction

- lock pillars, setting, realism level, and first-playable scope
- define the first biome and first body archetype
- keep the composition system as a core gameplay commitment
- document the technical architecture boundaries for harvesting, ecology, and automation

### Phase B - Vertical slice

- one biome
- one body archetype
- harvesting, survival pressure, and basic chemistry-aware crafting
- one seasonal/ecological showcase species or small plant set
- one compact automation chain
- one milestone that points toward long-term ship construction

### Phase C - Systems expansion

- add the remaining body archetypes
- expand the ecology model, plant species, and seasonal systems
- add mining, logistics, and broader energy systems
- generalize plant growth and phenology

### Phase D - Content and industrial depth

- more biomes
- more species and resources
- larger automation chains
- more specialized chemistry and manufacturing pathways

### Phase E - Endgame and polish

- starship assembly progression
- large-world optimization
- balancing, onboarding, and UX refinement

---

## Recent engineering progress (2026-03-30)

- Fixed main-thread Texture2D construction by deferring texture uploads and adding ApplyPendingCellMaterialDebugTextureIfNeeded.
- Implemented per-column debug texture and a debug-flat vertex color mode to verify material/visual parity.
- Reworked terrain normals to use full 3D central-difference gradient to correctly shade caves and overhangs.
- Replaced shader white-noise with triplanar FBM; exposed rock colors to SRP-compatible CBUFFER.
- Replaced blocking Parallel.For mesh builds with Task-based background work.
- Computed per-chunk neighbor LOD masks and passed transitionMask into the mesher; added scaffolding to detect boundary cells that require Transvoxel transition-cell handling. (Current job falls back to the regular 3D tri-table until 2D transition tables are integrated.)

## Immediate next tasks

- cliff-smooth-blending: done — per-vertex slopeWeight packed into UV.w and shader blending implemented.
- Dual-identity refactor: make mesher derive vertex colors directly from authoritative cellMaterialIndices (high priority).
- lod-stitching: in_progress — neighbor LOD mask computation implemented and passed to mesher; next: integrate 2D Transvoxel transition tables and validate stitching across LOD boundaries (Phase 9).
- Add shader debug visualizations (normals, N·L, albedo) to diagnose remaining lighting issues.
- Verify ApplyPendingCellMaterialDebugTextureIfNeeded is called on all main-thread commit paths.
- Run visual QA with Debug Flat Colors enabled and iterate until pixel-perfect match.


## Risks and scope controls

- detailed chemistry can become unreadable if UI and progression are not carefully staged
- procedural terrain, procedural flora, survival simulation, and automation together are a large technical load
- dynamic growth for whole forests is a major future feature and should not block the first playable
- Washington-derived research should guide the world, not trap the project in endless research overhead
- realism should serve gameplay, not dominate it

## Immediate planning priorities

1. Lock the first biome and the first body archetype. ✅ — Olympic Rainforest biome; one humanoid body archetype
2. Define how much chemistry detail is visible to the player in the early game versus later progression.
3. Design the first harvesting-to-processing-to-automation chain around the current composition system. ✅ — Biomass → fermentation vessel → biofuel
4. Define the first-playable survival pressure loop. ✅ — Caloric intake + water hydration (water as a real consumed molecule)
5. Decide the first build-mode / factory-planning UX target.
6. Generalize the seasonal plant architecture so future fruiting species do not require bespoke harvest scripts every time.
7. Keep plant generation modular enough that dynamic growth can be layered in later.

## First playable build plan

### Decisions locked

- **Survival pressure:** caloric intake + hydration. Water is a real consumed molecule tracked through the composition system.
- **Automation chain:** Biomass → fermentation vessel → biofuel (liquid fuel for crafting/machines).
- **Milestone:** Synthesize a propellant precursor chemical (first step toward rocket fuel / ship construction).
- **Character controller:** Custom modular controller, third-person. Designed for swappable body archetypes.
- **Save/load:** In scope for first playable.

### Work phases

#### Phase B1 — Character & locomotion
- Custom modular character controller (third-person)
- Terrain-reactive: walk/run/jump/crouch, slope handling, water wading
- Body component system: plug in metabolism, carrying capacity, limb data per archetype
- Camera: third-person orbit, smooth terrain follow

#### Phase B2 — Metabolism & survival pressure
- `BodyMetabolism` component: caloric reserve, hydration reserve
- Both tracked as mass (grams), not arbitrary bars
- Caloric drain rate based on activity (rest/walk/run)
- Hydration drain rate (constant + activity multiplier)
- UI: simple HUD showing caloric and hydration state
- Death/collapse state at depletion

#### Phase B3 — Crafting system
- Crafting table prefab (placeable world object)
- `CraftingRecipe` ScriptableObject: input compositions → output composition
- Composition-aware matching: recipes check what molecules are present, not just item names
- UI: inspect inventory, drag to crafting table, see output preview

#### Phase B4 — Automation chain (fermentation)
- `FermentationVessel` machine: accepts biomass, water; produces biofuel over time
- Machine component: input slot, output slot, progress timer, fuel requirement
- Biofuel as a new CompositionInfo asset (ethanol + water + trace)
- Simple machine placement UI

#### Phase B5 — Progression milestone
- Biofuel → chemical reduction recipe → propellant precursor compound
- In-world log/journal entry unlocked on first synthesis
- Simple objective tracker HUD element ("Ship Log: Stage 1 complete")

#### Phase B6 — Save/load
- `WorldSaveManager`: serialize terrain seed + edits, inventory, placed machines, player position/health
- JSON or binary save file in `Application.persistentDataPath`
- Auto-save on scene exit; manual save from pause menu

#### Phase B7 — UI polish
- Main HUD: health/energy bars, interaction prompt, objective tracker
- Pause menu: save, load, quit
- Inventory UI: already exists — wire to new systems

## Notes

- Favor **composition-aware crafting** over replacing materials with broad abstract loot categories.
- Keep the first playable intentionally narrow and finishable.
- Use the alien terraforming backstory to justify Washington-derived ecology without losing the planet's own identity.
- Preserve science flavor and material continuity, but simplify aggressively when realism stops helping the game.
