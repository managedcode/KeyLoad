
namespace KeyLoad.QueryExecution;

internal static class ScriptDomParsingObservation
{
    private static readonly AsyncLocal<Action<int>?> Current = new();
    internal static IDisposable Enter(Action<int> tokenAccess) => new Scope(tokenAccess);
    internal static void Token(int index) => Current.Value?.Invoke(index);
    private sealed class Scope : IDisposable
    {
        private readonly Action<int>? previous;
        private bool disposed;
        internal Scope(Action<int> tokenAccess)
        {
            ArgumentNullException.ThrowIfNull(tokenAccess);
            previous = Current.Value;
            Current.Value = tokenAccess;
        }
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            Current.Value = previous;
        }
    }
}
