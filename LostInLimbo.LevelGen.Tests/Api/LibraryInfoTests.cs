using LostInLimbo.LevelGen.Api;
using System.Reflection;

namespace LostInLimbo.LevelGen.Tests.Api;

public sealed class LibraryInfoTests
{
    [Fact]
    public void Name_ReturnsExpectedName() {
      // Assert: Library returns correct name of library.
      Assert.Equal("LostInLimbo.LevelGen", LibraryInfo.Name);
    }

    [Fact]
    public void Version_ReturnsVersionInSemanticFormat()
    {
      // Assert: Version format is d.d.d.
      Assert.Matches(@"^\d+\.\d+\.\d+$", LibraryInfo.Version);
    }

    [Fact]
    public void Version_ReturnsAssemblyInformationalVersion() {
      // Arrange: Get version information from .csproj file.
      var expectedVersion = typeof(LibraryInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
        .InformationalVersion
        .Split('+')[0];

      // Assert: Version given by library is actually from .csproj file.
      Assert.Equal(expectedVersion, LibraryInfo.Version);
    }
}
