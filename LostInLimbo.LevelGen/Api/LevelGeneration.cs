using System;
using System.Diagnostics;
using System.Threading;
using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Other;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Internal.Generator;
using LostInLimbo.LevelGen.Internal.Status;

namespace LostInLimbo.LevelGen.Api;

/// <summary>
/// <para>Entry point for entire level generator. Serves as layer between consumer and internals. There can be only one generation running at once.</para>
/// <para>Commands like <see cref="Start"/>, <see cref="Resume"/>, <see cref="Cancel"/> and <see cref="Pause"/> are asynchronous and return immediately.
/// <see cref="Clear"/> is the one exception: when a run is in flight it pauses the run and waits (up to a timeout) for the worker to stop before clearing.</para>
/// </summary>
public static class LevelGeneration
{
  /// <summary>
  /// Single source of truth for generation status. Owned here; generators receive it
  /// and publish their transitions from the worker thread.
  /// </summary>
  private static readonly GenerationStatusStore _statusStore = new();

  /// <summary>
  /// Generator instance owned by the current or last run. Every read and write holds
  /// <see cref="RunSourceGate"/>; volatile so the pairing with the status store's snapshots
  /// stays safe against out-of-thin-air staleness on weak memory models.
  /// </summary>
  private static volatile LevelGenerator? _generator;

  /// <summary>
  /// Serializes the claim + install of a run's state and its replacement/disposal against
  /// <see cref="Cancel"/>, <see cref="Pause"/> and <see cref="Clear"/>. Every access to
  /// <see cref="_runSource"/> or <see cref="_generator"/> must hold this gate, and a claim only
  /// runs under it, so those fields always belong to the run the status currently reports - and
  /// a source is never disposed while <see cref="Cancel"/> is invoking it.
  /// </summary>
  private static readonly object RunSourceGate = new();

  /// <summary>
  /// Cancellation source of the run that currently holds the claim, or of the last run.
  /// Retired (disposed and replaced) when a new run claims, and by <see cref="Clear"/>.
  /// </summary>
  private static CancellationTokenSource? _runSource;

  /// <summary>Default upper bound <see cref="Clear"/> waits for an in-flight run to stop.</summary>
  private static readonly TimeSpan DefaultClearTimeout = TimeSpan.FromSeconds(10);

  /// <summary>Poll interval used while waiting for an in-flight run to reach a terminal state.</summary>
  private static readonly TimeSpan TerminalPollInterval = TimeSpan.FromMilliseconds(10);

  // TEST SEAMS
  //
  // Hooks used by tests to hold open otherwise unreachable interleaving windows. They are never
  // set in production (always <see langword="null"/>), tests set them before and clear them
  // after the act they synchronize, and their bodies must not call back into commands that take
  // <see cref="RunSourceGate"/> on the same thread while the claim hook holds it.

  /// <summary>
  /// Test-only hook invoked inside <see cref="Start"/> and <see cref="Resume"/> after the run was
  /// claimed but before its generator and cancellation source are installed - the window in which
  /// <see cref="Cancel"/>/<see cref="Pause"/> must not resolve a retired run's state.
  /// </summary>
  internal static volatile Action? AfterClaimTestHook;

  /// <summary>
  /// Test-only hook invoked inside <see cref="Clear(TimeSpan?)"/> after it captured the claim id
  /// and observed a running run, but before it requests the pause - the window in which a newer
  /// run may claim, which <see cref="Clear"/> then must not disturb.
  /// </summary>
  internal static volatile Action? BeforeClearPauseTestHook;

  /// <summary>
  /// Test-only hook invoked inside <see cref="World"/> after the call sampled the state it will
  /// report and before it resolves the world reference - the window in which a newer run may
  /// claim, which must not change which run's world is returned.
  /// </summary>
  internal static volatile Action? WorldSampleTestHook;

  // COMMANDS

