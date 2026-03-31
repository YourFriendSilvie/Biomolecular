# Biomolecular — GitHub Copilot Instructions

## Project Overview

**Biomolecular** is a Unity 6 (6000.4.0f1) survival/exploration game set in a procedurally generated Olympic Peninsula-style rainforest. The codebase generates infinite voxel terrain, freshwater hydrology (lakes, ponds, rivers), biome scattering (trees, shrubs), and a resource/composition-based inventory system.

**Render pipeline:** Universal Render Pipeline (URP) 17.4.0  
**Key packages:** Unity Burst 1.8.28, Unity Jobs / Collections 6.4.0, Unity Mathematics 1.3.3, Input System 1.19.0, Cinemachine 3.1.2  
**All game code** lives in the default `Assembly-CSharp` assembly (no custom .asmdef files).

---

## Architecture Map

```
Assets/
├── Editor/              Custom Unity Editor inspector scripts
├── Scripts/
│   ├── Helper/          Pure static utility classes (math, mesh, materials)
│   ├── Inventory/       Item system, harvesting, composition, seasons
│   └── World/
│       ├── VoxelTerrain/     Terrain generation, materials, mesh, editing
│       ├── VoxelWater/       Freshwater hydrology, lake/pond/river generation
│       ├── VoxelScatter/     Biome object placement (trees, shrubs, props)
│       └── VoxelStartArea/   Safe starting zone generation
├── Shader/              VoxelTerrain.shader (custom URP lit shader)
└── Objects/ Scenes/ Settings/
```

---

## Major Systems & Facades

Each major system is a MonoBehaviour **facade** that delegates to internal subsystems. Always program to the facade; never call internal subsystems directly from outside code.

| Facade | File | Responsibility |
|--------|------|----------------|
| `ProceduralVoxelTerrain` | `VoxelTerrain/ProceduralVoxelTerrain.cs` | Voxel terrain generation, editing, chunk lifecycle |
| `ProceduralVoxelTerrainWaterSystem` | `VoxelWater/ProceduralVoxelTerrainWaterSystem.cs` | Lakes, ponds, rivers, ocean |
| `ProceduralVoxelTerrainScatterer` | `VoxelScatter/ProceduralVoxelTerrainScatterer.cs` | Tree/shrub/prop biome placement |
| `ProceduralVoxelStartAreaSystem` | `VoxelStartArea/ProceduralVoxelStartAreaSystem.cs` | Safe starting zone |
| `Inventory` | `Inventory/Inventory.cs` | Player item storage, stacking, events |

---

## Partial Class Split (ProceduralVoxelTerrain)

`ProceduralVoxelTerrain` is split across 8 partial files — each owns a distinct concern. Add new terrain features to the correct partial file:

| Partial File | Owns |
|---|---|
| `ProceduralVoxelTerrain.cs` | Inspector fields, public API, chunk management |
| `TerrainMaterialManager.cs` | Material definitions, profile textures, shader uniforms, basin constants |
| `TerrainEditingCore.cs` | Excavate, paint, voxel editing |
| `ChunkLifecycle.cs` | Chunk creation, destruction, rebuild triggers |
| `TerrainGenerationOperation.cs` | Burst Jobs, generation pipeline steps |
| `TerrainGenerationOrchestration.cs` | Async frame-budgeted generation loop |
| `EditorTerrainGenerationDriver.cs` | Editor-only generation (not in builds) |
| `TerrainRegionSnapshot.cs` | Terrain state capture/restore |

---

## Shader Architecture

**`Assets/Shader/VoxelTerrainLit.shader`** — custom URP opaque terrain shader. Terrain albedo is read entirely from `vertex.color.rgb`, baked per-vertex by `ChunkMeshBuilder.ComputeVertexColors` at mesh build time. The shader does not perform per-column texture lookups for base color.

### Vertex Color Layout (baked by ChunkMeshBuilder.ComputeVertexColors)

| Channel | Content |
|---------|---------|
| R, G, B | Terrain material albedo (soil layer, beach, basin sediment, ore tint, etc.) |
| A | Slope factor — drives rock-face color blend in the fragment shader |

### Shader Rules (CRITICAL)
- **`Properties {}` entries MUST also be declared in `CBUFFER_START(UnityPerMaterial)`.** The SRP Batcher requires every `Properties {}` entry to have a matching declaration in this CBUFFER. Defining a property in `Properties {}` without a corresponding CBUFFER entry (or vice-versa) causes the material to render as **solid black** at runtime with no error message.
- **Terrain albedo is vertex color only** — do not add per-column texture lookups for base albedo. All soil/basin/beach/ore color decisions are already baked into `vertex.color.rgb` by `ChunkMeshBuilder.ComputeVertexColors`.

---

## Terrain Material Stack

12 named materials assigned per-voxel, from surface down:

```
Organic → Topsoil → Eluviation → Subsoil → Parent → Weathered → Bedrock
                                                                  ↑ deep underground

Special surface overrides (by location/context):
Beach:  BeachSand, BeachGravel
Basin:  BasinGravel (rim) → BasinSand → BasinMud → Clay (by water depth)
Ore:    IronVein, CopperVein (rare, underground)
```

Material index constants and color definitions are in `TerrainMaterialManager.cs`.

---

## Harvesting System

The harvesting system is interface-driven. Any harvestable object implements `IHarvestable`:

```csharp
public interface IHarvestable {
    bool Harvest(Inventory playerInventory);
    string GetHarvestDisplayName();
    string GetHarvestPreview();
}
```

