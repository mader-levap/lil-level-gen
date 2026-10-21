# Generator

This is preliminary design. It can and will change.

Generator will create the world. This is finite world, though generator is designed in way that allows adding stuff to already existing world that was properly finished or paused.

Smallest unit of work is step. If you finished step, state of data must be consistent and correct (meaning editor/game can generate level geometry and objects just fine). Only exception: all steps except last one can have map not being consistent with graph and map might not be beatable.

## Data

Generator needs two datasets to work:
- Theming data: provide knowledge about level theming. It is essentially information about available game assets so graph and map generator can use them properly. Later editor/game will have AssetTranslator that will translate generated data (including style) into actual assets put in 3D world with correct rotation, orientation etc.
- Configuration data: provide config for generating level with specific properties.

## Structure of theme

Generator engine has some concepts related to themes:
- **Theme**: Content within area with particular consistent look and feel, both visually and architecturally. For example, `home` theme focuses on what you can see in your own home.
  Note that it is allowed for same asset to appear in multiple themes. 
- **Subtheme**: Different versions of same theme. For example, `home` can be studio apartment, flat in a block housing estate or entire mansion.
- **Zone**: You can think of zones as biomes of same theme. For example, `home` can have differently decorated rooms like Bathroom, Living Room, etc.
- **Style**: Chosen theme-related settings.

Theme data must be (de)serializable to/from JSON.

- `ThmThemes`: Root of level themes. Contains map of themes: key is identifier of theme, value is `ThmTheme`.
  - `ThmTheme`: Single theme.

## Structure of config

Configuration must be (de)serializable to/from JSON.

- `CnfLevelGeneration`: Root of level configuration.
  - `CnfGeneral`: General configuration data like `Seed` or `Corruption`.
  - `CnfStyle`: Style configuration. Currently placeholder.
  - `CnfGraph`: Graph configuration. Builders interpret these settings.
    - `Builder`: Which graph builder to use (`OneNode`, `Simple` or `Linear`; `OneNode` is the default). More builders will come.
    - `Size`: Size of graph. Usually number of nodes. Soft cap - builder is allowed to add more nodes if small number of nodes makes it impossible to make graph while fulfilling other settings. Hard cap is 100 (`CnfGraph.MaxSize`).
    - `Connectivity`: General connectivity of graph. It is not amount of relationships between nodes, at least not directly.
    - `Complexity`: General complexity of graph. Usually number of obstacles.
    - `ProcOpts` (`CnfProcOpts`): Options of the graph finalisation processors. One section per processor; sections left out keep defaults matching the hardcoded values used before options became configurable. Ignored by builders that run no processors (`OneNode`).
  - `CnfMap`: Map configuration. Currently placeholder.
  - `CnfDebug`: Debug configuration data.

## Structure of resulting map data

- `LilWorld`: All data.
  - Contains `LilGraph`. This is abstract representation of level. Graph is generated first based on config, then `LilMap` is created based on config and graph.
  - Contains `LilMap`. This is concrete representation of map that can be used for generating meshes and object in game.
  - Contains `LilStats`. Various statistics about current state of world, like count of areas, how many steps were made etc.

### Graph.

- `LilGraph`: Contains all nodes and relationships between them.
  - Contains one or more `LilNode`s.
  - Contains one or more `LilEdge`s.

- `LilNode`: Single place in the level.
  - Has unique `Id` (GUID).
  - Contains zero or more `LilPoi`s. Player has access to everything within given place.
  - Has `RelSize`. If node A has value 1 and node B 2, latter node is *roughly* twice as big, volume-wise. It is soft requirement.
    For example, if node A is processed by MapGenerator into two 2x2x1 rooms, node B should have around 16 cells, give or take. It does not matter if node B represents one big 4x4x1 room or seven 1x2x1 small rooms or even 1x1x17 chimney (though you would need some serious Corruption rate to get such deranged result). Volume of taken space matters.
  - Has `MaxEdges`: hard cap on relationships touching the node (both directions, parallel passages count individually). Negative means no limit.
- `LilPoi`: Point of interest inside a node.
  - Has unique `Id` (GUID), unique across entire graph so conditions can reference a specific POI.
  - Has type: `Start`, `Exit`, `Goal` or `Solution`.
  - Example of `Solution`: key/button/lever used to open door.
- `LilEdge`: Passage between two places (nodes).
  - Has unique `Id` (GUID).
  - References source and destination node by `Id`.
  - Can be bidirectional (most common) or one-way.
  - Contains zero or more `LilCondition`s. Empty means open (always traversable) passage.
- `LilCondition`: Single requirement that must be met before passage can be used.
  - References a specific solution POI (not just its node) by `Id`, because one place can contain multiple solutions each opening a different place.
  - All conditions on an edge must be met (e.g. door requiring both red and blue keys).

### Graph builders.

`GraphGenerator` creates the `AGraphBuilder` implementation selected by `CnfGraph.Builder` (via `GraphBuilderFactory`), hands it the run's inputs with `Setup(config, prepData, graph)`, and calls the parameterless `Build()` to fill `LilGraph` in place. This is considered single step.