  /// <summary>
  /// Begin level generation based on given config. Will discard all and any partial data,
  /// including the world built by any previous run.
  /// </summary>
  /// <param name="enRunKind">Kind of run.</param>
  /// <param name="config">Configuration data for level generation.</param>
  /// <param name="cancellationToken">
  /// Optional external token. When cancelled, the run stops at its next checkpoint and reports
  /// <see cref="EnGenResult.Cancelled"/>. The token is linked into a source owned by this call,
  /// so this method does not need to observe it again; pass it to <see cref="Resume"/> as well
  /// if subsequent runs should honor it too.
  /// </param>
  /// <returns>True if successfully started generation, otherwise false.</returns>
  /// <exception cref="ArgumentNullException">Config is <see langword="null"/>.</exception>
  /// <exception cref="ArgumentOutOfRangeException">A numeric config setting is out of range.</exception>
  /// <exception cref="ArgumentException">The configuration violates its own invariants.</exception>
  public static bool Start(EnRunKind enRunKind, CnfLevelGeneration config, CancellationToken cancellationToken = default)
  {
    // Validate before claiming so configuration errors cannot leave status stuck at "running".
    // This is the only validation pass; the generator trusts its caller.
    ValidateConfig(config);

    try
    {
      // The gate makes claim + install one atomic step with respect to Cancel, Pause and Clear:
      // no observer can see a claimed run whose cancellation source and generator still belong
      // to the previous run. The claim still precedes any allocation inside the gate, so a
      // refused start neither touches the active run's source nor builds a throwaway generator.
      LevelGenerator generator;
      CancellationTokenSource source;
      lock (RunSourceGate)
      {
        if (!_statusStore.TryBeginRun()) return false;

        AfterClaimTestHook?.Invoke();
        generator = new LevelGenerator(enRunKind, config, _statusStore);
        source = CreateRunSource(cancellationToken);
        InstallRunState(generator, source);
      }

      // The thread is started outside the gate so the worker (and any Cancel of the freshly
      // installed source) never waits on command bookkeeping.
      return TryRunOnNewThread(generator, source.Token);
    }
    catch (Exception exception)
    {
      // Defensive: a failure after the claim (e.g. source creation on a disposed token)
      // must not leave status stuck at "running".
      _statusStore.Publish(new GenerationStatus { Result = EnGenResult.Error, Error = exception });
      return false;
    }
  }

  /// <summary>
  /// Resume level generation, applying the given run kind to the next segment.
  /// Works only if generation is paused (run kind budget exhausted or an explicit
  /// <see cref="Pause"/> request stopped the previous run).
  /// </summary>
  /// <param name="enRunKind">Kind of run.</param>
  /// <param name="cancellationToken">
  /// Optional external token, honored exactly like in <see cref="Start"/>. A token given to a
  /// previous <see cref="Start"/> call does not carry over automatically; pass it again here if
  /// this resumed run should honor it as well.
  /// </param>
  /// <returns>
  /// True if successfully resumed generation, otherwise false. A refusal, a launch failure,
  /// or a violated internal invariant (see <see cref="EnGenResult.Error"/>) all return false;
  /// use <see cref="Status"/> to tell them apart.
  /// </returns>
  public static bool Resume(EnRunKind enRunKind, CancellationToken cancellationToken = default)
  {
    try
    {
      // Same atomic claim + install as Start (see there): while the gate is held no Cancel,
      // Pause or Clear can resolve the previous run's source or generator for this claim.
      LevelGenerator generator;
      CancellationTokenSource source;
      lock (RunSourceGate)
      {
        if (!_statusStore.TryBeginResume()) return false;

        AfterClaimTestHook?.Invoke();
        // The run is claimed, so no worker can be executing this generator yet: safe to update
        // the kind before Thread.Start releases it to the worker.
        generator = _generator ?? throw new InvalidOperationException(
          "Resume requires an existing paused generator, but none is present.");
        generator.SetRunKind(enRunKind);
        source = CreateRunSource(cancellationToken);
        InstallRunState(generator, source);
      }

      return TryRunOnNewThread(generator, source.Token);
    }
    catch (Exception exception)
    {
      // Defensive: a failure after the claim must not leave status stuck at "running".
      _statusStore.Publish(new GenerationStatus { Result = EnGenResult.Error, Error = exception });
      return false;
    }
  }

  /// <summary>
  /// Request cancellation of the run that is currently in flight.
  /// The worker stops at its next checkpoint and publishes <see cref="EnGenResult.Cancelled"/>;
  /// the partial world of a cancelled run is discarded.
  /// </summary>
  /// <remarks>
  /// Best-effort: a run that reaches a terminal state concurrently may still report
  /// <see cref="EnGenResult.Finished"/> or <see cref="EnGenResult.Paused"/>. An explicit
  /// <see cref="Pause"/> request that is already pending takes precedence over cancellation.
  /// Calling this when no run is in flight is a no-op. Cancellation of a paused run must go
  /// through a fresh <see cref="Start"/> call instead.
  /// </remarks>
  /// <returns>
  /// <see langword="true"/> if a running generation was asked to cancel;
  /// <see langword="false"/> otherwise.
  /// </returns>
  public static bool Cancel()
  {
    // Fast path: nothing to cancel outside of a running claim.
    if (_statusStore.Current.Result != EnGenResult.Running) return false;

    lock (RunSourceGate)
    {
      // Re-check under the gate: while waiting for it the run may have reached a terminal state
      // and Clear may have retired the source, so the fast-path observation can go stale.
      if (_statusStore.Current.Result != EnGenResult.Running) return false;

      if (_runSource is null) return false;

      _runSource.Cancel();
      return true;
    }
  }

