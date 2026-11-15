using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.SeedSweep.Cli;
using LostInLimbo.LevelGen.SeedSweep.Export;

namespace LostInLimbo.LevelGen.SeedSweep.Data;

/// <summary>
/// Options of one harness invocation, as parsed by <see cref="SweepOptionsParser"/>.
/// </summary>
/// <remarks>
/// Run indices are absolute: run <c>i</c> of a sweep has index <c>StartIndex + i</c>, and
/// <see cref="RunSpec.ForIndex"/> turns an index into the same props every time, so any run can
/// be reproduced by <see cref="EnSweepCommand.Dump"/> with the same options.
/// </remarks>
internal sealed record SweepOptions
{
  /// <summary>Default number of runs of a sweep.</summary>
  public const int DefaultRuns = 10000;
  /// <summary>Default text seed prefix; run <c>i</c> uses <c>"sweep-i"</c>.</summary>
  public const string DefaultSeedPrefix = "sweep";
  /// <summary>Default interval of determinism reruns: every 25th run is generated twice.</summary>
  public const int DefaultDeterminismEvery = 25;
  /// <summary>Default per-run timeout in seconds.</summary>
  public const int DefaultTimeoutSeconds = 20;
  /// <summary>
  /// Default number of untimed warm-up runs: one full difficulty cycle with many themes off
  /// and one with it on, which covers the code paths of both graph builders.
  /// </summary>
  public const int DefaultWarmupRuns = 2 * (PropMain.MaxDifficulty + 1);

  /// <summary>
  /// Untimed runs generated before measuring, so measured runs see JIT-compiled code; 0 disables.
  /// Warm-up runs use their own seeds and are not part of the results.
  /// </summary>
  public int WarmupRuns { get; init; } = DefaultWarmupRuns;

  /// <summary>Command to execute.</summary>
  public EnSweepCommand Command { get; init; } = EnSweepCommand.Sweep;

  /// <summary>Number of runs of a sweep; at least 1.</summary>
  public int Runs { get; init; } = DefaultRuns;

  /// <summary>Lowest difficulty, inclusive; within <c>[0, PropMain.MaxDifficulty]</c>.</summary>
  public int DifficultyMin { get; init; } = 0;

  /// <summary>Highest difficulty, inclusive; not below <see cref="DifficultyMin"/>.</summary>
  public int DifficultyMax { get; init; } = PropMain.MaxDifficulty;

  /// <summary>How many themes are set per run.</summary>
  public EnManyThemesMode ManyThemes { get; init; } = EnManyThemesMode.Both;

  /// <summary>Text seed prefix; run <c>i</c> uses <c>"{prefix}-{i}"</c>.</summary>
  public string SeedPrefix { get; init; } = DefaultSeedPrefix;

  /// <summary>Index of the first run; not negative.</summary>
  public int StartIndex { get; init; } = 0;

  /// <summary>Every run whose index is a multiple of this is generated twice and compared; 0 disables it.</summary>
  public int DeterminismEvery { get; init; } = DefaultDeterminismEvery;

  /// <summary>Degree of parallelism; 0 uses the processor count.</summary>
  public int Parallelism { get; init; } = 0;

  /// <summary>Per-run timeout in seconds; at least 1.</summary>
  public int TimeoutSeconds { get; init; } = DefaultTimeoutSeconds;

  /// <summary>Output root; <see langword="null"/> uses <see cref="ResultPaths.DefaultRoot"/>.</summary>
  public string? OutputRoot { get; init; } = null;

  /// <summary>Run indices to dump (<see cref="EnSweepCommand.Dump"/> only).</summary>
  public IReadOnlyList<int> DumpIndices { get; init; } = [];

  /// <summary>
  /// Explicit text seed to dump (<see cref="EnSweepCommand.Dump"/> only), instead of run
  /// indices. Requires a single difficulty and <see cref="EnManyThemesMode.On"/> or
  /// <see cref="EnManyThemesMode.Off"/>.
  /// </summary>
  public string? DumpTextSeed { get; init; } = null;

  /// <summary>Number of difficulties in the range.</summary>
  public int DifficultySpan => DifficultyMax - DifficultyMin + 1;

  /// <summary>Effective degree of parallelism: <see cref="Parallelism"/>, or the processor count for 0.</summary>
  public int EffectiveParallelism => Parallelism > 0 ? Parallelism : Environment.ProcessorCount;
}
