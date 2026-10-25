namespace LostInLimbo.LevelGen.Internal.Generator.Graph
{
  /// <summary>
  /// Outcome of a single generation run.
  /// </summary>
  /// <remarks>
  /// Distinguishes a failed <b>build</b> (graph/map may be partially mutated, world must be hidden)
  /// from a failed <b>validation</b> (graph/map is built, world stays available for debugging),
  /// which the caller maps to different terminal results.
  /// </remarks>
  internal enum EnGenerationOutcome
  {
    /// <summary>The graph/map was built and validated successfully.</summary>
    Success,

    /// <summary>
    /// The graph/map build reported failure. The graph/map may be partially mutated and must not be
    /// published or re-run against the existing data.
    /// </summary>
    BuildFailed,

    /// <summary>
    /// The graph/map was built successfully, but validation rejected it. The built graph is intact
    /// and can be published for debugging.
    /// </summary>
    ValidationFailed,
  }
}