  /// <summary>
  /// Request pause of the run that is currently in flight. The worker stops at its next step boundary
  /// and publishes <see cref="EnGenResult.Paused"/>; the partial world is preserved and
  /// <see cref="Resume"/> can continue generation later.
  /// </summary>
  /// <remarks>
  /// Like cancellation, the request is observed only at step boundaries: processing of a
  /// running step is never interrupted. The partial world of the in-flight step's successor
  /// is therefore consistent, same as after a Step/Area run.
  /// <para>
  /// Returns <see langword="false"/> when there is nothing to pause: no run is in flight, or
  /// the run already reached a terminal state. A request accepted while the final step was
  /// still executing may lose the race to completion; in that rare case the run reports
  /// <see cref="EnGenResult.Finished"/> despite this method returning
  /// <see langword="true"/>. A paused run never reports
  /// <see cref="EnGenResult.Cancelled"/>: pause takes precedence over cancellation.
  /// </para>
  /// </remarks>
  /// <returns>
  /// <see langword="true"/> if a running generation was asked to pause;
  /// <see langword="false"/> otherwise.
  /// </returns>
  public static bool Pause()
  {
    // Resolving the generator under the gate pairs it with the claim: a run that is claimed but
    // has not installed its state yet can no longer be acknowledged against the previous run's
    // generator, and a request can never be credited to a run that is already retired.
    lock (RunSourceGate)
    {
      return _generator?.TryRequestPause() ?? false;
    }
  }

  /// <summary>
  /// Restores the pristine never-ran state: status back to <see cref="EnGenResult.None"/> with
  /// no error, no generator, and no live cancellation source, so <see cref="World"/> returns
  /// <see langword="null"/>. A subsequent <see cref="Start"/> begins from scratch.
  /// </summary>
  /// <remarks>
  /// Unlike the other commands this one may block. When a run is in flight, the call first
  /// requests a pause (the same request <see cref="Pause"/> issues), then waits for the worker
  /// to stop at its next step boundary before clearing; the wait is bounded by
  /// <paramref name="timeout"/>. Callers that must never block can pause themselves, wait until
  /// <see cref="Status"/> reports a terminal state, and then call this method - it then never
  /// waits.
  /// <para>
  /// Designed for a single consumer: concurrent calls or calls racing a
  /// <see cref="Start"/>/<see cref="Resume"/> are resolved by the internal claims. This call
  /// captures the claim id of the run it observes and only clears while that same run still
  /// holds the claim; a run that wins a concurrent claim is neither paused nor cleared by
  /// this call, and a new run that claims after this call started makes it return
  /// <see langword="false"/>.
  /// </para>
  /// </remarks>
  /// <param name="timeout">
  /// Optional upper bound for waiting out an in-flight run. <see cref="TimeSpan.Zero"/> means
  /// do not wait at all: while a run is in flight this call then always returns
  /// <see langword="false"/> and clears nothing (the pause is still requested); only a state
  /// that is already terminal or idle is cleared. When <see langword="null"/>, a default of
  /// 10 seconds is used.
  /// </param>
  /// <returns>
  /// <see langword="true"/> if the state was cleared;
  /// <see langword="false"/> if the run was in flight when this call began and did not stop
  /// within <paramref name="timeout"/> (a <see cref="TimeSpan.Zero"/> timeout always takes
  /// this path while a run is in flight), or another run claimed the state in the meantime -
  /// in both cases nothing was cleared.
  /// </returns>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="timeout"/> is negative.</exception>
  public static bool Clear(TimeSpan? timeout = null)
  {
    if (timeout.HasValue && timeout.Value < TimeSpan.Zero)
      throw new ArgumentOutOfRangeException(nameof(timeout), timeout, "Clear timeout must not be negative.");

    var effectiveTimeout = timeout ?? DefaultClearTimeout;

    // Identify the run this call intends to clear. Everything below is conditional on this
    // id staying current: a concurrent Start or Resume bumps it and makes this call refuse
    // instead of wiping state that belongs to the newly claimed run.
    var claimId = _statusStore.ClaimId;

    // A worker only exists while the status is Running; clearing under it would let the
    // worker publish its terminal state over the fresh snapshot. Pause first, then wait
    // for the cooperative stop at the next step boundary.
    if (_statusStore.Current.Result == EnGenResult.Running)
    {
      BeforeClearPauseTestHook?.Invoke();

      // Pause only the run this call captured: the claim id is re-checked in the same gate
      // section that resolves the generator, and claims run under that gate too, so a run that
      // claimed since the capture above is never disturbed by a pause meant for another run.
      lock (RunSourceGate)
      {
        if (_statusStore.ClaimId != claimId) return false; // A newer run claimed; it must not be paused by a clear meant for an older one.

        _generator?.TryRequestPause(); // Best-effort: may return false if the run reached a terminal state in the meantime.
      }

      // Zero budget: the outcome is decided by the entry observation above, not by a re-sample
      // after the pause - a run that is in flight when this call began never counts as stopped
      // within a zero timeout, so a pause landing before the wait's first read (or a worker
      // stopping at its first checkpoint) can never flip this call into a clear.
      if (effectiveTimeout == TimeSpan.Zero) return false;

      if (!WaitForTerminal(claimId, effectiveTimeout)) return false; // Timed out, or a newer run claimed; either way nothing was cleared.
    }

    // Retire the source and the generator under the gate so neither can be observed (or the
    // source disposed) while a concurrent claim installs fresh run state.
    lock (RunSourceGate)
    {
      // The compare-and-swap closes the race where a concurrent Start or Resume claims a run
      // between the check above and this point: a run that wins the claim is never disturbed.
      if (!_statusStore.TryPublishIdle(claimId)) return false;

      var previous = _runSource;
      _runSource = null;
      _generator = null;
      previous?.Dispose();
    }

    return true;
  }

