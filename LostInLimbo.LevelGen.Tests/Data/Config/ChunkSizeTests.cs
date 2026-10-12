using LostInLimbo.LevelGen.Data.Config;

namespace LostInLimbo.LevelGen.Tests.Data.Config;

public sealed class ChunkSizeTests
{
  [Fact]
  public void Default_ReturnsSixteenBySixteenBySixteenCells()
  {
    // Act: Read the shared default instance.
    var size = ChunkSize.Default;

    // Assert: The default is 16x16x16 cells.
    Assert.Equal(16, size.X);
    Assert.Equal(16, size.Y);
    Assert.Equal(16, size.Z);
  }

  [Fact]
  public void Validate_Default_DoesNotThrow()
  {
    // Arrange: The default size.
    var size = ChunkSize.Default;

    // Act/Assert: The default size is valid (no exception is thrown).
    size.Validate();
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  public void Validate_WithInvalidX_ThrowsArgumentOutOfRangeException(int x)
  {
    // Arrange: A size that is invalid along X only.
    var size = new ChunkSize { X = x, Z = 16 };

    // Act/Assert: Validation rejects it.
    Assert.Throws<ArgumentOutOfRangeException>(() => size.Validate());
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  public void Validate_WithInvalidY_ThrowsArgumentOutOfRangeException(int y)
  {
    // Arrange: A size that is invalid along Y only.
    var size = new ChunkSize { X = 16, Y = y, Z = 16 };

    // Act/Assert: Validation rejects it.
    Assert.Throws<ArgumentOutOfRangeException>(() => size.Validate());
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-1)]
  public void Validate_WithInvalidZ_ThrowsArgumentOutOfRangeException(int z)
  {
    // Arrange: A size that is invalid along Z only.
    var size = new ChunkSize { X = 16, Z = z };

    // Act/Assert: Validation rejects it.
    Assert.Throws<ArgumentOutOfRangeException>(() => size.Validate());
  }
}
