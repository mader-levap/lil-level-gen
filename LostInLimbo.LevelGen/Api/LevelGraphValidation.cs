using System;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Internal.Generator.Graph.Validator;

namespace LostInLimbo.LevelGen.Api;

/// <summary>
/// Public entry point for verifying that a generated world's level graph is beatable:
/// start and exit exist, the player can reach every place, every conditioned passage
/// can be opened, and mandatory objectives stay completable across one-way passages -
/// both behind and beyond them - without softlocks.
/// </summary>
/// <remarks>
/// Only <see cref="LilWorld.Graph"/> is read; map, stats and phase are ignored. Intended for
/// tests, editor tooling and bulk verification loops over many seeds. See
/// <see cref="GraphValidationResult"/> for the result shape.
/// </remarks>
public static class LevelGraphValidation
{
  /// <summary>
  /// Validates the level graph of the given world.
  /// </summary>
  /// <param name="world">World whose graph is checked.</param>
  /// <returns>
  /// A result whose <see cref="GraphValidationResult.Success"/> is <see langword="true"/> when
  /// the graph is beatable; otherwise a failed result with the reason of the first failed check.
  /// </returns>
  /// <exception cref="ArgumentNullException"><paramref name="world"/> is <see langword="null"/>.</exception>
  public static GraphValidationResult Validate(LilWorld world)
  {
    if (world is null) throw new ArgumentNullException(nameof(world));

    // Fresh validator per call: instances hold per-call state and must not be shared.
    return new GraphValidator(world.Graph).Validate();
  }
}
