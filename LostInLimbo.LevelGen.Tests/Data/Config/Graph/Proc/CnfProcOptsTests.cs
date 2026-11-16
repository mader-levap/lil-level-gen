using LostInLimbo.LevelGen.Data.Config.Graph;
using LostInLimbo.LevelGen.Data.Config.Graph.Proc;

namespace LostInLimbo.LevelGen.Tests.Data.Config.Graph.Proc;

/// <summary>
/// Tests the invariants of the processor-options config section.
/// </summary>
public sealed class CnfProcOptsTests
{
  /// <summary>Tests the top-level <see cref="CnfProcOpt"/> section: nulls and cascading.</summary>
  public sealed class ProcOptsTests
  {
    [Fact]
    public void Validate_WithDefaultInstance_DoesNotThrow()
    {
      // Arrange/Act: The default instance uses every section's defaults.
      CnfProcOpt.Default.Validate();
    }

    [Theory]
    [InlineData(nameof(CnfProcOpt.RelSecrets))]
    [InlineData(nameof(CnfProcOpt.NodeObjectives))]
    [InlineData(nameof(CnfProcOpt.ObstaclePlacer))]
    [InlineData(nameof(CnfProcOpt.NodeRelSize))]
    [InlineData(nameof(CnfProcOpt.NodeStyle))]
    public void Validate_WithNullSection_ThrowsArgumentExceptionNamingSection(string section)
    {
      // Arrange: Proc options with one section explicitly nulled out.
      var procOpts = section switch
      {
        nameof(CnfProcOpt.RelSecrets) => CnfProcOpt.Default with { RelSecrets = null! },
        nameof(CnfProcOpt.NodeObjectives) => CnfProcOpt.Default with { NodeObjectives = null! },
        nameof(CnfProcOpt.ObstaclePlacer) => CnfProcOpt.Default with { ObstaclePlacer = null! },
        nameof(CnfProcOpt.NodeRelSize) => CnfProcOpt.Default with { NodeRelSize = null! },
        _ => CnfProcOpt.Default with { NodeStyle = null! },
      };

      // Act/Assert: The null section is rejected naming the offending property.
      var exception = Assert.Throws<ArgumentException>(() => procOpts.Validate());
      Assert.Equal(section, exception.ParamName);
    }

    [Fact]
    public void Validate_WithInvalidNestedValue_ThrowsNamingNestedProperty()
    {
      // Arrange: Proc options whose nested section violates its own invariants.
      var procOpts = CnfProcOpt.Default with
      {
        RelSecrets = new CnfProcRelSecretsOpt { Amount = -1 },
      };

      // Act/Assert: Validation cascades into the section and names the nested property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => procOpts.Validate());
      Assert.Equal("Amount", exception.ParamName);
    }
  }

  /// <summary>Tests the per-processor config records: defaults and range rules.</summary>
  public sealed class ProcSectionTests
  {
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RelSecrets_WithNonNegativeAmount_DoesNotThrow(int amount)
    {
      // Arrange: Config with an allowed amount (0 disables the processor).
      var opts = new CnfProcRelSecretsOpt { Amount = amount };

      // Assert: Zero and positive amounts are both inside the allowed range.
      opts.Validate();
    }

    [Fact]
    public void RelSecrets_WithNegativeAmount_ThrowsArgumentOutOfRangeException()
    {
      // Arrange: Config with a forbidden amount.
      var opts = new CnfProcRelSecretsOpt { Amount = -1 };

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
      var opts = new CnfProcRelSecretsOpt { ChanceSecretInSecret = chance };

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
      var opts = new CnfProcRelSecretsOpt { ChanceSecretInSecret = chance };

      // Act/Assert: The out-of-range value is rejected naming the offending property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => opts.Validate());
      Assert.Equal("ChanceSecretInSecret", exception.ParamName);
    }

