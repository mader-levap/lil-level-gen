using System.Diagnostics;
using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Api.Serializers;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Prop;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Data.Result.Stats;
using LostInLimbo.LevelGen.Data.Status;
using LostInLimbo.LevelGen.SeedSweep.Data;

namespace LostInLimbo.LevelGen.SeedSweep.Runner;

/// <summary>
/// Generates levels through the public API, the way a consumer would, and analyzes them.
/// </summary>
/// <remarks>
/// Each run resolves props to a config, then runs a session up to the graph phase
/// (<see cref="EnPhase.Map"/>), so a successful run ends <see cref="EnGenResult.Paused"/>.
/// </remarks>
internal static class SweepRunner
{
  /// <summary>Poll interval of the session status loop, in milliseconds.</summary>
  private const int PollIntervalMs = 1;
  /// <summary>Text seed prefix of warm-up runs; distinct from any sweep prefix default.</summary>
  public const string WarmupSeedPrefix = "warmup";

  /// <summary>
  /// Runs all runs of a sweep in parallel, after <see cref="SweepOptions.WarmupRuns"/> untimed
  /// warm-up runs (see <see cref="WarmUp"/>).
  /// </summary>
  /// <param name="options">Sweep options.</param>
  /// <param name="progress">Called with the number of finished runs after each run; may be null.</param>
  /// <param name="cancellationToken">Stops scheduling new runs.</param>
  /// <returns>Measured records sorted by run index, plus the warm-up records.</returns>
  /// <exception cref="OperationCanceledException">Cancelled.</exception>
  public static SweepResult RunAll(
    SweepOptions options, Action<int>? progress = null, CancellationToken cancellationToken = default)
  {
    if (options is null) throw new ArgumentNullException(nameof(options));

    var warmup = WarmUp(options.WarmupRuns, TimeSpan.FromSeconds(options.TimeoutSeconds), cancellationToken);
    var records = new RunRecord[options.Runs];
    var finished = 0;
    var parallel = new ParallelOptions
    {
      MaxDegreeOfParallelism = options.EffectiveParallelism,
      CancellationToken = cancellationToken,
    };

    Parallel.For(0, options.Runs, parallel, i =>
    {
      var spec = RunSpec.ForIndex(options.StartIndex + i, options);
      records[i] = Run(spec, ShouldCheckDeterminism(spec.Index, options), TimeSpan.FromSeconds(options.TimeoutSeconds));
      progress?.Invoke(Interlocked.Increment(ref finished));
    });

    return new SweepResult(records, warmup);
  }

  /// <summary>
  /// Generates <paramref name="runs"/> untimed levels sequentially on the calling thread, so the
  /// JIT compiles the generator's code paths before anything is measured.
  /// </summary>
  /// <remarks>
  /// Independent of sweep options: run <c>i</c> uses difficulty <c>i % 21</c>, many themes on
  /// for odd difficulty cycles and text seed <c>"warmup-i"</c>, covering both graph builders
  /// across all difficulties. Never the same levels as sweep runs.
  /// </remarks>
  /// <param name="runs">Number of warm-up runs; 0 does nothing.</param>
  /// <param name="timeout">Per-run timeout.</param>
  /// <param name="cancellationToken">Checked before every run.</param>
  /// <returns>Records of the warm-up runs, for the report summary.</returns>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="runs"/> is negative.</exception>
  /// <exception cref="OperationCanceledException">Cancelled.</exception>
  public static IReadOnlyList<RunRecord> WarmUp(int runs, TimeSpan timeout, CancellationToken cancellationToken = default)
  {
    if (runs < 0) throw new ArgumentOutOfRangeException(nameof(runs), runs, "Warm-up run count must not be negative.");

    var span = PropMain.MaxDifficulty + 1;
    var records = new List<RunRecord>(runs);
    for (var i = 0; i < runs; i++)
    {
      cancellationToken.ThrowIfCancellationRequested();
      var spec = new RunSpec(i, i % span, i / span % 2 == 1, $"{WarmupSeedPrefix}-{i}");
      records.Add(Run(spec, checkDeterminism: false, timeout));
    }

    return records;
  }

  /// <summary>
  /// Generates and analyzes one run. Never throws for generation problems: they become errors
  /// of the returned record.
  /// </summary>
  /// <param name="spec">Run props.</param>
  /// <param name="checkDeterminism">Generate a second time and compare the serialized worlds.</param>
  /// <param name="timeout">Per-generation timeout.</param>
  /// <returns>Filled record.</returns>
  public static RunRecord Run(RunSpec spec, bool checkDeterminism, TimeSpan timeout)
  {
    return RunDetailed(spec, checkDeterminism, timeout).Record;
  }

