using LostInLimbo.LevelGen.Data.Prop;

namespace LostInLimbo.LevelGen.Tests.Prop.Resolvers.Graph;

/// <summary>
/// Expected inclusive upper bounds of the graph resolver's connectivity and complexity draws.
/// Deliberately an independent copy of the documented formula, so a silent change of the
/// resolver's scaling breaks the tests.
/// </summary>
internal static class GraphResolverExpect
{
  /// <summary>
  /// Largest connectivity for a size and difficulty: <c>ceil(sqrt(size) * (1 + 2 * t))</c>
  /// with <c>t = difficulty / MaxDifficulty</c>.
  /// </summary>
  public static int MaxConnectivity(int size, int difficulty)
  {
    return SqrtSizeBound(size, difficulty, 1.0, 2.0);
  }

  /// <summary>
  /// Largest complexity for a size and difficulty: <c>ceil(sqrt(size) * (0.5 + 1.5 * t))</c>,
  /// capped at <c>size - 1</c> (one obstacle per cut at most).
  /// </summary>
  public static int MaxComplexity(int size, int difficulty)
  {
    return Math.Min(Math.Max(0, size - 1), SqrtSizeBound(size, difficulty, 0.5, 1.5));
  }

  /// <summary>
  /// Largest amount of secret nodes for a size and difficulty:
  /// <c>ceil(sqrt(size) * (0.5 + 1.0 * t))</c>.
  /// </summary>
  public static int MaxSecretAmount(int size, int difficulty)
  {
    return SqrtSizeBound(size, difficulty, 0.5, 1.0);
  }

  /// <summary>
  /// Largest amount of mandatory goals for a size and difficulty:
  /// <c>ceil(sqrt(size) * (0.5 + 1.0 * t))</c>, at least 1.
  /// </summary>
  public static int MaxGoalAmount(int size, int difficulty)
  {
    return Math.Max(1, SqrtSizeBound(size, difficulty, 0.5, 1.0));
  }

  /// <summary>
  /// Largest amount of optional goals for a size: <c>ceil(sqrt(size))</c>; difficulty has no
  /// effect.
  /// </summary>
  public static int MaxOptGoalAmount(int size, int difficulty)
  {
    return SqrtSizeBound(size, difficulty, 1.0, 0.0);
  }

  /// <summary>Square-root size bound scaled by difficulty.</summary>
  private static int SqrtSizeBound(int size, int difficulty, double baseFactor, double difficultyFactor)
  {
    var t = (double)difficulty / PropMain.MaxDifficulty;
    return (int)Math.Ceiling(Math.Sqrt(size) * (baseFactor + difficultyFactor * t));
  }
}
