// Polyfill required for `record` and `init` accessors on netstandard2.0.
// The compiler emits references to this type when the `init` keyword or positional
// record syntax is used; without it the build fails on older TFMs.
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
