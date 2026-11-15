using LostInLimbo.LevelGen.SeedSweep.Data;

namespace LostInLimbo.LevelGen.SeedSweep.Cli;

/// <summary>Outcome of <see cref="SweepOptionsParser.Parse"/>.</summary>
/// <param name="Options">Parsed options; meaningful only when <paramref name="Errors"/> is empty.</param>
/// <param name="Errors">Problems found in the arguments; empty on success.</param>
internal sealed record SweepParseResult(SweepOptions Options, IReadOnlyList<string> Errors)
{
  /// <summary>True when the arguments were valid.</summary>
  public bool Success => Errors.Count == 0;
}
