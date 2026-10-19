namespace KeyLoad.Server.Features.Search;

internal sealed class TextSelectedWriteLease : IDisposable
{
    private TextSelectedReadAdmission? owner;
    internal TextSelectedWriteLease(TextSelectedReadAdmission admission, CancellationToken token)
    {
        admission.WaitForWriteAsync(token).GetAwaiter().GetResult();
        owner = admission;
    }
    public void Dispose() => Interlocked.Exchange(ref owner, null)?.ExitWrite();
}
