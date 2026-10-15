namespace System.Runtime.CompilerServices;

// Compile-time marker needed to use `required` members when targeting netstandard2.1.
// Never instantiated; the types shipped with C# 11 / .NET 7 and are not part of netstandard2.1.
// Without this shim, marking a member `required` fails compilation.
// The same applies to any target older than .NET 7 using modern syntax.

/// <summary>Marks a member as required to be set in an object initializer.</summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
internal sealed class RequiredMemberAttribute : Attribute
{
}

/// <summary>Indicates that a compiler feature is required to use the annotated member.</summary>
[AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
internal sealed class CompilerFeatureRequiredAttribute : Attribute
{
  /// <summary>Creates an instance of the attribute.</summary>
  /// <param name="featureName">Name of the required compiler feature.</param>
  public CompilerFeatureRequiredAttribute(string featureName)
  {
    FeatureName = featureName;
  }

  /// <summary>Name of the required compiler feature.</summary>
  public string FeatureName { get; }

  /// <summary>Whether the feature is optional; only required when <see langword="false"/>.</summary>
  public bool IsOptional { get; set; }
}
