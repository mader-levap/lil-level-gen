# Generator

This is preliminary design. It can and will change.

## Structure

- `LiLMap`: Entire level.
  - Contains one or more `LiLArea`s.
- `LiLArea`: Large (usually) volume of space that has common characteristics.
  - Has `LiLAreaSettings` data. That allows very differently sized, styled and themed areas in single map.
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
  
Tests and map validator will have to ensure that walls on each side of main cell agree with each other, fill type of cell agrees with type of wall, neighbor filled cells do not have wall on side they share etc.

**Very important concept**: While cells are on 3d grid, not all cells are same, though each set of cells repeats on grid.
This allows having thin walls and other effects.
