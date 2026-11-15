using LostInLimbo.LevelGen.Api;
using LostInLimbo.LevelGen.Data.Result;
using LostInLimbo.LevelGen.Data.Status;

namespace LostInLimbo.LevelGen.SeedSweep.Data;

/// <summary>Outcome of one generation through <see cref="LevelGenSession"/>.</summary>
/// <param name="Status">Final session status; <see cref="EnGenResult.Paused"/> on success.</param>
/// <param name="World">Generated world; null when generation did not succeed.</param>
/// <param name="TimedOut">True when the run was cancelled for exceeding the timeout.</param>
internal sealed record GenerationOutcome(GenerationStatus Status, LilWorld? World, bool TimedOut);
