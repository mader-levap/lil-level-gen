using System;
using System.Threading;

using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Data.Result.Stats;
using LostInLimbo.LevelGen.Data.Status;
using LostInLimbo.LevelGen.Internal.Generator.Graph;
using LostInLimbo.LevelGen.Internal.Generator.Map;
using LostInLimbo.LevelGen.Internal.Generator.Prepass;
using LostInLimbo.LevelGen.Internal.Status;

namespace LostInLimbo.LevelGen.Internal.Generator;

/// <summary>
/// <para>Main class managing the entire generation and its passes. Should be run in its own thread.</para>
/// <para>Updates the status during execution, so the consumer can show progress.</para>
/// </summary>
/// <remarks>
/// Prepass and graph runs are real passes that complete within a single execution; only the
/// map phase is still a placeholder that advances a fixed number of steps, each imitating
/// work by sleeping (see <see cref="MainLoop"/>).
/// Mutates its world in place on the worker thread, so consumers never get it from a
/// running run: terminal publishes pair it with a <see cref="EnGenResult.Paused"/> or
/// <see cref="EnGenResult.Finished"/> status in the store, and every other outcome pairs with
/// no world at all (see <see cref="PublishTerminal"/>).
/// </remarks>
internal sealed class LevelGenerator
{
  /// <summary>Map generator. Lazily created as it needs data unavailable in constructor.</summary>
  private MapGenerator _mapGenerator = null!;

  /// <summary>Kind of run. Mutated only between runs via <see cref="SetRunKind"/>.</summary>
  private EnRunKind _enRunKind;
  /// <summary>Configuration data for level generation.</summary>
  private readonly CnfLevelGeneration _config;
  /// <summary>Preparatory data based on config.</summary>
  private PrepData? _prepData = null;
  /// <summary>Shared, thread-safe status sink owned by the API layer.</summary>
  private readonly GenerationStatusStore _statusStore;

  /// <summary>World: partial or complete result of generation. Published to consumers only
  /// through <see cref="PublishTerminal"/>, never exposed while a run is in flight.</summary>
  private readonly LilWorld _world = new();

  /// <summary>
  /// Serializes pause requests against terminal publishes, so <see cref="TryRequestPause"/>
  /// never accepts a request for a run that has already committed to a terminal state, and
  /// the pause flag is always consumed together with that commit.
  /// </summary>
  private readonly object _pauseGate = new();

  /// <summary>
  /// Pause request flag. Written and consumed under <see cref="_pauseGate"/>; read without the
  /// gate at step boundaries, so it is volatile.
  /// </summary>
  private volatile bool _pauseRequested;

  /// <summary>Total number of placeholder steps of this generator's configuration.</summary>
  private int _totalSteps => _config.Debug?.PlaceholderSteps ?? CnfDebug.DefaultPlaceholderSteps;

  /// <summary>
  /// Prepares the level generator. Assumes the config was already validated by the API layer
  /// (see <see cref="CnfLevelGeneration.Validate"/>) before the run was claimed.
  /// </summary>
  /// <param name="enRunKind">The kind of run.</param>
  /// <param name="config">Configuration data for level generation. Must not be <see langword="null"/> and must be valid.</param>
  /// <param name="statusStore">Store receiving status snapshots during execution.</param>
  /// <exception cref="ArgumentNullException">Status store is <see langword="null"/>.</exception>
  public LevelGenerator(EnRunKind enRunKind, CnfLevelGeneration config, GenerationStatusStore statusStore)
  {
    if (statusStore is null) throw new ArgumentNullException(nameof(statusStore));

    _enRunKind = enRunKind;
    _config = config;
    _statusStore = statusStore;
  }

  /// <summary>
  /// Sets the kind of the next run. Called by the API layer when resuming a paused generation
  /// with a different run kind than the original run used. Must only be called after the run
  /// has been claimed, so no other thread can be executing this generator at the time.
  /// </summary>
  /// <param name="enRunKind">The kind of run.</param>
  internal void SetRunKind(EnRunKind enRunKind)
  {
    _enRunKind = enRunKind;
  }

