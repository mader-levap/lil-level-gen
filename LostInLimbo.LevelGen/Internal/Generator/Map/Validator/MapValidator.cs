using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Config.Graph.Validator;
using LostInLimbo.LevelGen.Data.Result.Map;

namespace LostInLimbo.LevelGen.Internal.Generator.Map.Validator;

/// <summary>
/// Verifies that a level map data is consistent and correctly filled.
/// </summary>
/// <remarks>
/// <para>Currently PLACEHOLDER.</para>
/// </remarks>
/// <param name="map">Map to verify.</param>
/// <param name="options">Validator options.</param>
internal sealed class MapValidator(LilMap? map, CnfValidMapOpt options)
{
  /// <summary>Map to verify.</summary>
  private LilMap? _map = map;
  /// <summary>Validator options.</summary>
  private CnfValidMapOpt _options = options;

  /// <summary>
  /// Validates the given level map.
  /// </summary>
  /// <returns>
  /// A result whose <see cref="LilResult.Success"/> is <see langword="true"/> when the map is valid
  /// or validation is skipped; otherwise a failed result with the reason of the first failed check.
  /// </returns>
  public LilResult Validate()
  {
    if (!_options.Enabled) return LilResult.Succeed(); // Validation skipped.

    // TODO: CURRENTLY PLACEHOLDER

    return LilResult.Succeed(); // Success: All checks passed.
  }
}