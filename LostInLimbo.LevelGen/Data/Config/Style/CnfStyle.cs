namespace LostInLimbo.LevelGen.Data.Config.Style;

/// <summary>
/// <para>Style configuration data.</para>
/// </summary>
/// <remarks>
/// Currently placeholder.
/// </remarks>
public sealed record CnfStyle
{
  // Constants here

  #region Defaults

  /// <summary>Default instance.</summary>
  public static CnfStyle Default { get; } = new();

  #endregion

  #region Fields

  // actual fields here

  #endregion

  #region Helpers

  /// <summary>
  /// Checks that this config can be used for generation.
  /// </summary>
  public void Validate()
  {
  }

  #endregion
}