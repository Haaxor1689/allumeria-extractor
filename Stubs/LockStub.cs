// Stub for System.Threading.Lock introduced in .NET 9.
// Provides enough surface area to compile decompiled Allumeria source against .NET 8.

namespace System.Threading
{
  /// <summary>Navigation-only stub for the .NET 9 System.Threading.Lock type.</summary>
  public sealed class Lock
  {
    public void Enter() { }
    public bool TryEnter() => true;
    public bool TryEnter(int millisecondsTimeout) => true;
    public bool TryEnter(TimeSpan timeout) => true;
    public void Exit() { }
    public bool IsHeldByCurrentThread => true;

    public Scope EnterScope() => new Scope(this);

    public ref struct Scope
    {
      private readonly Lock _lock;
      internal Scope(Lock l) { _lock = l; }
      public void Dispose() => _lock.Exit();
    }
  }
}
