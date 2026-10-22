# Generator

This is a preliminary design. It can and will change.

The generator will create the world. This is a finite world, though the generator is designed in a way that allows adding stuff to an already existing world that was properly finished or paused.

The smallest unit of work is a step. If you finish a step, the state of data must be consistent and correct (meaning the editor/game can generate level geometry and objects just fine). The only exception: all steps except the last one can have the map not being consistent with the graph and the map might not be beatable.

## Data

The generator needs two datasets to work:
- Theming data: provides knowledge about level theming. It is essentially information about available game assets so the graph and map generator can use them properly. Later the editor/game will have an AssetTranslator that will translate generated data (including style) into actual assets put in the 3D world with correct rotation, orientation, etc.
- Configuration data: provides config for generating a level with specific properties.

## Structure of theme

The generator engine has some concepts related to themes:
- **Theme**: Content within an area with a particular consistent look and feel, both visually and architecturally. For example, `home` theme focuses on what you can see in your own home.
  Note that it is allowed for the same asset to appear in multiple themes.
- **Subtheme**: Different versions of the same theme. For example, `home` can be studio apartment, flat in a block housing estate or entire mansion.
- **Zone**: You can think of zones as biomes of the same theme. For example, `home` can have differently decorated rooms like Bathroom, Living Room, etc.
- **Style**: Chosen theme-related settings.

Theme data must be (de)serializable to/from JSON.

- `ThmThemes`: Root of level themes. Contains map of themes: the key is the identifier of a theme, the value is a `ThmTheme`.
  - `ThmTheme`: Single theme.

## Structure of config

Configuration must be (de)serializable to/from JSON.

- `CnfLevelGeneration`: Root of level configuration.
  - `CnfGeneral`: General configuration data like `Seed` or `Degeneration`.
  - `CnfStyle`: Style configuration. Currently placeholder.
  - `CnfGraph`: Graph configuration. Builders interpret these settings.
    - `Builder`: Which graph builder to use (`OneNode`, `Simple` or `Linear`; `OneNode` is the default). More builders will come.
    - `Size`: Size of the graph. Usually the number of nodes. Soft cap - the builder is allowed to add more nodes if a small number of nodes makes it impossible to make the graph while fulfilling other settings. The hard cap is 100 (`CnfGraph.MaxSize`).
    - `Connectivity`: General connectivity of the graph. It is not the number of relationships between nodes, at least not directly.
    - `Complexity`: General complexity of the graph. Usually the number of obstacles.
    - `ProcOpts` (`CnfProcOpts`): Options of the graph finalization processors. One section per processor; sections left out keep defaults matching the hardcoded values used before options became configurable. Ignored by builders that run no processors (`OneNode`).
  - `CnfMap`: Map configuration. Currently placeholder.
  - `CnfDebug`: Debug configuration data.

## Structure of resulting map data

- `LilWorld`: All data.
  - Contains `LilGraph`. This is the abstract representation of the level. The graph is generated first based on the config, then `LilMap` is created based on the config and the graph.
  - Contains `LilMap`. This is the concrete representation of the map that can be used for generating meshes and objects in the game.
  - Contains `LilStats`. Various statistics about the current state of the world, like the count of areas, how many steps were made, etc.

### Graph

- `LilGraph`: Contains all nodes and relationships between them.
  - Contains one or more `LilNode`s.
  - Contains one or more `LilEdge`s.

- `LilNode`: Single place in the level.
  - Has unique `Id` (GUID).
  - Contains zero or more `LilPoi`s. The player has access to everything within the given place.
  - Has `RelSize`. If node A has value 1 and node B 2, the latter node is *roughly* twice as big, volume-wise. It is a soft requirement.
    For example, if node A is processed by MapGenerator into two 2x2x1 rooms, node B should have around 16 cells, give or take. It does not matter if node B represents one big 4x4x1 room or seven 1x2x1 small rooms or even 1x1x17 chimney (though you would need some serious degeneration rate to get such a deranged result). The volume of taken space matters.
  - Has `MaxEdges`: hard cap on relationships touching the node (both directions, parallel passages count individually). Negative means no limit.
- `LilPoi`: Point of interest inside a node.
  - Has unique `Id` (GUID), unique across entire graph so conditions can reference a specific POI.
  - Has type: `Start`, `Exit`, `Goal` or `Solution`.
  - Example of `Solution`: key/button/lever used to open a door.
- `LilEdge`: Passage between two places (nodes).
  - Has unique `Id` (GUID).
  - References source and destination node by `Id`.
  - Can be bidirectional (most common) or one-way.
  - Contains zero or more `LilCondition`s. Empty means open (always traversable) passage.
- `LilCondition`: Single requirement that must be met before the passage can be used.
  - References a specific solution POI (not just its node) by `Id`, because one place can contain multiple solutions each opening a different place.
  - All conditions on an edge must be met (e.g. door requiring both red and blue keys).

### Graph builders

`GraphGenerator` creates the `AGraphBuilder` implementation selected by `CnfGraph.Builder` (via `GraphBuilderFactory`), hands it the run's inputs with `Setup(config, prepData, graph)`, and calls the parameterless `Build()` to fill `LilGraph` in place. This is considered a single step.

