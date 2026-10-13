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
  /// Latest status snapshot. Safe to read from any thread at any time.
  /// </summary>
  public GenerationStatus Current => Volatile.Read(ref _status);

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
        return true;
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
        return true;
    }
  }
}
