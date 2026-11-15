using LostInLimbo.LevelGen.Data.Prop;

namespace LostInLimbo.LevelGen.SeedSweep.Data;

/// <summary>How <see cref="PropMain.ManyThemes"/> is set per run.</summary>
internal enum EnManyThemesMode
{
  /// <summary>Alternates per difficulty cycle: off for the first cycle, on for the second, and so on.</summary>
  Both,
  /// <summary>Always on.</summary>
  On,
  /// <summary>Always off.</summary>
  Off,
}