    [Fact]
    public void RelSecrets_WithTwoPassageDefaults_DoesNotThrow()
    {
      // Arrange/Act: A fresh config carries the documented two-passage defaults.
      var opts = new CnfProcRelSecretsOpt();
      opts.Validate();

      // Assert: The two-passage feature ships off, with a balanced return roll, a rare
      // conditional allowance and a three-hop exit bound.
      Assert.Equal(0f, opts.ChanceTwoPassages);
      Assert.Equal(0.5f, opts.ChanceReturnToHost);
      Assert.Equal(0.1f, opts.ChanceIgnoreConditionals);
      Assert.Equal(3, opts.MaxExitDistance);
    }

    [Theory]
    [InlineData(nameof(CnfProcRelSecretsOpt.ChanceTwoPassages))]
    [InlineData(nameof(CnfProcRelSecretsOpt.ChanceReturnToHost))]
    [InlineData(nameof(CnfProcRelSecretsOpt.ChanceIgnoreConditionals))]
    public void RelSecrets_WithTwoPassageChanceWithinUnitInterval_DoesNotThrow(string property)
    {
      // Arrange: Config with every two-passage chance at its allowed boundaries.
      var opts = property switch
      {
        nameof(CnfProcRelSecretsOpt.ChanceTwoPassages) => new CnfProcRelSecretsOpt { ChanceTwoPassages = 1f },
        nameof(CnfProcRelSecretsOpt.ChanceReturnToHost) => new CnfProcRelSecretsOpt { ChanceReturnToHost = 0f },
        _ => new CnfProcRelSecretsOpt { ChanceIgnoreConditionals = 0.5f },
      };

      // Assert: Boundary values and the midpoint are all allowed.
      opts.Validate();
    }

