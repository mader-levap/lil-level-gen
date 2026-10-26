using System;

namespace System.Diagnostics.CodeAnalysis;

// Compile-time marker needed for records with `required` members when targeting netstandard2.1.
// Records synthesize a copy constructor annotated with this attribute; without this shim the
// compilation fails with CS0656. Never instantiated; the type shipped with C# 11 / .NET 7.

/// <summary>Indicates that the annotated constructor sets all required members.</summary>
[AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false, Inherited = false)]
[ExcludeFromCodeCoverage]
internal sealed class SetsRequiredMembersAttribute : Attribute
{
}