  /// <summary>
  /// Polls the status store until the run identified by <paramref name="claimId"/> is no longer
  /// <see cref="EnGenResult.Running"/>, a newer run claims the state, or the timeout elapses.
  /// </summary>
  /// <param name="claimId">Claim id captured when this wait decided which run to wait for.</param>
  /// <param name="timeout">Maximum time to wait.</param>
  /// <returns>
  /// <see langword="true"/> if the observed run reached a terminal state;
  /// <see langword="false"/> if a newer run claimed in the meantime (do not clear) or on timeout.
  /// </returns>
  private static bool WaitForTerminal(long claimId, TimeSpan timeout)
  {
    var stopwatch = Stopwatch.StartNew();
    while (true)
    {
      // A new claim means this wait no longer observes the run it was started for; waiting
      // out the new run and then clearing would wipe state that won the race.
      if (_statusStore.ClaimId != claimId) return false;

      if (_statusStore.Current.Result != EnGenResult.Running) return true;
      if (stopwatch.Elapsed >= timeout) return false;
      Thread.Sleep(TerminalPollInterval);
    }
  }

  // DATA

  /// <summary>
  /// Get status of generation.
  /// </summary>
  /// <returns>Status of generation.</returns>
  public static GenerationStatus Status()
  {
    return _statusStore.Current;
  }

  /// <summary>
  /// Get the world built by the current or last run.
  /// </summary>
  /// <remarks>
  /// The generator mutates the world in place on the worker thread while generation is
  /// <see cref="EnGenResult.Running"/>, so this method returns it only for stable states:
  /// <see cref="EnGenResult.Paused"/> (partial results of an Area or Step run) and
  /// <see cref="EnGenResult.Finished"/> (complete world). It returns <see langword="null"/>
  /// while running, after <see cref="EnGenResult.Error"/> (partial world must not be used),
  /// after <see cref="EnGenResult.Cancelled"/> (cancelled runs discard their partial world),
  /// and before the first run. Status and world are sampled as one pair, so a returned world
  /// always belongs to the status this call observed (and that snapshot's publish guarantees
  /// the final data is visible to the caller), never to a run that claimed in the meantime.
  /// </remarks>
  /// <returns>
  /// World of the current or last run when its state is stable, otherwise <see langword="null"/>.
  /// </returns>
  public static LilWorld? World()
  {
    // One atomic sample carries both the status and the world it exposes, so the two halves can
    // never come from two different runs while a concurrent claim, clear or terminal publish
    // lands between reading the status and resolving the world.
    var snapshot = _statusStore.CurrentSnapshot;
    WorldSampleTestHook?.Invoke();
    var result = snapshot.Status.Result;
    return result == EnGenResult.Paused || result == EnGenResult.Finished
      ? snapshot.World
      : null;
  }

