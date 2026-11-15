namespace LostInLimbo.LevelGen.SeedSweep.Data;

/// <summary>What the harness does in one invocation.</summary>
internal enum EnSweepCommand
{
  /// <summary>Generate many levels and report on them.</summary>
  Sweep,
  /// <summary>Generate selected levels and print their config and graph.</summary>
  Dump,
  /// <summary>Print usage only.</summary>
  Help,
}
