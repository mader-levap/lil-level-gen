namespace LostInLimbo.LevelGen.Internal.Generator.Graph.Processors;

/// <summary>
/// Enumerates how the step values of <see cref="NodeRelSizerOptions"/> progress
/// from <see cref="NodeRelSizerOptions.MinSize"/> up to the largest value.
/// </summary>
internal enum EnRelSizeCurve
{
  /// <summary>
  /// Each step adds <see cref="NodeRelSizerOptions.Growth"/>, for example
  /// MinSize=1 with Growth=2 and Steps=5 gives 1, 3, 5, 7, 9.
  /// </summary>
  Linear,

  /// <summary>
  /// Each step multiplies by <see cref="NodeRelSizerOptions.Growth"/>, for example
  /// MinSize=1 with Growth=2 and Steps=5 gives 1, 2, 4, 8, 16.
  /// </summary>
  Geometric,
}
