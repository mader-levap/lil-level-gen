using LostInLimbo.LevelGen.Data.Prop;

namespace LostInLimbo.LevelGen.SeedSweep.Data;

/// <summary>
/// Props of one run: which difficulty, theme mode and text seed it uses.
/// </summary>
/// <param name="Index">Absolute run index; not negative.</param>
/// <param name="Difficulty">Difficulty of the run.</param>
/// <param name="ManyThemes">Value of <see cref="PropMain.ManyThemes"/>.</param>
/// <param name="TextSeed">Text seed of the run.</param>
internal sealed record RunSpec(int Index, int Difficulty, bool ManyThemes, string TextSeed)
{
  /// <summary>
  /// Maps a run index to its props. Deterministic: the same index and options always give the
  /// same spec, which is what makes <c>dump &lt;index&gt;</c> reproduce a sweep run.
  /// </summary>
  /// <remarks>
  /// Difficulty cycles through the range: <c>DifficultyMin + index % span</c>. With
  /// <see cref="EnManyThemesMode.Both"/>, many themes is off for even cycles
  /// (<c>index / span</c>) and on for odd ones.
  /// </remarks>
  /// <param name="index">Absolute run index; not negative.</param>
  /// <param name="options">Sweep options supplying the range, theme mode and seed prefix.</param>
  /// <returns>Spec of the run.</returns>
  /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
  /// <exception cref="ArgumentNullException"><paramref name="options"/> is null.</exception>
  public static RunSpec ForIndex(int index, SweepOptions options)
  {
    if (options is null) throw new ArgumentNullException(nameof(options));
    if (index < 0) throw new ArgumentOutOfRangeException(nameof(index), index, "Run index must not be negative.");

    var span = options.DifficultySpan;
    var cycle = index / span;
    var manyThemes = options.ManyThemes switch
    {
      EnManyThemesMode.On => true,
      EnManyThemesMode.Off => false,
      _ => cycle % 2 == 1,
    };

    return new RunSpec(index, options.DifficultyMin + index % span, manyThemes, $"{options.SeedPrefix}-{index}");
  }

  /// <summary>
  /// Spec for an explicit text seed (dump of a seed from outside the sweep).
  /// </summary>
  /// <param name="options">Options with a single difficulty and theme mode on or off.</param>
  /// <returns>Spec with index -1.</returns>
  /// <exception cref="ArgumentException">Options do not hold a text seed.</exception>
  public static RunSpec ForTextSeed(SweepOptions options)
  {
    if (options is null) throw new ArgumentNullException(nameof(options));
    if (string.IsNullOrWhiteSpace(options.DumpTextSeed))
      throw new ArgumentException("Options have no text seed.", nameof(options));

    return new RunSpec(-1, options.DifficultyMin, options.ManyThemes == EnManyThemesMode.On, options.DumpTextSeed!);
  }

  /// <summary>Props that produce this run's config.</summary>
  /// <returns>New props.</returns>
  public PropMain ToProps()
  {
    return new PropMain { Difficulty = Difficulty, TextSeed = TextSeed, ManyThemes = ManyThemes };
  }
}