Systems that provide harvestables via player raycasts implement `IRaycastHarvestableProvider`. `PlayerInteraction` queries all registered providers and picks the nearest hit.

**Current harvestable types:**
- Terrain voxels (`ProceduralVoxelTerrain` via `IRaycastHarvestableProvider`)
- Procedural trees (`ProceduralTreeHarvestable`)
- Serviceberry shrubs (`ServiceberryShrubHarvestable`)
- Water bodies (`ProceduralVoxelTerrainWaterSystem` via `IRaycastHarvestableProvider`)

**To add a new harvestable:** Implement `IHarvestable` on a MonoBehaviour, and optionally `IRaycastHarvestableProvider` if it needs raycast integration.

---

## Inventory & Composition System

`InventoryItem` tracks both name and molecular/resource composition. Two items stack if and only if they share the **same name** and the **same set of molecule names** (regardless of percentages). When stacked, compositions blend by mass-weighted average.

`CompositionInfo` is a ScriptableObject defining what resources an item contains (with optional random range per resource). Create new `CompositionInfo` assets under `Assets/` via `Create → Biomolecular → CompositionInfo`.

`CompositionInfoRegistry` is a lookup: item display name → `CompositionInfo`. Register new harvestable items here.

---

## Water & Basin System

Water bodies are generated by `FreshwaterGenerator`, solved for water level by `LakeHydrologySolver`, then carved into terrain by `WaterTerrainCarver` (a stateless static class). Always use `BeginBulkEdit()` / `EndBulkEdit()` around multi-step carving operations.

Basin material painting order: carve first (geometry), then paint materials, then rebuild shader textures. Painting before carving will expose wrong subsurface layers.

---

## Performance Conventions

- **Burst Jobs** are used for all hot terrain generation paths. Job structs live in `TerrainGenerationOperation.cs` and `LakeTriangleFilterJob.cs`. New parallel work should use `IJobParallelFor` + `[BurstCompile]`.
- **Frame budget**: Generation is async and time-sliced; the frame budget (default 4ms) is configurable in the Inspector on `ProceduralVoxelTerrain`.
- **NativeArray / NativeList**: Use Unity Collections for job-compatible data. Dispose in `finally` blocks.
- **No LINQ in hot paths**: Terrain generation uses flat arrays with manual index math (`VoxelDataStore`). Keep allocations out of `Update()`.

---

## Coding Conventions

### Naming
- `Procedural*` — runtime procedural generation components
- `*System` — major MonoBehaviour systems
- `*Generator` — stateless or internal generation logic
- `*Manager` — manages state/lifecycle of resources
- `*Helper` / `*Utility` — pure static utilities

### Fields
```csharp
[SerializeField] private float myField;       // Serialized private fields
[NonSerialized]  private float runtimeState;  // Transient runtime state
[Header("Section")] [SerializeField] ...      // Inspector grouping
[Min(0f)] [Range(0, 1)] ...                   // Inspector constraints
```

### Events
```csharp
// Prefer Action<TArgs> for system events
public event Action<VoxelTerrainGeometryChangedEventArgs> GeometryChanged;
// Prefer delegate for simple inventory-style events
public delegate void InventoryChanged();
public event InventoryChanged OnInventoryChanged;
```

### ScriptableObjects
All ScriptableObjects use `[CreateAssetMenu(menuName = "Biomolecular/...")]`.

### Unity-specific
- Always check `if (this == null)` before accessing Unity objects after `await` / coroutine yields.
- Use `OnValidate()` for Inspector-driven validation, not property setters.
- Prefer `OnEnable` / `OnDisable` for event subscription/unsubscription lifecycle.
- Use `[HideInInspector]` for fields that must be serialized but should not appear in the Inspector.

---

## Seasonal System

`SeasonCalendar` (static utility) provides date math. Key enums:
- `CalendarMonth` — January through December
- `WorldSeason` — Winter, Spring, Summer, Autumn

Tree harvests check season for fruit availability. Extend seasonal logic by adding cases to `SeasonCalendar` and calling it from `IHarvestable.Harvest()`.

---

## Biome Scattering

`ProceduralVoxelTerrainScatterer` places prefabs across terrain using `TerrainScatterPrototype` ScriptableObjects (density, height range, slope, biome rules). The built-in preset is `OlympicRainforestPreset`. Create new presets via `Create → Biomolecular → TerrainScatterPrototype`.

Scatterer excludes water areas automatically via `WaterSpatialQueryUtility`. Overlap avoidance is handled by `SpatialPlacementSolver`.

---

## Build & Testing

- **Unity version:** 6000.4.0f1
- **Batch build:**  
  `"C:\Program Files\Unity\Hub\Editor\6000.0.4f1\Editor\Unity.exe" -batchmode -quit -projectPath "C:\Users\Silver\source\repos\biomolecular\Biomolecular" -logFile build.log`
- **Test scenes:** `Assets/Scenes/Test.unity`, `Tester.unity`, `TesterTwo.unity`
- **No custom CI pipeline** — validate by opening the project and checking the Unity Console for errors.

---

## What NOT to Do

- Do not add terrain shader properties to the `Properties {}` block (SRP Batcher will break rendering).
- Do not duplicate the basin noise formula or thresholds in the shader — they are pre-baked from C#.
- Do not call internal generator classes (`FreshwaterGenerator`, `VoxelTerrainGenerator`) from outside their owning facade.
- Do not use LINQ in terrain generation hot paths.
- Do not add managed allocations (`new List<>()`, `string` concatenation) inside Burst jobs.
- Do not paint basin materials before carving terrain geometry.
