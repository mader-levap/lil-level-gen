using System.Reflection;

namespace LostInLimbo.LevelGen.Api;

/// <summary>
/// Information about the level generation library.
/// </summary>
public sealed record LibraryInfo(string Name, string Version)
{
  /// <summary>Library information read from the compiled assembly.</summary>
  public static LibraryInfo Default { get; } = new(
    Name: "LostInLimbo.LevelGen",
    Version: ReadVersion());

  /// <summary>Retrieves version number from .csproj file so you need to update project version in only one place.</summary>
  /// <returns>Version string.</returns>
  private static string ReadVersion()
  {
    var informational = typeof(LibraryInfo).Assembly
      .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
      ?.InformationalVersion;

    // Strip any "+commit" build metadata if added by tooling later.
    return (informational ?? "0.0.0").Split('+')[0];
  }
}