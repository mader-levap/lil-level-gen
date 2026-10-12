# Generator

This is preliminary design. It can and will change.

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
  - Contains `LilGraph`. This is abstract representation of level.
  - Contains `LilMap`. This is concrete representation of map that can be used for generating meshes and object in game.

### Graph.

- `LilGraph`: Contains all nodes and relationships between them.
TODO Maybe there is some lightweight library for that?

### Map.

- `LiLMap`: Entire level.
  - Contains one or more `LiLArea`s.
  - Contain data about entities, markers and other objects in `LilEntity` list.

- `LiLArea`: Large (usually) volume of space that has common characteristics. You can think of it as chunk, if chunk was not perfect cube.
  - Has `LiLAreaSettings` data, including individual seed. That allows very differently sized, styled and themed areas in single map.
  - Contains one or more `LiLRoom`s.
  - All rooms in area share many properties of area.
  - While it has dimensions, not all cells must be filled. That means areas can have different shapes, not just filled cubes. You can fit different area in unused space of other area, it is allowed (unless settings forbid it).
- `LiLRoom`: Single enclosed space.
  - Has `LiLRoomSettings` data.
  - Contains one or more `LiLCell`s.
  - While it has dimensions, not all cells must be present. That means rooms can have different shapes, not just filled cubes. You can fit different room in unused space of other room, it is allowed (unless settings forbid it).
- `LiLCell`: Basic unit of map: 2x2x2 meters.
  - Contains fill type and four `LiLWall`s. Note one or more wall can be absent if it would be invisible or should not be present in first place (empty cell).
- `LiLWall`: Describes wall.
  - Contains wall type, name of mesh that should be used, additional decoration (if any), texture etc.

- `LilEntity`: Common class for all entities.
  - Every entity has X, Y and Z coordinates, placing it in given cell.

Tests and map validator will have to ensure that walls on each side of main cell agree with each other, fill type of cell agrees with type of wall, neighbor filled cells do not have wall on side they share etc.

**Very important concept**: While cells are on 3d grid, not all cells are same, though each set of cells repeats on grid.
This allows thin walls, thin floor/ceiling between storeys and other effects.

## Structure of theme.

TODO: add theme/style config.
TODO: define and determine what theme, style etc means here.
