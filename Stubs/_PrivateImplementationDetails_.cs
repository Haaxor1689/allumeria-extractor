// Stub for the compiler-generated <PrivateImplementationDetails> type that dotPeek
// emits when decompiling code that uses C# 12 inline-array structs ([InlineArray(N)]).
// The two methods mirror the JIT intrinsics used internally by the runtime.

using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// ReSharper disable once CheckNamespace
internal static class _PrivateImplementationDetails_
{
  /// <summary>Returns a reference to the element at <paramref name="index"/> inside an inline-array struct.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal static ref T InlineArrayElementRef<TArray, T>(ref TArray array, int index) =>
    ref Unsafe.Add(ref Unsafe.As<TArray, T>(ref array), index);

  /// <summary>Returns a read-only span over the entire inline-array struct.</summary>
  [MethodImpl(MethodImplOptions.AggressiveInlining)]
  internal static ReadOnlySpan<T> InlineArrayAsReadOnlySpan<TArray, T>(in TArray array, int length) =>
    MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<TArray, T>(ref Unsafe.AsRef(in array)), length);
}

