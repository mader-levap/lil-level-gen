using LostInLimbo.LevelGen.Api;
using System.Reflection;

namespace LostInLimbo.LevelGen.Tests.Api;

public sealed class LibraryInfoTests
{
    [Fact]
    public void Name_ReturnsExpectedName() {
      // Assert: Library returns the correct name of the library.
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
      // Arrange: Read the informational version attribute from the compiled assembly.
      var expectedVersion = typeof(LibraryInfo).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!
        .InformationalVersion
        .Split('+')[0];

      // Assert: The version given by the library matches the assembly's informational version.
      Assert.Equal(expectedVersion, LibraryInfo.Version);
    }
}
