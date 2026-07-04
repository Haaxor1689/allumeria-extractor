// Stub for the C# 12 [InlineArray(N)] compiler-generated struct InlineArray4<T>.
// The decompiled source uses this as a fixed-size 4-element buffer for BitConverter
// packing (4 bytes → uint via BitConverter.ToUInt32).

using System.Runtime.CompilerServices;

[InlineArray(4)]
internal struct InlineArray4<T>
{
#pragma warning disable CS0649
  private T _element0;
#pragma warning restore CS0649
}