  /// <summary>
  /// Requests that the currently running execution stop at its next step boundary and report
  /// <see cref="EnGenResult.Paused"/>. Called by the API layer on the consumer's thread while
  /// the worker thread is inside <see cref="Execute"/>.
  /// </summary>
  /// <remarks>
  /// Pause has precedence over cancellation: when both a pause and a cancellation are pending
  /// at a checkpoint, the run reports <see cref="EnGenResult.Paused"/> and keeps the partial
  /// world. It never rewrites <see cref="EnGenResult.Error"/> or
  /// <see cref="EnGenResult.Finished"/>: a failed run has no work to pause, and a run that
  /// completed its final step before observing the request finishes normally.
  /// </remarks>
  /// <returns>
  /// <see langword="true"/> if a run was in flight and the request was accepted;
  /// <see langword="false"/> if no run is running (already finished, paused, cancelled,
  /// failed, or never started), so there is nothing to pause.
  /// </returns>
  internal bool TryRequestPause()
  {
    lock (_pauseGate)
    {
      // Re-check under the gate: a run that committed to a terminal state while we waited
      // for the gate can no longer observe this request, so it must not be acknowledged.
      if (_statusStore.Current.Result != EnGenResult.Running) return false;

      _pauseRequested = true;
      return true;
    }
  }

  /// <summary>
  /// Publishes a terminal status snapshot for a run that is no longer executing, applying
  /// pause precedence and consuming any pending pause request.
  /// </summary>
  /// <remarks>
  /// Must be called while holding <see cref="_pauseGate"/> semantics (this method takes it):
  /// the pause flag is rewritten and cleared together with the publish, so a request accepted
  /// by <see cref="TryRequestPause"/> always belongs to the run that publishes next, and no
  /// stale pause leaks into a later run. Paused and finished results carry this generator's
  /// world into the store, so <c>World</c> reads status and world as one unit; every other
  /// result hides the world by pairing with <see langword="null"/>.
  /// </remarks>
  /// <param name="result">Terminal result to publish.</param>
  /// <param name="error">Exception to attach when publishing <see cref="EnGenResult.Error"/>.</param>
  private void PublishTerminal(EnGenResult result, Exception? error = null)
  {
    lock (_pauseGate)
    {
      // Pause has precedence over cancellation only; Finished and Error always stand.
      if (_pauseRequested && result == EnGenResult.Cancelled) result = EnGenResult.Paused;

      _pauseRequested = false;
      var world = result is EnGenResult.Paused or EnGenResult.Finished ? _world : null;
      _statusStore.Publish(new GenerationStatus { Result = result, Error = error }, world);
    }
  }

  /// <summary>
  /// Starts or resumes generation.
  /// </summary>
  /// <remarks>
  /// A caught exception does not propagate: this method is meant to run on a background thread,
  /// where an unhandled exception would terminate the host process. Instead the failure is
  /// reported through the status store as <see cref="EnGenResult.Error"/> with the exception
  /// captured in <see cref="GenerationStatus.Error"/>.
  /// <para>
  /// Cancellation is not a failure: when <paramref name="cancellationToken"/> is cancelled, the run
  /// stops at its next checkpoint and reports <see cref="EnGenResult.Cancelled"/> with a
  /// <see langword="null"/> error instead. An explicit pause request (see
  /// <see cref="TryRequestPause"/>) has precedence over cancellation: a run with both pending
  /// reports <see cref="EnGenResult.Paused"/> and keeps its partial world.
  /// </para>
  /// </remarks>
  /// <param name="cancellationToken">
  /// Token governing this run. Checked before generation starts and then only at step
  /// boundaries - the sleep of a running placeholder step is never interrupted.
  /// </param>
  public void Execute(CancellationToken cancellationToken = default)
  {
    _statusStore.Publish(new GenerationStatus { Result = EnGenResult.Running });
    try
    {
      // First checkpoint: a token cancelled before the run got going ends it right away.
      cancellationToken.ThrowIfCancellationRequested();

      EnGenResult genResult = RunGeneration(cancellationToken);
      PublishTerminal(genResult); // Finished or Paused here.
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
      // Only our own token maps to Cancelled; an unrelated OperationCanceledException
      // still falls through to the generic handler below and reports an error.
      // A pending pause request rewrites Cancelled to Paused inside PublishTerminal.
      PublishTerminal(EnGenResult.Cancelled);
    }
    catch (Exception exception)
    {
      PublishTerminal(EnGenResult.Error, exception);
    }
  }

  /// <summary>
  /// Runs the generation passes in order: prepass, graph, then the placeholder map loop, where
  /// each map step imitates real work by sleeping. The number of map steps comes from
  /// <see cref="CnfDebug.PlaceholderSteps"/>.
  /// </summary>
  /// <param name="cancellationToken">Token checked at every step boundary.</param>
  /// <returns>Generation result. Finished when all steps are done, otherwise Paused.</returns>
  private EnGenResult RunGeneration(CancellationToken cancellationToken)
  {
    if (_config.Debug?.FailAtStart == true) throw new InvalidOperationException("Deliberate failure at start.");

    MainLoop(cancellationToken);

    // Complete generation means the level is done; otherwise the run stopped early (Step/Area
    // budget exhausted, or an explicit pause request) and can be resumed with remaining steps.
    return IsGenerationComplete() ? EnGenResult.Finished : EnGenResult.Paused;
  }

