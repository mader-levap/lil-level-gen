using LostInLimbo.LevelGen.SeedSweep.Data;

namespace LostInLimbo.LevelGen.SeedSweep.Export;

/// <summary>
/// Median generation time of runs grouped by builder and Size ("peers"), used to tell a heavy
/// level from an outlier.
/// </summary>
internal sealed class PeerMedians
{
  /// <summary>Median and run count per (builder, Size).</summary>
  private readonly Dictionary<(string Builder, int Size), (double Median, int Count)> _groups;

  /// <summary>Wraps precomputed groups.</summary>
  private PeerMedians(Dictionary<(string Builder, int Size), (double Median, int Count)> groups)
  {
    _groups = groups;
  }

  /// <summary>Computes the medians of <paramref name="runs"/>.</summary>
  /// <param name="runs">Generated runs.</param>
  /// <returns>Peer medians.</returns>
  public static PeerMedians From(IEnumerable<RunRecord> runs)
  {
    if (runs is null) throw new ArgumentNullException(nameof(runs));

    var groups = runs
      .GroupBy(r => (r.Builder, r.Size))
      .ToDictionary(g => g.Key, g =>
      {
        var sorted = g.Select(r => r.GenerationMs).OrderBy(ms => ms).ToList();
        return (sorted[sorted.Count / 2], sorted.Count);
      });
    return new PeerMedians(groups);
  }

  /// <summary>Median generation time and run count of the peers of <paramref name="run"/> (itself included).</summary>
  /// <param name="run">Run whose builder and Size select the group.</param>
  /// <param name="median">Median generation time in ms.</param>
  /// <param name="count">Number of runs in the group.</param>
  /// <returns>False when the group is unknown.</returns>
  public bool TryGet(RunRecord run, out double median, out int count)
  {
    if (_groups.TryGetValue((run.Builder, run.Size), out var group))
    {
      (median, count) = group;
      return true;
    }

    (median, count) = (0, 0);
    return false;
  }
}