  // OTHER

  /// <summary>
  /// Validates a generation config before its run is claimed, so a bad config cannot leave
  /// status stuck at <see cref="EnGenResult.Running"/>. The single validation pass for a run:
  /// generators receive the config already validated by this method.
  /// </summary>
  /// <param name="config">Configuration to validate. Must not be <see langword="null"/>.</param>
  /// <exception cref="ArgumentNullException">Config is <see langword="null"/>.</exception>
  /// <exception cref="ArgumentOutOfRangeException">A numeric config setting is out of range.</exception>
  /// <exception cref="ArgumentException">The configuration violates its own invariants.</exception>
  private static void ValidateConfig(CnfLevelGeneration config)
  {
    if (config is null) throw new ArgumentNullException(nameof(config));
    config.Validate();
  }

  /// <summary>
  /// Creates the cancellation source for a new run. When the caller supplied an external token,
  /// the source is linked to it, so cancelling that token cancels the run as well.
  /// </summary>
  /// <param name="externalToken">Optional caller-owned token. May be <see cref="CancellationToken.None"/>.</param>
  /// <returns>A fresh source; the caller owns it until it is installed via <see cref="InstallRunState"/>.</returns>
  private static CancellationTokenSource CreateRunSource(CancellationToken externalToken)
  {
    return externalToken.CanBeCanceled
      ? CancellationTokenSource.CreateLinkedTokenSource(externalToken)
      : new CancellationTokenSource();
  }

  /// <summary>
  /// Publishes the new run's generator and cancellation source as the current ones and retires
  /// the previous run's source, as one atomic step.
  /// </summary>
  /// <remarks>
  /// Must only be called while holding <see cref="RunSourceGate"/> and after a successful
  /// claim: the claim guarantees no worker is still using the previous source, and callers
  /// already hold the gate <see cref="Cancel"/> serializes on (the nested lock is reentrant),
  /// so the disposal below can never race an in-flight cancellation. Because the claim runs
  /// under the same gate, <see cref="_generator"/> and <see cref="_runSource"/> can never be
  /// observed together with a status that belongs to a different run - and assigning
  /// <see cref="_generator"/> there pairs with <see cref="Clear"/> nulling it under the gate.
  /// </remarks>
  /// <param name="generator">Generator of the run that holds the claim.</param>
  /// <param name="source">Cancellation source of the run that holds the claim.</param>
  private static void InstallRunState(LevelGenerator generator, CancellationTokenSource source)
  {
    lock (RunSourceGate)
    {
      var previous = _runSource;
      _runSource = source;
      _generator = generator;
      previous?.Dispose();
    }
  }

  /// <summary>
  /// Runs <see cref="LevelGenerator.Execute"/> on a dedicated background thread so that
  /// callers of commands like <see cref="Start"/> or <see cref="Resume"/> return immediately.
  /// Background thread will not keep the process alive on shutdown.
  /// </summary>
  /// <remarks>
  /// If the thread cannot be created or started after the run was already claimed, the failure
  /// is published as <see cref="EnGenResult.Error"/> with the exception captured in
  /// <see cref="GenerationStatus.Error"/> and this method returns <see langword="false"/>.
  /// Status therefore can never remain stuck at <see cref="EnGenResult.Running"/> because of
  /// a launch failure. The run's cancellation source stays installed in that case and is
  /// retired by the next run or by <see cref="Clear"/>.
  /// </remarks>
  /// <param name="generator">Generator to execute.</param>
  /// <param name="cancellationToken">Token governing the run, handed to the worker thread.</param>
  /// <returns>
  /// <see langword="true"/> if the worker thread was started;
  /// <see langword="false"/> if launching failed (status reports the error).
  /// </returns>
  private static bool TryRunOnNewThread(LevelGenerator generator, CancellationToken cancellationToken)
  {
    try
    {
      var thread = new Thread(() => generator.Execute(cancellationToken))
      {
        IsBackground = true,
        Name = "LostInLimbo.LevelGenerator",
      };
      thread.Start();
      return true;
    }
    catch (Exception exception)
    {
      _statusStore.Publish(new GenerationStatus { Result = EnGenResult.Error, Error = exception });
      return false;
    }
  }
}
