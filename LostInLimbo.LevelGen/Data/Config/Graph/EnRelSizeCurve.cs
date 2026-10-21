namespace LostInLimbo.LevelGen.Data.Config.Graph;

/// <summary>
/// Enumerates how the step values of the relative-size processor progress from the
/// configured minimum size up to the largest value.
/// </summary>
public enum EnRelSizeCurve
{
  /// <summary>
  /// Each step adds the growth value, for example MinSize=1 with Growth=2 and Steps=5
  /// gives 1, 3, 5, 7, 9.
  /// </summary>
  Linear,

  /// <summary>
  /// Each step multiplies by the growth value, for example MinSize=1 with Growth=2 and
  /// Steps=5 gives 1, 2, 4, 8, 16.
  /// </summary>
  Geometric,
}