- `GraphBuilderOneNode`: Single node containing both start and exit POI, no edges, conditions or obstacles. Player can beeline to the exit immediately. Ignores size, connectivity and complexity - always produces the same shape. Useful for tests, debug and special scenarios.
- `GraphBuilderSimple`: One or more nodes without obstacles. Size sets the node count. First it builds a minimal connected skeleton (a tree) from one randomly rolled scenario: a plain chain, a star, or a chain with the remaining nodes hanging off any of its nodes - all three come from the same algorithm, parameterized by the length of a linear prefix; remaining nodes attach to a central node, and when that central node reaches its skeleton cap of 7 relationships per node, a new eligible node takes over, so no node ever exceeds the cap. Then it adds extra random relationships on top until `Connectivity` is spent or no eligible node pair remains under the limit of 4 relationships per node (nodes already above 4 from the skeleton get none). Start and exit land in two random nodes, which may be the same node. Ignores complexity, never fails - unsatisfiable settings are stretched silently.
- `GraphBuilderLinear`: Chain of nodes - first holds start, last holds exit, in-between nodes link to the previous and next node; node count follows `Size`. Then it adds extra random relationships on top until `Connectivity` is spent or no eligible node pair remains: a random source (never the last node) links to a random later node at most `MaxSkipDistance` (2) nodes away, adjacent targets most likely and likelihood falling with distance; at most 4 extra relationships per node counted per direction (in and out separately, `MaxExtraDegree` is 2 each), never past the node's total `MaxEdges` hard cap of 5 (including skeleton), and at most one extra relationship per node pair, so an adjacent pair can carry two parallel passages while a skipping pair carries one. Start and exit always land in the first and last node respectively. Then the obstacle pass spends `Complexity`: each iteration picks a random node (never the first) that still has capacity, creates a new solution POI and adds one `RequiresSolution` condition pointing at it to every relationship going into that node - so all in-passages of a node always carry the same conditions and saturate together at `MaxConditionPerRel` (3) conditions per relationship. The solution lands at or before the node's chain predecessor, most likely right in front of the guarded passage and less likely up to `MaxConditionDistance` (3) nodes away, which keeps the graph beatable. Once every relationship is saturated the excess `Complexity` is discarded. Soft constraints throughout - never fails, unsatisfiable settings are stretched silently.

All builders share the `AGraphBuilder` abstract base class, which stores the setup inputs in protected fields (`Config`, `PrepData`, `Graph`). Every builder must be deterministic for the same configuration (IDs derived from the seed, never random GUIDs) and must produce a solvable graph (no softlock).

`Simple` and `Linear` finish with the same four finalization processors in the fixed order - secrets, objectives, relative size, grouping - each parameterized by its section of `CnfGraph.ProcOpts`.

### Map

- `LilMap`: Entire level.
  - Contains one or more `LilArea`s.
  - Contains data about entities, markers and other objects in the `LilEntity` list.

- `LilArea`: Large (usually) volume of space that has common characteristics. You can think of it as a chunk, if the chunk was not a perfect cube.
  - Has `LilAreaSettings` data, including an individual seed. That allows very differently sized, styled and themed areas in a single map.
  - Contains one or more `LilRoom`s.
  - All rooms in an area share many properties of the area.
  - While it has dimensions, not all of its cells must be filled. That means areas can have different shapes, not just filled cubes. You can fit a different area in the unused space of another area; it is allowed (unless the configuration forbid it).
- `LilRoom`: Single enclosed space.
  - Has `LilRoomSettings` data.
  - Contains one or more `LilCell`s.
  - While it has dimensions, not all of its cells must be present. That means rooms can have different shapes, not just filled cubes. You can fit a different room in the unused space of another room; it is allowed (unless the configuration forbid it).
- `LilCell`: Basic unit of map: 2x2x2 meters.
  - Contains fill type and four `LilWall`s. Note one or more walls can be absent if it would be invisible or should not be present in the first place (empty cell).
- `LilWall`: Describes a wall.
  - Contains wall type, name of mesh that should be used, additional decoration (if any), texture etc.

- `LilEntity`: Common class for all entities.
  - Every entity has X, Y and Z coordinates, placing it in a given cell.

Tests and the map validator will have to ensure that walls on each side of the main cell agree with each other, the fill type of the cell agrees with the type of the wall, neighboring filled cells do not have a wall on the side they share etc.

**Very important concept**: While cells are on a 3d grid, not all cells are the same, though each set of cells repeats on the grid.
This allows thin walls, thin floor/ceiling between storeys and other effects.

### Map builders

`MapGenerator` creates the `AMapBuilder` implementation selected by `CnfMap.Builder` (via `MapBuilderFactory`), hands it the run's inputs with `Setup(config, prepData, graph)`, and calls the parameterless `Build()` to fill `LilMap` in place. The map is built step by step.

Each builder handles a single theme.

TODO

## Cancellation

Runs are cancellable through `LevelGenerationSession.Cancel()` or an optional token passed to `Start`/`Resume`. Convention for generator passes:

- Every pass takes a `CancellationToken` parameter (never a stored field) and calls `ThrowIfCancellationRequested()` at pass boundaries.
- Inside a pass, cancellation happens at a step boundary. Note some passes are fast enough that they are considered a single step.
- Cancellation must never change the output of a run that is not cancelled: it only truncates it. The cancelled run reports `EnGenResult.Cancelled` and its partial data is discarded.
