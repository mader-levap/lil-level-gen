using LostInLimbo.LevelGen.Data.Config;

namespace LostInLimbo.LevelGen.Tests.Data.Config;

public sealed class LevelSizeTests
{
  [Fact]
  public void Default_ReturnsThreeByThreeByThreeChunks()
  {
    // Act: Read the shared default instance.
    var size = LevelSize.Default;

    // Assert: The default is 3x3x3 chunks.
    Assert.Equal(3, size.X);
    Assert.Equal(3, size.Y);
    Assert.Equal(3, size.Z);
  }

  [Fact]
  public void Validate_Default_DoesNotThrow()
  {
    // Arrange: The default size.
    var size = LevelSize.Default;

    // Act/Assert: The default size is valid (no exception is thrown).
    size.Validate();
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  public void Validate_WithInvalidX_ThrowsArgumentOutOfRangeException(int x)
  {
    // Arrange: A size that is invalid along X only.
    var size = new LevelSize { X = x, Z = 3 };

    // Act/Assert: Validation rejects it.
    Assert.Throws<ArgumentOutOfRangeException>(() => size.Validate());
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  public void Validate_WithInvalidY_ThrowsArgumentOutOfRangeException(int y)
  {
    // Arrange: A size that is invalid along Y only.
    var size = new LevelSize { X = 3, Y = y, Z = 3 };

    // Act/Assert: Validation rejects it.
    Assert.Throws<ArgumentOutOfRangeException>(() => size.Validate());
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  public void Validate_WithInvalidZ_ThrowsArgumentOutOfRangeException(int z)
  {
    // Arrange: A size that is invalid along Z only.
    var size = new LevelSize { X = 3, Z = z };

    // Act/Assert: Validation rejects it.
    Assert.Throws<ArgumentOutOfRangeException>(() => size.Validate());
  }
}