    [Theory]
    [InlineData(nameof(CnfProcRelSecretsOpt.ChanceTwoPassages), -0.1f)]
    [InlineData(nameof(CnfProcRelSecretsOpt.ChanceTwoPassages), 1.1f)]
    [InlineData(nameof(CnfProcRelSecretsOpt.ChanceTwoPassages), float.NaN)]
    [InlineData(nameof(CnfProcRelSecretsOpt.ChanceReturnToHost), -0.1f)]
    [InlineData(nameof(CnfProcRelSecretsOpt.ChanceReturnToHost), 1.1f)]
    [InlineData(nameof(CnfProcRelSecretsOpt.ChanceReturnToHost), float.PositiveInfinity)]
    [InlineData(nameof(CnfProcRelSecretsOpt.ChanceIgnoreConditionals), -0.1f)]
    [InlineData(nameof(CnfProcRelSecretsOpt.ChanceIgnoreConditionals), 1.1f)]
    [InlineData(nameof(CnfProcRelSecretsOpt.ChanceIgnoreConditionals), float.NegativeInfinity)]
    public void RelSecrets_WithTwoPassageChanceOutsideUnitInterval_ThrowsArgumentOutOfRangeException(string property, float chance)
    {
      // Arrange: Config with one forbidden two-passage chance.
      var opts = property switch
      {
        nameof(CnfProcRelSecretsOpt.ChanceTwoPassages) => new CnfProcRelSecretsOpt { ChanceTwoPassages = chance },
        nameof(CnfProcRelSecretsOpt.ChanceReturnToHost) => new CnfProcRelSecretsOpt { ChanceReturnToHost = chance },
        _ => new CnfProcRelSecretsOpt { ChanceIgnoreConditionals = chance },
      };

      // Act/Assert: The out-of-range value is rejected naming the offending property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => opts.Validate());
      Assert.Equal(property, exception.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void RelSecrets_WithPositiveMaxExitDistance_DoesNotThrow(int distance)
    {
      // Arrange: Config demanding a reachable exit bound.
      var opts = new CnfProcRelSecretsOpt { MaxExitDistance = distance };

      // Assert: Any bound of at least one hop is allowed.
      opts.Validate();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void RelSecrets_WithNonPositiveMaxExitDistance_ThrowsArgumentOutOfRangeException(int distance)
    {
      // Arrange: Config demanding an impossible exit bound.
      var opts = new CnfProcRelSecretsOpt { MaxExitDistance = distance };

      // Act/Assert: The out-of-range value is rejected naming the offending property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => opts.Validate());
      Assert.Equal("MaxExitDistance", exception.ParamName);
    }

    [Fact]
    public void RelSecrets_WithPositiveMaxEdges_DoesNotThrow()
    {
      // Arrange: Config with a cap that allows at least one passage per secret.
      var opts = new CnfProcRelSecretsOpt { MaxEdges = 1 };

      // Assert: Any cap of at least one passage is allowed.
      opts.Validate();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void RelSecrets_WithNonPositiveMaxEdges_ThrowsArgumentOutOfRangeException(int maxEdges)
    {
      // Arrange: Config demanding a cap that leaves no room for a secret's entry passage.
      var opts = new CnfProcRelSecretsOpt { MaxEdges = maxEdges };

      // Act/Assert: The out-of-range value is rejected naming the offending property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => opts.Validate());
      Assert.Equal("MaxEdges", exception.ParamName);
    }

    [Fact]
    public void NodeObjectives_WithDefaultInstance_DoesNotThrow()
    {
      // Arrange/Act: The default instance uses the legacy hardcoded values.
      CnfProcNodeObjectivesOpt.Default.Validate();

      // Assert: Defaults match the values builders used before options were configurable.
      Assert.Equal(1, CnfProcNodeObjectivesOpt.Default.GoalAmount);
      Assert.Equal(1, CnfProcNodeObjectivesOpt.Default.OptGoalAmount);

      // Assert: Host weights fall from POI-free nodes to Start/Exit nodes.
      Assert.Equal(4, CnfProcNodeObjectivesOpt.Default.WeightNoPoi);
      Assert.Equal(2, CnfProcNodeObjectivesOpt.Default.WeightOtherPoi);
      Assert.Equal(1, CnfProcNodeObjectivesOpt.Default.WeightStartExitPoi);
    }

    [Theory]
    [InlineData(nameof(CnfProcNodeObjectivesOpt.WeightNoPoi), 0)]
    [InlineData(nameof(CnfProcNodeObjectivesOpt.WeightNoPoi), -1)]
    [InlineData(nameof(CnfProcNodeObjectivesOpt.WeightOtherPoi), 0)]
    [InlineData(nameof(CnfProcNodeObjectivesOpt.WeightOtherPoi), -1)]
    [InlineData(nameof(CnfProcNodeObjectivesOpt.WeightStartExitPoi), 0)]
    [InlineData(nameof(CnfProcNodeObjectivesOpt.WeightStartExitPoi), -1)]
    public void NodeObjectives_WithWeightBelowOne_ThrowsArgumentOutOfRangeException(string property, int value)
    {
      // Arrange: Config with one forbidden weight.
      var opts = property switch
      {
        nameof(CnfProcNodeObjectivesOpt.WeightNoPoi) => new CnfProcNodeObjectivesOpt { WeightNoPoi = value },
        nameof(CnfProcNodeObjectivesOpt.WeightOtherPoi) => new CnfProcNodeObjectivesOpt { WeightOtherPoi = value },
        _ => new CnfProcNodeObjectivesOpt { WeightStartExitPoi = value },
      };

      // Act/Assert: The out-of-range value is rejected naming the offending property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => opts.Validate());
      Assert.Equal(property, exception.ParamName);
    }

    [Theory]
    [InlineData(nameof(CnfProcNodeObjectivesOpt.GoalAmount))]
    [InlineData(nameof(CnfProcNodeObjectivesOpt.OptGoalAmount))]
    public void NodeObjectives_WithNegativeAmount_ThrowsArgumentOutOfRangeException(string property)
    {
      // Arrange: Config with one forbidden amount.
      var opts = property == nameof(CnfProcNodeObjectivesOpt.GoalAmount)
        ? new CnfProcNodeObjectivesOpt { GoalAmount = -1 }
        : new CnfProcNodeObjectivesOpt { OptGoalAmount = -1 };

      // Act/Assert: The out-of-range value is rejected naming the offending property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => opts.Validate());
      Assert.Equal(property, exception.ParamName);
    }

    [Fact]
    public void NodeObjectives_WithZeroAmounts_DoesNotThrow()
    {
      // Arrange: Config disabling both phases.
      var opts = new CnfProcNodeObjectivesOpt { GoalAmount = 0, OptGoalAmount = 0 };

      // Assert: Zero is allowed and disables the processor.
      opts.Validate();
    }

    [Fact]
    public void ObstaclePlacer_WithDefaultInstance_DoesNotThrow()
    {
      // Arrange/Act: The default instance carries the documented defaults.
      var opts = CnfProcObstaclePlacerOpt.Default;
      opts.Validate();

      // Assert: Defaults match the values documented in OBSTACLES.md.
      Assert.Equal(3, opts.MaxConditionsPerEdge);
      Assert.Equal(4, opts.MaxEdgesPerObstacle);
      Assert.Equal(0.7f, opts.MainShare);
      Assert.Equal(3, opts.SmallComplexityMax);
      Assert.Equal(0.75f, opts.ChanceReserveSide);
      Assert.Equal(0.2f, opts.ChanceReserveSideOnly);
      Assert.Equal(0.4f, opts.ChanceNestSolution);
      Assert.Equal(0.2f, opts.ChanceStack);
      Assert.Equal(0.25f, opts.MaxStackLoad);
      Assert.Equal(2, opts.FreeStackedConditions);
      Assert.Equal(0.25f, opts.ChanceSecretBypass);
      Assert.Equal(0.01f, opts.ChanceBypass);
      Assert.Equal(2f, opts.CutCostHalving);
    }

    [Fact]
    public void ObstaclePlacer_WithBoundaryValues_DoesNotThrow()
    {
      // Arrange: Config with every value at an allowed boundary.
      var opts = new CnfProcObstaclePlacerOpt
      {
        MaxConditionsPerEdge = 1,
        MaxEdgesPerObstacle = 1,
        SmallComplexityMax = 0,
        MainShare = 0f,
        ChanceReserveSide = 1f,
        ChanceReserveSideOnly = 0f,
        ChanceNestSolution = 1f,
        ChanceStack = 0f,
        MaxStackLoad = 0f,
        FreeStackedConditions = 0,
        ChanceSecretBypass = 1f,
        ChanceBypass = 0f,
        CutCostHalving = 0f,
      };

      // Act/Assert: Smallest limits and both ends of the unit interval are allowed.
      opts.Validate();
    }

    [Theory]
    [InlineData(nameof(CnfProcObstaclePlacerOpt.MaxConditionsPerEdge), 0)]
    [InlineData(nameof(CnfProcObstaclePlacerOpt.MaxConditionsPerEdge), -2)]
    [InlineData(nameof(CnfProcObstaclePlacerOpt.MaxEdgesPerObstacle), 0)]
    [InlineData(nameof(CnfProcObstaclePlacerOpt.MaxEdgesPerObstacle), -2)]
    [InlineData(nameof(CnfProcObstaclePlacerOpt.SmallComplexityMax), -1)]
    public void ObstaclePlacer_WithLimitBelowMinimum_ThrowsArgumentOutOfRangeException(string property, int value)
    {
      // Arrange: Config with one forbidden limit.
      var opts = property switch
      {
        nameof(CnfProcObstaclePlacerOpt.MaxConditionsPerEdge) => new CnfProcObstaclePlacerOpt { MaxConditionsPerEdge = value },
        nameof(CnfProcObstaclePlacerOpt.MaxEdgesPerObstacle) => new CnfProcObstaclePlacerOpt { MaxEdgesPerObstacle = value },
        _ => new CnfProcObstaclePlacerOpt { SmallComplexityMax = value },
      };

      // Act/Assert: The out-of-range value is rejected naming the offending property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => opts.Validate());
      Assert.Equal(property, exception.ParamName);
    }

    /// <summary>Every share/chance property of the obstacle placer paired with forbidden values.</summary>
    public static TheoryData<string, float> ObstaclePlacerBadChances()
    {
      string[] properties =
      [
        nameof(CnfProcObstaclePlacerOpt.MainShare),
        nameof(CnfProcObstaclePlacerOpt.ChanceReserveSide),
        nameof(CnfProcObstaclePlacerOpt.ChanceReserveSideOnly),
        nameof(CnfProcObstaclePlacerOpt.ChanceNestSolution),
        nameof(CnfProcObstaclePlacerOpt.ChanceStack),
        nameof(CnfProcObstaclePlacerOpt.ChanceSecretBypass),
        nameof(CnfProcObstaclePlacerOpt.ChanceBypass),
      ];
      float[] values = [-0.1f, 1.1f, float.NaN, float.PositiveInfinity];

      var data = new TheoryData<string, float>();
      foreach (var property in properties)
      {
        foreach (var value in values) data.Add(property, value);
      }

      return data;
    }

    [Theory]
    [MemberData(nameof(ObstaclePlacerBadChances))]
    public void ObstaclePlacer_WithChanceOutsideUnitInterval_ThrowsArgumentOutOfRangeException(string property, float chance)
    {
      // Arrange: Config with one forbidden share or chance.
      var opts = property switch
      {
        nameof(CnfProcObstaclePlacerOpt.MainShare) => new CnfProcObstaclePlacerOpt { MainShare = chance },
        nameof(CnfProcObstaclePlacerOpt.ChanceReserveSide) => new CnfProcObstaclePlacerOpt { ChanceReserveSide = chance },
        nameof(CnfProcObstaclePlacerOpt.ChanceReserveSideOnly) => new CnfProcObstaclePlacerOpt { ChanceReserveSideOnly = chance },
        nameof(CnfProcObstaclePlacerOpt.ChanceNestSolution) => new CnfProcObstaclePlacerOpt { ChanceNestSolution = chance },
        nameof(CnfProcObstaclePlacerOpt.ChanceStack) => new CnfProcObstaclePlacerOpt { ChanceStack = chance },
        nameof(CnfProcObstaclePlacerOpt.ChanceSecretBypass) => new CnfProcObstaclePlacerOpt { ChanceSecretBypass = chance },
        _ => new CnfProcObstaclePlacerOpt { ChanceBypass = chance },
      };

      // Act/Assert: The out-of-range value is rejected naming the offending property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => opts.Validate());
      Assert.Equal(property, exception.ParamName);
    }

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ObstaclePlacer_WithInvalidMaxStackLoad_ThrowsArgumentOutOfRangeException(float load)
    {
      // Arrange: A stack limit that is negative or not finite.
      var opts = new CnfProcObstaclePlacerOpt { MaxStackLoad = load };

      // Act/Assert: Validation rejects it naming the property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => opts.Validate());
      Assert.Equal(nameof(CnfProcObstaclePlacerOpt.MaxStackLoad), exception.ParamName);
    }

    [Theory]
    [InlineData(-0.01f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ObstaclePlacer_WithInvalidCutCostHalving_ThrowsArgumentOutOfRangeException(float halving)
    {
      // Arrange: A cost halving that is negative or not finite.
      var opts = new CnfProcObstaclePlacerOpt { CutCostHalving = halving };

      // Act/Assert: Validation rejects it naming the property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => opts.Validate());
      Assert.Equal(nameof(CnfProcObstaclePlacerOpt.CutCostHalving), exception.ParamName);
    }

    [Fact]
    public void ObstaclePlacer_WithMaxStackLoadAboveOne_DoesNotThrow()
    {
      // Arrange/Act/Assert: The load is a ratio without an upper bound; a large value switches the limit off.
      new CnfProcObstaclePlacerOpt { MaxStackLoad = 100f }.Validate();
    }

    [Fact]
    public void ObstaclePlacer_WithNegativeFreeStackedConditions_ThrowsArgumentOutOfRangeException()
    {
      // Arrange: A negative allowance.
      var opts = new CnfProcObstaclePlacerOpt { FreeStackedConditions = -1 };

      // Act/Assert: Validation rejects it naming the property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => opts.Validate());
      Assert.Equal(nameof(CnfProcObstaclePlacerOpt.FreeStackedConditions), exception.ParamName);
    }

    [Fact]
    public void ProcOpts_WithInvalidObstaclePlacerValue_ThrowsNamingNestedProperty()
    {
      // Arrange: Proc options whose obstacle-placer section violates its own invariants.
      var procOpts = CnfProcOpt.Default with
      {
        ObstaclePlacer = new CnfProcObstaclePlacerOpt { MaxConditionsPerEdge = 0 },
      };

      // Act/Assert: Validation cascades into the section and names the nested property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => procOpts.Validate());
      Assert.Equal("MaxConditionsPerEdge", exception.ParamName);
    }

    [Fact]
    public void NodeRelSize_WithDefaultInstance_DoesNotThrow()
    {
      // Arrange/Act: The default instance uses the legacy hardcoded values.
      CnfProcNodeRelSizeOpt.Default.Validate();

      // Assert: Defaults match the values builders used before options were configurable.
      Assert.Equal(3, CnfProcNodeRelSizeOpt.Default.Steps);
      Assert.Equal(EnRelSizeCurve.Geometric, CnfProcNodeRelSizeOpt.Default.Curve);
      Assert.Equal(1, CnfProcNodeRelSizeOpt.Default.MinSize);
      Assert.Equal(2f, CnfProcNodeRelSizeOpt.Default.Growth);
    }

    [Fact]
    public void NodeRelSize_WithSingleStepAndDefaultBiases_DoesNotThrow()
    {
      // Arrange: A single step leaves no variation, so bias is ignored rather than rejected.
      var opts = CnfProcNodeRelSizeOpt.Default with { Steps = 1 };

      // Act/Assert: Validation accepts it; bias simply does nothing with one step.
      opts.Validate();
    }

    [Fact]
    public void NodeRelSize_WithNegativeGrowth_ThrowsArgumentOutOfRangeException()
    {
      // Arrange: Growth below zero violates the documented non-negative range.
      var opts = CnfProcNodeRelSizeOpt.Default with
      {
        Growth = -1f,
      };

      // Act/Assert: The out-of-range value is rejected naming the offending property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => opts.Validate());
      Assert.Equal("Growth", exception.ParamName);
    }

    [Fact]
    public void NodeRelSize_WithUndefinedCurve_ThrowsArgumentOutOfRangeException()
    {
      // Arrange: Config carrying a curve value no member defines.
      var opts = CnfProcNodeRelSizeOpt.Default with
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
      CnfProcNodeStyleOpt.Default.Validate();

      // Assert: Defaults match the values builders used before options were configurable.
      Assert.Equal(3, CnfProcNodeStyleOpt.Default.GroupCount);
    }

    [Fact]
    public void NodeStyle_WithZeroGroupCount_ThrowsArgumentOutOfRangeException()
    {
      // Arrange: Config demanding fewer than one group.
      var opts = new CnfProcNodeStyleOpt { GroupCount = 0 };

      // Act/Assert: The out-of-range value is rejected naming the offending property.
      var exception = Assert.Throws<ArgumentOutOfRangeException>(() => opts.Validate());
      Assert.Equal("GroupCount", exception.ParamName);
    }
  }
}
