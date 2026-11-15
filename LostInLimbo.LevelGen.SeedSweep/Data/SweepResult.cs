namespace LostInLimbo.LevelGen.SeedSweep.Data;

/// <summary>Outcome of a whole sweep.</summary>
/// <param name="Records">Measured runs, sorted by run index.</param>
/// <param name="WarmupRecords">Untimed warm-up runs generated before the sweep; not part of the results.</param>
internal sealed record SweepResult(IReadOnlyList<RunRecord> Records, IReadOnlyList<RunRecord> WarmupRecords);
