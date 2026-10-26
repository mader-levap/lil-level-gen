Current tasks:

We need a config generator (called `LevelGenConfig`) that will generate the entire config based on a minimal set of properties. These properties will more or less reflect typical settings from game start screen where player decides on properties of level to run.
For example, such screen would have **size** (in game it would be an adjective like small or huge, but we get integer) or **difficulty** (named, but in practice it is also an integer: higher number means higher difficulty). All such properties would contribute in logical way to actual config, often in complex ways.

In general, a lot of properties would have range of possible values (number randomly chosen in that range) for various configuration variables.

Examples:
- Field `CnfGraph.Size` scales off **size** only weakly, as it is more about physical size than amount of nodes, but on average bigger size would mean more nodes. `CnfMap.Size` variable in future config of map generator would scale more directly off **size**.
  - More concrete example: **size** = small could pick from 1 to 3 nodes, medium 2 - 4, large 2 - 5, huge 2 - 6. Something like that.
- Field `CnfGraph.Complexity` would be more related to **difficulty**, but taking **size** in account: small graph can't really support large amount of obstacles. So we need some kind of algo to calculate final `Complexity` based on these properties.
- Field `CnfGeneral.Degeneration` would scale off **difficulty** directly, though still in certain range. Harder levels are more rotten and corrupted.

There will be also some config variables that aren't directly or indirectly set by properties. Examples:
- `CnfGeneral.Seed`: we just get current time as long.
- `CnfGraphBuilder.Builder`: pick builder randomly from all available builders except `OneNode`.
- `CnfGraphBuilderSimple.ChokepointChance`: random number from 0.2f to 0.95f. Obviously calculated only if picked builder is `GraphBuilderSimple`.
And last case - config vars that are just left untouched at default value.

In `Api` file `LevelGenConfig.cs`: just wrapper that calls internals.
In `Data.Prop`: properties to be translated into full config. Main property file is `PropMain.cs`.
In `Internal.Prop` all classes used for config generation based on properties

TODO:
- Think up all relevant properties.