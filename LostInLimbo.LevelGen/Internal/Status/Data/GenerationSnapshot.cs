using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Data.Status;

namespace LostInLimbo.LevelGen.Internal.Status.Data;

/// <summary>
/// Immutable pair of a status snapshot and the world that status exposes, swapped as a single
/// unit by <see cref="GenerationStatusStore"/>.
/// </summary>
/// <remarks>
/// Pairing both values in one reference is what lets a consumer read the status and the world
/// of exactly one run without locks: the two can never come from two different runs, no matter
/// how a claim, a terminal publish or a reset interleaves with the read.
/// </remarks>
/// <param name="Status">Status snapshot of the run that published this pair.</param>
/// <param name="World">Frozen copy of the world the status exposes, or <see langword="null"/> while the status
/// hides it (pristine, running, cancelled or failed runs). Never mutated after publication.</param>
internal sealed record GenerationSnapshot(GenerationStatus Status, LilWorld? World)
{
  /// <summary>Pristine instance: never ran, no world.</summary>
  public static GenerationSnapshot Default { get; } = new(GenerationStatus.Default, null);
}
