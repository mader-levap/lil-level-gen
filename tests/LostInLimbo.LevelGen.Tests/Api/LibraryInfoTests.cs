using LostInLimbo.LevelGen.Api;
using System.Reflection;

namespace LostInLimbo.LevelGen.Tests.Api;

public sealed class LibraryInfoTests
{
    [Fact]
    public void Default_ReturnsExpectedName() {
      // Assert: Library returns correct name of library.
      Assert.Equal("LostInLimbo.LevelGen", LibraryInfo.Default.Name);
    }

    [Fact]
    public void Default_ReturnsVersionInSemanticFormat()
    {
      // Assert: Version format is d.d.d.
      Assert.Matches(@"^\d+\.\d+\.\d+$", LibraryInfo.Default.Version);
    }

    [Fact]
    public void Default_ReturnsAssemblyInformationalVersion() {
      // Arrange: Get version information from .csproject file.
      var expectedVersion = typeof(LibraryInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
        .InformationalVersion
        .Split('+')[0];

      // Assert: Version given by library is actually from .csproject file.
      Assert.Equal(expectedVersion, LibraryInfo.Default.Version);
    }
}
