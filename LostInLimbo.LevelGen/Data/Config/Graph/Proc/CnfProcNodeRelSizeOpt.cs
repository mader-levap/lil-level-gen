using System;

namespace LostInLimbo.LevelGen.Data.Config.Graph.Proc;

/// <summary>
/// <para>
/// Configuration of the relative-size processor (<c>ProcNodeRelSize</c>): how many
/// relative-size steps exist, how their values progress, how strongly one step dominates
/// the base distribution and how strongly relationships and POIs push nodes toward larger
/// steps.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// Unlike the soft constraints of <see cref="CnfGraph"/>, these are hard constraints:
/// <see cref="Validate"/> throws when the parameters are out of range. With a single
/// step there is no variation, so <see cref="BiasRelationships"/> and <see cref="BiasPois"/>
/// have no effect and are ignored.
/// </para>
/// <para>
/// Zero-valued behavior: <see cref="Skew"/> 0 makes every step equally probable,
/// <see cref="BiasRelationships"/> and <see cref="BiasPois"/> 0 turn the respective bias
/// off. <see cref="Growth"/> is only relevant when <see cref="Steps"/> is greater than 1.
/// </para>
/// </remarks>
public sealed record CnfProcNodeRelSizeOpt
{
  /// <summary>
  /// Default instance: three geometric steps (1, 2, 4), mild skew and mild bias toward
  /// well-connected POI carriers.
  /// </summary>
  public static CnfProcNodeRelSizeOpt Default { get; } = new()
  {
    Steps = 3,
    Skew = 0.3f,
    Curve = EnRelSizeCurve.Geometric,
    MinSize = 1,
    Growth = 2f,
    BiasRelationships = 0.6f,
    BiasPois = 0.6f,
  };

  /// <summary>Number of distinct relative sizes to distribute; 1 disables variation.</summary>
  public int Steps { get; init; } = 3;

  /// <summary>
  /// <para>
  /// Concentration of the base distribution around one dominant step, which is rolled per
  /// graph from the seed. 0 means every step is equally probable, 1 means every node
  /// samples the single dominant step (unless bias tilts it upward), values in between
  /// mix the two.
  /// </para>
  /// <para>Must be a finite value within [0, 1].</para>
  /// </summary>
  public float Skew { get; init; } = 0.3f;

  /// <summary>Progression curve of the step values; see <see cref="EnRelSizeCurve"/>.</summary>
  public EnRelSizeCurve Curve { get; init; } = EnRelSizeCurve.Geometric;

  /// <summary>Smallest step value (the first one); must be at least 1.</summary>
  public int MinSize { get; init; } = 1;

  /// <summary>
  /// Additive increment for <see cref="EnRelSizeCurve.Linear"/>, multiplier for
  /// <see cref="EnRelSizeCurve.Geometric"/>. Must be finite and non-negative; whether it
  /// produces strictly increasing integer values is checked per configuration. Ignored
  /// when <see cref="Steps"/> is 1.
  /// </summary>
  public float Growth { get; init; } = 2f;

  /// <summary>
  /// How strongly nodes with many relationships are pulled toward larger steps, scaled by
  /// the node's min-max normalized relationship count. 0 turns the bias off; higher
  /// values tilt harder. Must be finite and non-negative.
  /// </summary>
  public float BiasRelationships { get; init; } = 0.6f;

  /// <summary>
  /// How strongly nodes with POIs are pulled toward larger steps, scaled by the node's
  /// min-max normalized POI count - a node with several POIs pulls harder than one with a
  /// single POI. 0 turns the bias off. Must be finite and non-negative.
  /// </summary>
  public float BiasPois { get; init; } = 0.6f;

  /// <summary>
  /// Builds the ascending step values this configuration distributes, for example
  /// 1, 2, 4 for the default geometric settings.
  /// </summary>
  /// <returns>Step values from smallest to largest; a single value when <see cref="Steps"/> is 1.</returns>
  /// <exception cref="ArgumentOutOfRangeException">Options are out of range; see <see cref="Validate"/>.</exception>
  /// <exception cref="ArgumentException">Options contradict each other; see <see cref="Validate"/>.</exception>
  public int[] Prepare()
  {
    Validate();
    return ComputeStepValues();
  }