- `OneNodeGraphBuilder`: Single node containing both start and exit POI, no edges, conditions or obstacles. Player can beeline to exit immediately. Ignores size, connectivity and complexity - always produces the same shape. Useful for tests, debug and special scenarios.
- `SimpleGraphBuilder`: One or more nodes without obstacles. Size sets the node count. First builds a minimal connected skeleton (a tree) from one randomly rolled scenario: a plain chain, a star, or a chain with the remaining nodes hanging off any of its nodes - all three come from the same algorithm, parameterized by the length of a linear prefix; remaining nodes attach to a central node, and when that central reaches its skeleton cap of 7 relationships per node, a new eligible node takes over, so no node ever exceeds the cap. Then adds extra random relationships on top until `Connectivity` is spent or no eligible node pair remains under the limit of 4 relationships per node (nodes already above 4 from the skeleton get none). Start and exit land in two random nodes, which may be the same node. Ignores complexity, never fails - unsatisfiable settings are stretched silently.
- `LinearGraphBuilder`: Chain of nodes - first holds start, last holds exit, in-between nodes link to previous and next node; node count follows `Size`. Then adds extra random relationships on top until `Connectivity` is spent or no eligible node pair remains: a random source (never the last node) links to a random later node at most `MaxSkipDistance` (2) nodes away, adjacent targets most likely and likelihood falling with distance; at most 4 extra relationships per node counted per direction (in and out separately, `MaxExtraDegree` is 2 each), never past the node's total `MaxEdges` hard cap of 5 (including skeleton), and at most one extra relationship per node pair, so an adjacent pair can carry two parallel passages while a skipping pair carries one. Start and exit always land in the first and last node respectively. Then the obstacle pass spends `Complexity`: each iteration picks a random node (never the first) that still has capacity, creates a new solution POI and adds one `RequiresSolution` condition pointing at it to every relationship going into that node - so all in-passages of a node always carry the same conditions and saturate together at `MaxConditionPerRel` (3) conditions per relationship. The solution lands at or before the node's chain predecessor, most likely right in front of the guarded passage and less likely up to `MaxConditionDistance` (3) nodes away, which keeps the graph beatable. Once every relationship is saturated the excess `Complexity` is discarded. Soft constraints throughout - never fails, unsatisfiable settings are stretched silently.

All builders share the `AGraphBuilder` abstract base class, which stores the setup inputs in protected fields (`Config`, `PrepData`, `Graph`). Every builder must be deterministic for the same configuration (IDs derived from the seed, never random GUIDs) and must produce a solvable graph (no softlock).

`Simple` and `Linear` finish with the same four finalisation processors in fixed order - secrets, objectives, relative size, grouping - each parameterised by its section of `CnfGraph.ProcOpts`.

### Map.

- `LilMap`: Entire level.
  - Contains one or more `LilArea`s.
  - Contain data about entities, markers and other objects in `LilEntity` list.

- `LilArea`: Large (usually) volume of space that has common characteristics. You can think of it as chunk, if chunk was not perfect cube.
  - Has `LilAreaSettings` data, including individual seed. That allows very differently sized, styled and themed areas in single map.
  - Contains one or more `LilRoom`s.
  - All rooms in area share many properties of area.
  - While it has dimensions, not all cells must be filled. That means areas can have different shapes, not just filled cubes. You can fit different area in unused space of other area, it is allowed (unless settings forbid it).
- `LilRoom`: Single enclosed space.
  - Has `LilRoomSettings` data.
  - Contains one or more `LilCell`s.
  - While it has dimensions, not all cells must be present. That means rooms can have different shapes, not just filled cubes. You can fit different room in unused space of other room, it is allowed (unless settings forbid it).
- `LilCell`: Basic unit of map: 2x2x2 meters.
  - Contains fill type and four `LilWall`s. Note one or more wall can be absent if it would be invisible or should not be present in first place (empty cell).
- `LilWall`: Describes wall.
  - Contains wall type, name of mesh that should be used, additional decoration (if any), texture etc.

- `LilEntity`: Common class for all entities.
  - Every entity has X, Y and Z coordinates, placing it in given cell.

Tests and map validator will have to ensure that walls on each side of main cell agree with each other, fill type of cell agrees with type of wall, neighbor filled cells do not have wall on side they share etc.

**Very important concept**: While cells are on 3d grid, not all cells are same, though each set of cells repeats on grid.
This allows thin walls, thin floor/ceiling between storeys and other effects.

### Map builders.

`MapGenerator` creates the `AMapBuilder` implementation selected by `CnfMap.Builder` (via `MapBuilderFactory`), hands it the run's inputs with `Setup(config, prepData, graph)`, and calls the parameterless `Build()` to fill `LilMap` in place. Map is built step by step.

Each builder handles single theme.

TODO

## Cancellation

Runs are cancellable through `LevelGeneration.Cancel()` or an optional token passed to `Start`/`Resume`. Convention for generator passes:

- Every pass takes a `CancellationToken` parameter (never a stored field) and calls `ThrowIfCancellationRequested()` at pass boundaries.
- Inside pass cancellation happens at step boundary. Note some passes are fast enough that they are considered single step.
- Cancellation must never change the output of a run that is not cancelled: it only truncates it. The cancelled run reports `EnGenResult.Cancelled` and its partial data is discarded.
