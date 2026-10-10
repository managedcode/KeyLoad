namespace KeyLoad.Server;

internal sealed class ServerConnectionOperation : IDisposable
{
    private ServerConnectionFeature? owner;
    private readonly CancellationTokenSource cancellation;

    internal ServerConnectionOperation(ServerConnectionFeature owner, CancellationToken caller,
        CancellationToken connectionClosed)
    {
        this.owner = owner;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(caller, connectionClosed);
        Id = owner.Id;
    }

    internal Guid Id { get; }
    internal CancellationToken Token => cancellation.Token;

    public void Dispose()
    {
        var original = Interlocked.Exchange(ref owner, null);
        if (original is null)
        { return; }
        cancellation.Dispose();
        original.Release();
    }
}
