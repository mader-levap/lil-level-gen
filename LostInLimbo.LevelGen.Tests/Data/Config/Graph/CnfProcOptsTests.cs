using LostInLimbo.LevelGen.Data.Config.Graph;

namespace LostInLimbo.LevelGen.Tests.Data.Config.Graph;

/// <summary>
/// Tests the invariants of the processor-options config section.
/// </summary>
public sealed class CnfProcOptsTests
{
  /// <summary>Tests the top-level <see cref="CnfProcOpts"/> section: nulls and cascading.</summary>
  public sealed class ProcOptsTests
  {
    [Fact]
    public void Validate_WithDefaultInstance_DoesNotThrow()
    {
      // Arrange/Act: The default instance uses every section's defaults.
      CnfProcOpts.Default.Validate();
    }

    [Theory]
    [InlineData(nameof(CnfProcOpts.RelSecrets))]
    [InlineData(nameof(CnfProcOpts.NodeObjectives))]
    [InlineData(nameof(CnfProcOpts.NodeRelSize))]
    [InlineData(nameof(CnfProcOpts.NodeStyle))]
    public void Validate_WithNullSection_ThrowsArgumentExceptionNamingSection(string section)
    {
      // Arrange: Proc options with one section explicitly nulled out.
      var procOpts = section switch
      {
        nameof(CnfProcOpts.RelSecrets) => CnfProcOpts.Default with { RelSecrets = null! },
        nameof(CnfProcOpts.NodeObjectives) => CnfProcOpts.Default with { NodeObjectives = null! },
        nameof(CnfProcOpts.NodeRelSize) => CnfProcOpts.Default with { NodeRelSize = null! },
        _ => CnfProcOpts.Default with { NodeStyle = null! },
      };

      // Act/Assert: The null section is rejected naming the offending property.
      var exception = Assert.Throws<ArgumentException>(() => procOpts.Validate());
      Assert.Equal(section, exception.ParamName);
    }

    [Fact]
    public void Validate_WithInvalidNestedValue_ThrowsNamingNestedProperty()
    {
      // Arrange: Proc options whose nested section violates its own invariants.
      var procOpts = CnfProcOpts.Default with
      {
        RelSecrets = new CnfProcRelSecretsOpts { Amount = -1 },
      };

      // Act/Assert: Validation cascades into the section and names the nested property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => procOpts.Validate());
      Assert.Equal("Amount", exception.ParamName);
    }
  }

