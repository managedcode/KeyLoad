namespace KeyLoad.Query.Features.Search;

/// <summary>Flows one bounded fixture callback with its original call; never persists identity or results.</summary>
internal sealed class NativeTextPostingObservation : IDisposable
{
    private const string NonCurrentScope = "The original native text observation scope is not current.";

    private static readonly AsyncLocal<NativeTextPostingObservation?> Current = new();
    private readonly NativeTextPostingObservation? previous;
    private readonly Func<CancellationToken, ValueTask> callback;
    private bool disposed;

    private NativeTextPostingObservation(Func<CancellationToken, ValueTask> callback)
    {
        this.callback = callback;
        previous = Current.Value;
        Current.Value = this;
    }

    internal static NativeTextPostingObservation Enter(Func<CancellationToken, ValueTask> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return new(callback);
    }

    internal static Func<CancellationToken, ValueTask>? Capture()
    {
        var original = Current.Value;
        if (original is null)
        { return null; }
        ObjectDisposedException.ThrowIf(original.disposed, original);
        return original.callback;
    }

    public void Dispose()
    {
        if (disposed)
        { return; }
        if (!ReferenceEquals(Current.Value, this))
        { throw new InvalidOperationException(NonCurrentScope); }
        Current.Value = previous;
        disposed = true;
    }
}
