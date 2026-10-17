namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextSelectedWriteLease : IDisposable
{
    private NativeTextSelectedReadAdmission? owner;
    internal NativeTextSelectedWriteLease(NativeTextSelectedReadAdmission admission, CancellationToken token)
    {
        admission.WaitForWriteAsync(token).GetAwaiter().GetResult();
        owner = admission;
    }
    public void Dispose() => Interlocked.Exchange(ref owner, null)?.ExitWrite();
}
