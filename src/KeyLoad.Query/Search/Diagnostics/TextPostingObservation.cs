namespace KeyLoad.Query.Search;

/// <summary>Flows one bounded posting-observation callback within its original call.</summary>
internal sealed class TextPostingObservation : IDisposable
{
    private const string NonCurrentScope = "The original execution text observation scope is not current.";

    private static readonly AsyncLocal<TextPostingObservation?> Current = new();
    private readonly TextPostingObservation? previous;
    private readonly Func<CancellationToken, ValueTask> callback;
    private bool disposed;

    private TextPostingObservation(Func<CancellationToken, ValueTask> callback)
    {
        this.callback = callback;
        previous = Current.Value;
        Current.Value = this;
    }

    internal static TextPostingObservation Enter(Func<CancellationToken, ValueTask> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return new(callback);
    }

    internal static Func<CancellationToken, ValueTask>? Capture()
    {
        var original = Current.Value;
        if (original is null)
        {
            return null;
        }
        ObjectDisposedException.ThrowIf(original.disposed, original);
        return original.callback;
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        if (!ReferenceEquals(Current.Value, this))
        {
            throw new InvalidOperationException(NonCurrentScope);
        }
        Current.Value = previous;
        disposed = true;
    }
}
