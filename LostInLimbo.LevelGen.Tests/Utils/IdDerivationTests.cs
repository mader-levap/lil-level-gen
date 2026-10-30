using LostInLimbo.LevelGen.Internal.Utils.Rng;

namespace LostInLimbo.LevelGen.Tests.Utils;

/// <summary>
/// Tests <see cref="IdDerivation"/>: determinism, salt sensitivity and identifier validity.
/// </summary>
public sealed class IdDerivationTests
{
  [Fact]
  public void Derive_WithSameInputs_ReturnsSameGuid()
  {
    // Act: Derive twice from identical seed and salt.
    var first = IdDerivation.Derive(42, 0x6E6F6465);
    var second = IdDerivation.Derive(42, 0x6E6F6465);

    // Assert: The same inputs always produce the same identifier.
    Assert.Equal(first, second);
  }

  [Fact]
  public void Derive_WithDifferentSalt_ReturnsDifferentGuid()
  {
    // Act: Derive with the same seed but different role salts.
    var first = IdDerivation.Derive(42, 1);
    var second = IdDerivation.Derive(42, 2);

    // Assert: Salts select independent identifiers.
    Assert.NotEqual(first, second);
  }

  [Fact]
  public void Derive_WithDifferentSeed_ReturnsDifferentGuid()
  {
    // Act: Derive with the same salt but different seeds.
    var first = IdDerivation.Derive(1, 42);
    var second = IdDerivation.Derive(2, 42);

    // Assert: Seeds select independent identifiers.
    Assert.NotEqual(first, second);
  }

  [Theory]
  [InlineData(0, 0)]
  [InlineData(0, 1)]
  [InlineData(1, 0)]
  [InlineData(-1, -1)]
  public void Derive_NeverReturnsGuidEmpty(long seed, long salt)
  {
    // Act: Derive an identifier for edge-case inputs, including all-zero.
    var derived = IdDerivation.Derive(seed, salt);

    // Assert: The identifier is always usable.
    Assert.NotEqual(Guid.Empty, derived);
  }
}
