using System;
using System.Threading;

using LostInLimbo.LevelGen.Data.Config;
using LostInLimbo.LevelGen.Data.Other;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Internal.Status;

namespace LostInLimbo.LevelGen.Internal.Generator;

/// <summary>
/// <para>Main class managing entire generation and its passes. Should be run in its own thread.</para>
/// <para>Updates status during execution, so consumer can show progress.</para>
/// </summary>
/// <remarks>
/// NOTE: right now it is placeholder that pretends to do something (sleeps).
/// Mutates <see cref="World"/> in place on the worker thread; consumers must only read it
/// after status reports <see cref="EnGenResult.Finished"/>.
/// </remarks>
internal sealed class LevelGenerator
{
  private static readonly TimeSpan PlaceholderWorkDuration = TimeSpan.FromSeconds(3);

  /// <summary>Kind of run.</summary>
  private readonly EnRunKind _enRunKind;
  /// <summary>Configuration data for level generation.</summary>
  private readonly CnfLevelGeneration _config;
  /// <summary>Shared, thread-safe status sink owned by the API layer.</summary>
  private readonly GenerationStatusStore _statusStore;

  /// <summary>
  /// Result of generation. Placeholder: not populated yet. Will be built up in place
  /// by generation passes during <see cref="Execute"/>.
  /// </summary>
  public LilWorld World { get; } = new();

  /// <summary>
  /// Prepares level generator.
  /// </summary>
  /// <param name="enRunKind">Kind of run.</param>
  /// <param name="config">Configuration data for level generation.</param>
  /// <param name="statusStore">Store receiving status snapshots during execution.</param>
  /// <exception cref="ArgumentNullException">A parameter is <see langword="null"/>.</exception>
  public LevelGenerator(EnRunKind enRunKind, CnfLevelGeneration config, GenerationStatusStore statusStore)
  {
    if (config is null) throw new ArgumentNullException(nameof(config));
    if (statusStore is null) throw new ArgumentNullException(nameof(statusStore));
    _enRunKind = enRunKind;
    _config = config;
    _statusStore = statusStore;
  }

  /// <summary>
  /// Starts or continues generation.
  /// </summary>
  /// <remarks>
  /// A caught exception does not propagate: this method is meant to run on a background thread,
  /// where an unhandled exception would terminate the host process. Instead the failure is
  /// reported through the status store as <see cref="EnGenResult.Error"/> with the exception
  /// captured in <see cref="GenerationStatus.Error"/>.
  /// </remarks>
  public void Execute()
  {
    _statusStore.Publish(new GenerationStatus { Result = EnGenResult.Running });
    try
    {
      EnGenResult GenResult = RunGeneration();
      _statusStore.Publish(new GenerationStatus { Result = GenResult }); // Finished or Paused here.
    }
    catch (Exception exception)
    {
      _statusStore.Publish(new GenerationStatus { Result = EnGenResult.Error, Error = exception });
    }
  }

  /// <summary>
  /// Placeholder for generation passes. Will be replaced by real layout, validation and
  /// placement passes using <see cref="_enRunKind"/> and <see cref="_config"/>.
  /// </summary>
  /// <returns>Generation result. Can be only Finished or Paused.</returns>
  private EnGenResult RunGeneration()
  {
    if (_config.Debug?.FailAtStart == true) throw new Exception("Deliberate failure at start.");

    Thread.Sleep(PlaceholderWorkDuration);

    // In future we will be able to finish even with run kind different than All.
    return _enRunKind == EnRunKind.All ? EnGenResult.Finished : EnGenResult.Paused;
  }
}