  //

  /// <summary>
  /// <para>Main loop.</para>
  /// </summary>
  /// <param name="cancellationToken">Token checked at every step boundary.</param>
  private void MainLoop(CancellationToken cancellationToken)
  {
    if (!RunPrepass(cancellationToken)) return;
    if (!RunGraph(cancellationToken)) return;
    RunMap(cancellationToken);
  }

  /// <summary>
  /// <para>Execute the prepass generator. This is considered a single step.</para>
  /// </summary>
  /// <returns>True if the run should continue, otherwise false.</returns>
  private bool RunPrepass(CancellationToken cancellationToken)
  {
    // Are we in the correct phase?
    if (_world.Phase != EnPhase.Prepass) return true;
    // Pause checkpoint: a request pending before the next step starts ends the run.
    if (_pauseRequested) return false;
    // Cancellation checkpoint: a token cancelled before the next step starts ends the run.
    cancellationToken.ThrowIfCancellationRequested();

    PrepassGenerator prepassGenerator = new PrepassGenerator(_config);
    _prepData = prepassGenerator.Create();
    _mapGenerator = new MapGenerator(_config, _prepData, _world);
    _world.Phase = EnPhase.Graph; // move to next phase
    return true;
  }

  /// <summary>
  /// <para>Execute the graph generator. This is considered a single step.</para>
  /// </summary>
  /// <returns>True if the run should continue, otherwise false.</returns>
  private bool RunGraph(CancellationToken cancellationToken)
  {
    // Are we in the correct phase?
    if (_world.Phase != EnPhase.Graph) return true;
    // Pause checkpoint: a request pending before the next step starts ends the run.
    if (_pauseRequested) return false;
    // Cancellation checkpoint: a token cancelled before the next step starts ends the run.
    cancellationToken.ThrowIfCancellationRequested();

    GraphGenerator graphGenerator = new GraphGenerator(_config, _prepData!, _world);
    bool result = graphGenerator.Execute();
    if (result) _world.Phase = EnPhase.Map; // move to next phase
    return result;
  }

  /// <summary>
  /// <para>Execute the map generator. This can and will take many steps.</para>
  /// <para>
  /// Loops while generation is not complete yet (see <see cref="IsGenerationComplete"/>) and the
  /// run kind still allows more work: <c>EnRunKind.All</c> runs until generation completes, <c>Area</c>
  /// and <c>Step</c> run at most one step per execution.
  /// </para>
  /// <para>
  /// Each step first checks for a pause request (the only pause checkpoint, see
  /// <see cref="TryRequestPause"/>), then the token (the only cancellation checkpoint), and
  /// then sleeps for <c>PlaceholderSleep / PlaceholderSteps</c> milliseconds as an imitation of
  /// the work done in a single step. The sleep itself is a plain, uninterruptible
  /// <see cref="Thread.Sleep(int)"/>; both pause and cancellation are therefore observed only
  /// after the current step completes. Completed steps are counted in
  /// <see cref="LilStats.Steps"/> of <see cref="_world"/>, which survives
  /// pausing and resuming because the API keeps this generator instance alive.
  /// </para>
  /// </summary>
  private void RunMap(CancellationToken cancellationToken)
  {
    // Are we in the correct phase?
    if (_world.Phase != EnPhase.Map) return;

    // Maximum number of steps this execution may add; real passes will budget work differently.
    var maxStepsThisRun = _enRunKind == EnRunKind.All ? int.MaxValue : 1;

    var stepsRun = 0;
    while (stepsRun < maxStepsThisRun && !IsGenerationComplete())
    {
      // Pause checkpoint: a request pending before the next step starts ends the run.
      if (_pauseRequested) return;
      // Cancellation checkpoint: a token cancelled before the next step starts ends the run.
      cancellationToken.ThrowIfCancellationRequested();

      _mapGenerator.ExecuteStep();

      stepsRun++;
    }
  }

  /// <summary>
  /// Placeholder for the real completion criterion. The real generator will use a more
  /// complex check (layout complete, all areas placed, ...) and will not know the total
  /// number of steps in advance.
  /// </summary>
  private bool IsGenerationComplete()
  {
    return _world.Stats.Steps >= _totalSteps;
  }
}
