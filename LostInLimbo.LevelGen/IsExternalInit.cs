namespace System.Runtime.CompilerServices;

// Compile-time marker required by init-only setters on netstandard2.1.
// Never instantiated; defined here because netstandard2.1 predates it.
// Without this shim compilation will fail.
// This issue is specific to libraries targeting old frameworks while using modern syntax.
internal sealed class IsExternalInit
{
}