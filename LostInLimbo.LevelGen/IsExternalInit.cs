namespace System.Runtime.CompilerServices;

// Compile-time marker needed to compile init-only setters when targeting netstandard2.1.
// Never instantiated; the type shipped with C# 9 / .NET 5 and is not part of netstandard2.1.
// Without this shim, compilation will fail.
// The same applies to any target older than .NET 5 (e.g. .NET Framework) using modern syntax.
internal sealed class IsExternalInit
{
}