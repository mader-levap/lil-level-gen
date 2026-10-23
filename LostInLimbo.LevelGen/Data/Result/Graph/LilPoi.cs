using System;

namespace LostInLimbo.LevelGen.Data.Result.Graph;

/// <summary>
/// Point of interest (POI) inside a node: a marker describing what a place is for in the level design.
/// </summary>
/// <remarks>
/// Immutable once created. A node can contain multiple POIs (see <see cref="LilNode.Pois"/>).
/// Invariant (enforced by <see cref="LostInLimbo.LevelGen.Api.LevelGraphValidation"/>):
/// <see cref="Id"/> must not be <see cref="Guid.Empty"/>
/// and must be unique across the entire graph (not just within a single node), so that
/// <see cref="LilCondition"/> can reference a specific POI.
/// </remarks>
public sealed record LilPoi
{
  /// <summary>
  /// Unique identifier of this POI across the entire graph. Assigned by the generator;
  /// must not be <see cref="Guid.Empty"/>.
  /// </summary>
  public required Guid Id { get; init; }

  /// <summary>Type of this POI.</summary>
  public required EnPoiType Type { get; init; }

  /// <summary>
  /// Optional human-readable label, e.g. <c>Red Key</c>. Not used by generation logic;
  /// useful for debugging, stats and editor display.
  /// </summary>
  public string? Name { get; init; } = null;
}
