#if DEMEOBATTLES || DEMEOBATTLEMARKED
// IL2CPP proxy assemblies can expose a rewritten NullableAttribute whose
// constructors do not match the CLR compiler's byte/byte[] contract. Async and
// LINQ closures may require it even in a nullable-disabled source file.
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
    internal sealed class NullableAttribute : Attribute
    {
        public NullableAttribute(byte flag) { }
        public NullableAttribute(byte[] flags) { }
    }
}
#endif
