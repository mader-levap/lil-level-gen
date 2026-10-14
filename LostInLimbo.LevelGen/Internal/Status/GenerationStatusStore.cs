using System.Threading;

using LostInLimbo.LevelGen.Data.Other;

namespace LostInLimbo.LevelGen.Internal.Status;

/// <summary>
/// Thread-safe single source of truth for generation status.
/// </summary>
/// <remarks>
/// Producers replace the whole <see cref="GenerationStatus"/> snapshot instead of mutating it,
/// so consumers can poll <see cref="Current"/> from any thread without locks.
/// Owned by the API layer and injected into generators; contains no knowledge of when
/// status transitions happen.
/// </remarks>
internal sealed class GenerationStatusStore
{
  /// <summary>Cached snapshot published when a run begins; reused so CAS compares references.</summary>
  private static readonly GenerationStatus RunningStatus = new() { Result = EnGenResult.Running };

  /// <summary>Latest status snapshot. Replaced as a whole, never mutated.</summary>
  private GenerationStatus _status = GenerationStatus.Default;

  /// <summary>
  /// Id of the most recent successful claim (see <see cref="TryBeginRun"/> and
  /// <see cref="TryBeginResume"/>). Bumped immediately after the claim's status CAS succeeds;
  /// a claim that loses the CAS never bumps the id.
  /// </summary>
  private long _claimId;

  /// <summary>
  /// Latest status snapshot. Safe to read from any thread at any time.
  /// </summary>
  public GenerationStatus Current => Volatile.Read(ref _status);

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
  public void Publish(GenerationStatus status)
  {
    Volatile.Write(ref _status, status);
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
      var current = Volatile.Read(ref _status);
      if (current.Result == EnGenResult.Running) return false;

      // Succeeds only if nobody else changed the snapshot since we read it.
      if (ReferenceEquals(Interlocked.CompareExchange(ref _status, RunningStatus, current), current))
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
      var current = Volatile.Read(ref _status);
      if (current.Result != EnGenResult.Paused) return false;

      // Succeeds only if nobody else changed the snapshot since we read it.
      if (ReferenceEquals(Interlocked.CompareExchange(ref _status, RunningStatus, current), current))
      {
        Interlocked.Increment(ref _claimId);
        return true;
      }
    }
  }

  /// <summary>
  /// Atomically publishes the pristine idle snapshot (<see cref="GenerationStatus.Default"/>),
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
      var current = Volatile.Read(ref _status);
      if (current.Result == EnGenResult.Running) return false;

      // Checked after the snapshot read: a completed claim updates the status before it
      // bumps the id, so its terminal state is either seen here as Running or rejected by
      // this id check - never CAS'd away.
      if (Interlocked.Read(ref _claimId) != expectedClaimId) return false;

      // Succeeds only if nobody else changed the snapshot since we read it.
      if (ReferenceEquals(Interlocked.CompareExchange(ref _status, GenerationStatus.Default, current), current))
        return true;
    }
  }
}
