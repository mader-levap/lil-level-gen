using System;
using System.Threading;

using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Other;

namespace LostInLimbo.LevelGen.Internal.Generator;

/// <summary>
/// <para>Main class managing entire generation and its passes. Should be run in its own thread.</para>
/// </summary>
/// <remarks>
/// NOTE: right now it is placeholder that pretends to do something (sleeps).
/// </remarks>
public class LevelGenerator
{
  private static readonly TimeSpan PlaceholderWorkDuration = TimeSpan.FromSeconds(3);

  /// <summary>Kind of run.</summary>
  private readonly EnRunKind _enRunKind;
  /// <summary>Configuration data for level generation.</summary>
  private readonly CnfLevelGeneration _config;

  /// <summary>
  /// Prepares level generator.
  /// </summary>
  /// <param name="enRunKind">Kind of run.</param>
  /// <param name="config">Configuration data for level generation.</param>
  public LevelGenerator(EnRunKind enRunKind, CnfLevelGeneration config)
  {
    if (config is null) throw new ArgumentNullException(nameof(config));
    _enRunKind = enRunKind;
    _config = config;
  }

  /// <summary>
  /// Starts or continues generation.
  /// </summary>
  public void Execute()
  {
    Thread.Sleep(PlaceholderWorkDuration);
  }
}