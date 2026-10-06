namespace KeyLoad.AppHost.Hosting;

/// <summary>Owns a native provider deadline and the incoming cancellation registration.</summary>
internal sealed class AppHostDeadline : CancellationTokenSource
{
    private readonly CancellationTokenRegistration incoming;

    internal AppHostDeadline(TimeSpan timeout, TimeProvider timeProvider, CancellationToken token)
        : base(timeout, timeProvider)
    {
        incoming = token.UnsafeRegister(static state => ((AppHostDeadline)state!).Cancel(), this);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            incoming.Dispose();
        }
        base.Dispose(disposing);
    }
}
