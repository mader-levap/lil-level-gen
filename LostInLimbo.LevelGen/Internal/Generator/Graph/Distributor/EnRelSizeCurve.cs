namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Distributor;

/// <summary>
/// Enumerates how the step values of <see cref="RelSizeDistributionOptions"/> progress
/// from <see cref="RelSizeDistributionOptions.MinSize"/> up to the largest value.
/// </summary>
internal enum EnRelSizeCurve
{
  /// <summary>
  /// Each step adds <see cref="RelSizeDistributionOptions.Growth"/>, for example
  /// MinSize=1 with Growth=2 and Steps=5 gives 1, 3, 5, 7, 9.
  /// </summary>
  Linear,

  /// <summary>
  /// Each step multiplies by <see cref="RelSizeDistributionOptions.Growth"/>, for example
  /// MinSize=1 with Growth=2 and Steps=5 gives 1, 2, 4, 8, 16.
  /// </summary>
  Geometric,
}
