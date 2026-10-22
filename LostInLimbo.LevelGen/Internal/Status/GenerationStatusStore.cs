using System.Threading;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Internal.Status.Data;

namespace LostInLimbo.LevelGen.Internal.Status;

/// <summary>
/// Thread-safe single source of truth for generation status and the world it exposes.
/// </summary>
/// <remarks>
/// Producers replace the whole <see cref="GenerationSnapshot"/> (status + world) instead of
/// mutating it, so consumers can poll <see cref="Current"/> or <see cref="CurrentSnapshot"/>
/// from any thread without locks and always observe a pair that belongs to a single run.
/// Owned by the API layer and injected into generators; contains no knowledge of when
/// status transitions happen.
/// </remarks>
internal sealed class GenerationStatusStore
{
  /// <summary>Cached status published when a run begins; reused so CAS compares references.</summary>
  private static readonly GenerationStatus RunningStatus = new() { Result = EnGenResult.Running };

  /// <summary>Cached snapshot published when a run begins; a running run never exposes a world.</summary>
  private static readonly GenerationSnapshot RunningSnapshot = new(RunningStatus, null);

  /// <summary>Latest status snapshot. Replaced as a whole, never mutated.</summary>
  private GenerationSnapshot _snapshot = GenerationSnapshot.Default;

  /// <summary>
  /// Id of the most recent successful claim (see <see cref="TryBeginRun"/> and
  /// <see cref="TryBeginResume"/>). Bumped immediately after the claim's status CAS succeeds;
  /// a claim that loses the CAS never bumps the id.
  /// </summary>
  private long _claimId;

  /// <summary>
  /// Latest status snapshot. Safe to read from any thread at any time.
  /// </summary>
  public GenerationStatus Current => Volatile.Read(ref _snapshot).Status;

  /// <summary>
  /// Latest status and world snapshot, read as one unit. Safe to read from any thread.
  /// </summary>
  /// <remarks>
  /// Use this instead of reading <see cref="Current"/> and resolving a world separately:
  /// only the pair guarantees that both halves describe the same run, even while a concurrent
  /// claim installs a new generator or the worker publishes a terminal state.
  /// </remarks>
  public GenerationSnapshot CurrentSnapshot => Volatile.Read(ref _snapshot);

  /// <summary>
  /// Id of the run that holds (or last held) the claim. Safe to read from any thread.
  /// </summary>
  /// <remarks>
  /// Lets callers such as clearing the API state attribute a status observation to one
  /// specific run: a concurrent claim bumps the id, which invalidates any decision
  /// captured under the previous id (fail-closed: the stale caller refuses to act).
  /// </remarks>
  public long ClaimId => Interlocked.Read(ref _claimId);

  /// <summary>
  /// Replaces the whole status snapshot.
  /// </summary>
  /// <param name="status">New snapshot.</param>
  /// <param name="world">
  /// World to pair with the status, or <see langword="null"/> (default) when the status must
  /// not expose one. Terminal publishes of a paused or finished run pass their world here so
  /// consumers can read status and world as one unit.
  /// </param>
  public void Publish(GenerationStatus status, LilWorld? world = null)
  {
    Volatile.Write(ref _snapshot, new GenerationSnapshot(status, world));
  }

  /// <summary>
  /// Atomically claims the right to start a run.
  /// </summary>
  /// <returns>
  /// <see langword="true"/> if the store was idle and is now marked as running;
  /// <see langword="false"/> if a run is already in progress.
  /// </returns>
  public bool TryBeginRun()
  {
    while (true)
    {
      var current = Volatile.Read(ref _snapshot);
      if (current.Status.Result == EnGenResult.Running) return false;

      // Succeeds only if nobody else changed the snapshot since we read it.
      if (ReferenceEquals(Interlocked.CompareExchange(ref _snapshot, RunningSnapshot, current), current))
      {
        Interlocked.Increment(ref _claimId);
        return true;
      }
    }
  }

  /// <summary>
  /// Atomically claims the right to resume a paused run.
  /// </summary>
  /// <remarks>
  /// Stronger than <see cref="TryBeginRun"/>: the claim only succeeds when the current snapshot
  /// is <see cref="EnGenResult.Paused"/>, so a caller that checked for "paused" earlier cannot
  /// claim a state that changed in between.
  /// </remarks>
  /// <returns>
  /// <see langword="true"/> if the run was paused and is now marked as running;
  /// <see langword="false"/> otherwise.
  /// </returns>
  public bool TryBeginResume()
  {
    while (true)
    {
      var current = Volatile.Read(ref _snapshot);
      if (current.Status.Result != EnGenResult.Paused) return false;

      // Succeeds only if nobody else changed the snapshot since we read it.
      if (ReferenceEquals(Interlocked.CompareExchange(ref _snapshot, RunningSnapshot, current), current))
      {
        Interlocked.Increment(ref _claimId);
        return true;
      }
    }
  }

  /// <summary>
  /// Atomically publishes the pristine idle snapshot (<see cref="GenerationSnapshot.Default"/>),
  /// but only while the run identified by <paramref name="expectedClaimId"/> still holds the
  /// claim and no run is in flight.
  /// </summary>
  /// <remarks>
  /// Used by clearing the API state. Two conditions must hold, each closing a race:
  /// <list type="bullet">
  /// <item>The status must not be <see cref="EnGenResult.Running"/>, so a run that is
  /// currently executing is never overwritten.</item>
  /// <item>The claim id must be the one the caller observed before deciding to clear, so a
  /// run that won a concurrent claim (and possibly already reached a terminal state of its
  /// own) is never wiped by a reset meant for an earlier run.</item>
  /// </list>
  /// The order of checks matters: the snapshot is read first and the claim id is checked
  /// against it afterwards, so an entire claim (which updates the status before bumping the
  /// id) cannot fit between the two checks undetected. The compare-and-swap then pins the
  /// exact snapshot that was checked, so a claim landing after the checks causes a refusal
  /// instead of an overwrite.
  /// </remarks>
  /// <param name="expectedClaimId">Claim id captured by the caller when it inspected the state.</param>
  /// <returns>
  /// <see langword="true"/> if the store was reset to idle;
  /// <see langword="false"/> if a run is in progress or another run claimed in the meantime.
  /// </returns>
  public bool TryPublishIdle(long expectedClaimId)
  {
    while (true)
    {
      var current = Volatile.Read(ref _snapshot);
      if (current.Status.Result == EnGenResult.Running) return false;

      // Checked after the snapshot read: a completed claim updates the status before it
      // bumps the id, so its state is either seen here as Running or, once terminal, rejected
      // by this id check - never CAS'd away.
      if (Interlocked.Read(ref _claimId) != expectedClaimId) return false;

      // Succeeds only if nobody else changed the snapshot since we read it.
      if (ReferenceEquals(Interlocked.CompareExchange(ref _snapshot, GenerationSnapshot.Default, current), current))
        return true;
    }
  }
}
