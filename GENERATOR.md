# Generator

This is preliminary design. It can and will change.

Generator will create the world. This is finite world, though generator is designed in way that allows adding stuff to already existing world that was properly finished or paused.

Smallest unit of work is step. If you finished step, state of data must be consistent and correct (meaning editor/game can generate level geometry and objects just fine). Only exception: all steps except last one can have map not being consistent with graph.

## Structure of config

Configuration must be (de)serializable to/from JSON.

- `CnfLevelGeneration`: Root of configuration.
  - `CnfGeneral`: General configuration data like `Seed` or `Corruption`.
  - `CnfDebug`: Debug configuration data. Placeholder for now.
  - TODO more

_status will have to be altered later, when innards of generator will want to give feedback (update status somehow)
We also need to get generated data, somehow. Probably held in same way as _status. Ask AI.

## Structure of map data

- `LilWorld`: All data.
  - Contains `LilGraph`. This is abstract representation of level. Graph is generated first based on config, then `LilMap` is created based on config and graph.
  - Contains `LilMap`. This is concrete representation of map that can be used for generating meshes and object in game.
  - Contains `LilStats`. Various statistics about current state of world, like count of areas, how many steps were made etc.

### Graph.

- `LilGraph`: Contains all nodes and relationships between them.
TODO Maybe there is some lightweight library for that? But their data structures need to be serializable to JSON. Alternatively, we implement our own graph data. In this case we will likely need some GUID to uniquely identify nodes.

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

## Structure of theme.

TODO: add theme/style config.
TODO: define and determine what theme, style etc means here.

## Cancellation

Runs are cancellable through `LevelGeneration.Cancel()` or an optional token passed to `Start`/`Resume`. Convention for generator passes:

- Every pass takes a `CancellationToken` parameter (never a stored field) and calls `ThrowIfCancellationRequested()` at pass boundaries.
- Hot loops (cells, rooms, walls) check only every N iterations, not per element, to keep the cost negligible.
- Cancellation must never change the output of a run that is not cancelled: it only truncates it. The cancelled run reports `EnGenResult.Cancelled` and its partial data is discarded.