  // Helpers.

  /// <summary>
  /// Checks that this config can be used for generation.
  /// </summary>
  /// <exception cref="ArgumentOutOfRangeException">
  /// Some parameter has an invalid value.
  /// </exception>
  /// <exception cref="ArgumentException">
  /// The parameters contradict each other.
  /// </exception>
  public void Validate()
  {
    if (!Enum.IsDefined(typeof(EnRelSizeCurve), Curve))
      throw new ArgumentOutOfRangeException(nameof(Curve), Curve, "Unknown rel-size curve.");
    if (Steps < 1)
      throw new ArgumentOutOfRangeException(nameof(Steps), Steps, "Steps must be at least 1.");
    if (!float.IsFinite(Skew) || Skew < 0f || Skew > 1f)
      throw new ArgumentOutOfRangeException(nameof(Skew), Skew, "Skew must be a finite value within [0, 1].");
    if (MinSize < 1)
      throw new ArgumentOutOfRangeException(nameof(MinSize), MinSize, "MinSize must be at least 1.");
    if (!float.IsFinite(BiasRelationships) || BiasRelationships < 0f)
      throw new ArgumentOutOfRangeException(nameof(BiasRelationships), BiasRelationships, "BiasRelationships must be a finite value of at least 0.");
    if (!float.IsFinite(BiasPois) || BiasPois < 0f)
      throw new ArgumentOutOfRangeException(nameof(BiasPois), BiasPois, "BiasPois must be a finite value of at least 0.");

    // Growth only matters when there is more than one step to derive.
    if (Steps == 1) return;
    if (!float.IsFinite(Growth) || Growth < 0f)
      throw new ArgumentOutOfRangeException(nameof(Growth), Growth, "Growth must be a finite value of at least 0.");

    var raw = ComputeRawStepValues();
    for (var i = 0; i < raw.Length; i++)
    {
      // Rejects both non-finite results (overflow) and values that rounding would push out of Int32 range.
      if (!float.IsFinite(raw[i]) || raw[i] > MaxStepValue)
        throw new ArgumentOutOfRangeException(nameof(Growth), Growth, "Growth produces step values outside the Int32 range.");
    }

    // Rounding can collapse or reverse nearby values, so strictly increasing is checked on the final integers, not the raw floats.
    var values = ComputeStepValues();
    for (var i = 1; i < values.Length; i++)
    {
      if (values[i] <= values[i - 1])
        throw new ArgumentException($"Curve {Curve} with Growth {Growth} does not produce strictly increasing step values; raise Growth or lower Steps.", nameof(Growth));
    }
  }

  //
  // Internals
  //

  /// <summary>
  /// Largest float strictly below 2^31: rounding it (and anything below) can never leave
  /// <see cref="int"/> range, while any float at or above 2^31 could.
  /// </summary>
  private const float MaxStepValue = 2147483520f;

  /// <summary>
  /// Computes the raw (unrounded) step values by iterating the curve from
  /// <see cref="MinSize"/>; pure float arithmetic, so results are platform-stable.
  /// </summary>
  /// <returns>Raw step values; only valid to call after <see cref="Steps"/> was checked.</returns>
  private float[] ComputeRawStepValues()
  {
    var raw = new float[Steps];
    raw[0] = MinSize;
    for (var i = 1; i < Steps; i++)
    {
      raw[i] = Curve switch
      {
        EnRelSizeCurve.Linear => raw[i - 1] + Growth,
        _ => raw[i - 1] * Growth,
      };
    }

    return raw;
  }

  /// <summary>
  /// Rounds the raw step values into the integers the distributor assigns.
  /// </summary>
  /// <returns>Rounded step values; only valid to call after <see cref="Validate"/> passed.</returns>
  private int[] ComputeStepValues()
  {
    var raw = ComputeRawStepValues();
    var values = new int[Steps];
    for (var i = 0; i < Steps; i++)
    {
      values[i] = (int)MathF.Round(raw[i]);
    }

    return values;
  }
}
