We need algorithm that can put obstacles and solutions on any graph. Separate class `ProcObstaclePlacer` in separate subdirectory and appropriate namespace: `Internal.Generator.Graph.Processors.Obstacler`. I expect it will need some auxillary classes.
For now it will be used only in `GraphBuilderSimple` (in `RunProcessorsShaping()`, at end). `GraphBuilderLinear` uses its own algo to place obstacles taking advantage of linear nature of this builder and other builders do not exist yet.
It will likely have multiple passes and use flood algo. We need to ensure there is no softlock (that's it, solution to obstacle behind that obstacle).

Features:
- Uses `Complexity` as max amount of obstacles to put. Zero complexity means no-op, exit early. Same if only one node or start/exit POI are missing.
- Main pass ensures that there are many obstacles between node with start POI and node with exit POI. If start and exit POI is same node, this pass is skipped.
  - Not all Complexity is used up here (usually... small graphs and/or small Complexity means all might be used).
  - Must deal properly with various edge cases, for example:
    - Start and exit node having direct relationship. You can either slap multiple obstacles on that relationship or single obstacle that requires solution behind other obstacles elsewhere or anything between.
    - Flood means sometimes many passages between many nodes will get same obstacle, cutting off one group of nodes from the rest. Such fragmentation is fine, but try to put obstacle on bottleneck. We want to minimize amount of passages requiring same solution.
- Side pass: can deal separately with secrets, goals or optional goals in leaf or dead end nodes (cannot contain exit, obstacles or solutions to prevent possible softlocks), blocking them off with obstacle and dropping solution elsewhere in graph. Only if there is Complexity to spare.
- Unused Complexity simply is ignored. There is limit of max obstacles on passage (3).

Investigate possibilities. If you have better idea or algo, propose it.
This is pure planning session. Save findings in `OBSTACLES.md`. This is also where plan for it goes.