  /// <summary>Tests the per-processor config records: defaults and range rules.</summary>
  public sealed class ProcSectionTests
  {
    [Fact]
    public void RelSecrets_WithDefaultInstance_DoesNotThrow()
    {
      // Arrange/Act: The default instance uses the legacy hardcoded values.
      CnfProcRelSecretsOpts.Default.Validate();

      // Assert: Defaults match the values builders used before options were configurable.
      Assert.Equal(1, CnfProcRelSecretsOpts.Default.Amount);
      Assert.Equal(0.1f, CnfProcRelSecretsOpts.Default.ChanceSecretInSecret);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RelSecrets_WithNonNegativeAmount_DoesNotThrow(int amount)
    {
      // Arrange: Config with an allowed amount (0 disables the processor).
      var opts = new CnfProcRelSecretsOpts { Amount = amount };

      // Assert: Zero and positive amounts are both inside the allowed range.
      opts.Validate();
    }

    [Fact]
    public void RelSecrets_WithNegativeAmount_ThrowsArgumentOutOfRangeException()
    {
      // Arrange: Config with a forbidden amount.
      var opts = new CnfProcRelSecretsOpts { Amount = -1 };

      // Act/Assert: The out-of-range value is rejected naming the offending property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => opts.Validate());
      Assert.Equal("Amount", exception.ParamName);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(0.5f)]
    [InlineData(1f)]
    public void RelSecrets_WithChanceWithinUnitInterval_DoesNotThrow(float chance)
    {
      // Arrange: Config with a chance inside [0, 1].
      var opts = new CnfProcRelSecretsOpts { ChanceSecretInSecret = chance };

      // Assert: Boundary values and the midpoint are all allowed.
      opts.Validate();
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(1.1f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void RelSecrets_WithChanceOutsideUnitInterval_ThrowsArgumentOutOfRangeException(float chance)
    {
      // Arrange: Config with a forbidden chance.
      var opts = new CnfProcRelSecretsOpts { ChanceSecretInSecret = chance };

      // Act/Assert: The out-of-range value is rejected naming the offending property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => opts.Validate());
      Assert.Equal("ChanceSecretInSecret", exception.ParamName);
    }

    [Fact]
    public void NodeObjectives_WithDefaultInstance_DoesNotThrow()
    {
      // Arrange/Act: The default instance uses the legacy hardcoded values.
      CnfProcNodeObjectivesOpts.Default.Validate();

      // Assert: Defaults match the values builders used before options were configurable.
      Assert.Equal(1, CnfProcNodeObjectivesOpts.Default.GoalAmount);
      Assert.Equal(1, CnfProcNodeObjectivesOpts.Default.OptGoalAmount);
    }

    [Theory]
    [InlineData(nameof(CnfProcNodeObjectivesOpts.GoalAmount))]
    [InlineData(nameof(CnfProcNodeObjectivesOpts.OptGoalAmount))]
    public void NodeObjectives_WithNegativeAmount_ThrowsArgumentOutOfRangeException(string property)
    {
      // Arrange: Config with one forbidden amount.
      var opts = property == nameof(CnfProcNodeObjectivesOpts.GoalAmount)
        ? new CnfProcNodeObjectivesOpts { GoalAmount = -1 }
        : new CnfProcNodeObjectivesOpts { OptGoalAmount = -1 };

      // Act/Assert: The out-of-range value is rejected naming the offending property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => opts.Validate());
      Assert.Equal(property, exception.ParamName);
    }

    [Fact]
    public void NodeObjectives_WithZeroAmounts_DoesNotThrow()
    {
      // Arrange: Config disabling both phases.
      var opts = new CnfProcNodeObjectivesOpts { GoalAmount = 0, OptGoalAmount = 0 };

      // Assert: Zero is allowed and disables the processor.
      opts.Validate();
    }

    [Fact]
    public void NodeRelSize_WithDefaultInstance_DoesNotThrow()
    {
      // Arrange/Act: The default instance uses the legacy hardcoded values.
      CnfProcNodeRelSizeOpts.Default.Validate();

      // Assert: Defaults match the values builders used before options were configurable.
      Assert.Equal(3, CnfProcNodeRelSizeOpts.Default.Steps);
      Assert.Equal(EnRelSizeCurve.Geometric, CnfProcNodeRelSizeOpts.Default.Curve);
      Assert.Equal(1, CnfProcNodeRelSizeOpts.Default.MinSize);
      Assert.Equal(2f, CnfProcNodeRelSizeOpts.Default.Growth);
    }

    [Fact]
    public void NodeRelSize_WithContradictoryStepsAndBias_ThrowsArgumentException()
    {
      // Arrange: A single step leaves no variation for a positive bias.
      var opts = CnfProcNodeRelSizeOpts.Default with
      {
        Steps = 1,
        BiasRelationships = 0.5f,
      };

      // Act/Assert: The contradiction is rejected naming the step count.
      var exception = Assert.Throws<ArgumentException>(() => opts.Validate());
      Assert.Equal("Steps", exception.ParamName);
    }

    [Fact]
    public void NodeRelSize_WithUndefinedCurve_ThrowsArgumentOutOfRangeException()
    {
      // Arrange: Config carrying a curve value no member defines.
      var opts = CnfProcNodeRelSizeOpts.Default with
      {
        Curve = (EnRelSizeCurve)99,
      };

      // Act/Assert: The unknown curve is rejected naming the offending property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => opts.Validate());
      Assert.Equal("Curve", exception.ParamName);
    }

    [Fact]
    public void NodeStyle_WithDefaultInstance_DoesNotThrow()
    {
      // Arrange/Act: The default instance uses the legacy hardcoded values.
      CnfProcNodeStyleOpts.Default.Validate();

      // Assert: Defaults match the values builders used before options were configurable.
      Assert.Equal(3, CnfProcNodeStyleOpts.Default.GroupCount);
    }

    [Fact]
    public void NodeStyle_WithZeroGroupCount_ThrowsArgumentOutOfRangeException()
    {
      // Arrange: Config demanding fewer than one group.
      var opts = new CnfProcNodeStyleOpts { GroupCount = 0 };

      // Act/Assert: The out-of-range value is rejected naming the offending property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => opts.Validate());
      Assert.Equal("GroupCount", exception.ParamName);
    }
  }
}
