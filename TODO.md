Now we need algorithm that can put obstacles and solutions on any graph without softlock. Separate class `ObstaclePlacer` in separate subdirectory and appropriate namespace: `Internal.Generator.Graph.Obstacler`. I expect it will need some auxillary classes.
For now it will be used only in `GraphBuilderSimple` (after `RunProcessors()`). `GraphBuilderLinear` uses its own algo to place obstacles taking advantage of linear nature of this builder and other builders do not exist yet.
It will likely have multiple passes and use flood algo.

Features:
- Uses `Complexity` as max amount of obstacles to put. Zero complexity means no-op, exit early.
- Main pass ensures that there are many obstacles between node with start POI and node with exit POI. If start and exit POI is same node, this pass is skipped.
  - Not all Complexity is used up here (usually, small graphs and/or small Complexity means all might be used).
  - Must deal properly with various edge cases, for example start and exit node having direct relationship. You can either slap multiple obstacles on that relationship or single obstacle that requires solution behind other obstacles elsewhere or anything between.
- Side pass: can deal separately with secrets or goals in leaf or dead end nodes, blocking them off with obstacle and dropping solution elsewhere in graph. Only if there is leftover Complexity to spare.
- Either way, too high Complexity is silently ignored. There is limit of max obstacles on passage.

Investigate possibilities. If you have better idea or algo, propose it. This is pure planning session. Findings save in `OBSTACLES.md`.