  /// <summary>
  /// Like <see cref="Run"/>, also returning the config and world (for dumps).
  /// </summary>
  /// <param name="spec">Run props.</param>
  /// <param name="checkDeterminism">Generate a second time and compare the serialized worlds.</param>
  /// <param name="timeout">Per-generation timeout.</param>
  /// <returns>Record, plus config and world where they were produced.</returns>
  public static (RunRecord Record, CnfLevelGeneration? Config, LilWorld? World) RunDetailed(
    RunSpec spec, bool checkDeterminism, TimeSpan timeout)
  {
    if (spec is null) throw new ArgumentNullException(nameof(spec));

    CnfLevelGeneration? config = null;
    LilWorld? world = null;
    var record = new RunRecord
    {
      Index = spec.Index,
      Difficulty = spec.Difficulty,
      ManyThemes = spec.ManyThemes,
      Seed = spec.TextSeed,
    };

    try
    {
      config = LevelGenConfig.Execute(spec.ToProps());
      RunAnalyzer.FillConfig(record, config);

      var stopwatch = Stopwatch.StartNew();
      var outcome = Generate(config, timeout);
      record.WallMs = stopwatch.Elapsed.TotalMilliseconds;
      record.GenerationMs = outcome.Status.Elapsed.TotalMilliseconds;
      record.Status = outcome.TimedOut ? "Timeout" : outcome.Status.Result.ToString();
      if (outcome.Status.Result != EnGenResult.Paused || outcome.World is null)
      {
        record.Error($"status {record.Status}: {outcome.Status.FailureReason ?? outcome.Status.Error?.Message}");
        return (record, config, null);
      }

      world = outcome.World;
      RunAnalyzer.Analyze(record, config, world);
      if (checkDeterminism) CheckDeterminism(record, config, world, timeout);
    }
    catch (Exception exception)
    {
      // Harness boundary: any library exception is a finding of this run, not a harness crash.
      record.Error($"exception {exception.GetType().Name}: {exception.Message}");
    }

    return (record, config, world);
  }

  /// <summary>
  /// Runs a session for <paramref name="config"/> up to the graph phase.
  /// </summary>
  /// <param name="config">Resolved config.</param>
  /// <param name="timeout">Cancel the session after this long.</param>
  /// <returns>Final status and world.</returns>
  /// <exception cref="InvalidOperationException">The session refused to start.</exception>
  public static GenerationOutcome Generate(CnfLevelGeneration config, TimeSpan timeout)
  {
    if (config is null) throw new ArgumentNullException(nameof(config));

    var mapOnly = config with { Debug = new CnfDebug { EndAtPhase = EnPhase.Map } };
    var session = new LevelGenSession();
    if (!session.Start(EnRunKind.All, mapOnly)) throw new InvalidOperationException("Level generation session refused to start.");

    var stopwatch = Stopwatch.StartNew();
    while (true)
    {
      var status = session.Status();
      if (status.Result is not (EnGenResult.Running or EnGenResult.None)) return new GenerationOutcome(status, session.World(), false);

      if (stopwatch.Elapsed > timeout)
      {
        session.Cancel();
        return new GenerationOutcome(status, null, true);
      }

      Thread.Sleep(PollIntervalMs);
    }
  }

  /// <summary>True when the run with <paramref name="index"/> gets a determinism rerun.</summary>
  /// <param name="index">Absolute run index.</param>
  /// <param name="options">Options with the rerun interval.</param>
  /// <returns>Whether to rerun.</returns>
  public static bool ShouldCheckDeterminism(int index, SweepOptions options)
  {
    return options.DeterminismEvery > 0 && index % options.DeterminismEvery == 0;
  }

  /// <summary>Generates again and records an error when the world differs.</summary>
  private static void CheckDeterminism(RunRecord record, CnfLevelGeneration config, LilWorld world, TimeSpan timeout)
  {
    var rerun = Generate(config, timeout);
    if (rerun.Status.Result != EnGenResult.Paused || rerun.World is null)
      record.Error("determinism rerun failed");
    else if (!Serialize(world).AsSpan().SequenceEqual(Serialize(rerun.World)))
      record.Error("determinism: rerun produced different world");
  }

  /// <summary>Serializes a world for byte comparison.</summary>
  private static byte[] Serialize(LilWorld world)
  {
    using var stream = new MemoryStream();
    LevelGenWorldSerializer.Write(world, stream);
    return stream.ToArray();
  }
}
