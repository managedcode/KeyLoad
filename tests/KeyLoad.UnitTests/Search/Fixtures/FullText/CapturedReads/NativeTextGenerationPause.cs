using KeyLoad.Server.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class NativeTextGenerationPause : IDisposable
{
    private readonly ManualResetEventSlim entered = new();
    private readonly ManualResetEventSlim resume = new();
    private int armed;

    internal void Arm() => Interlocked.Exchange(ref armed, 1);

    internal void Observe(NativeTextFaultStage stage)
    {
        if (stage != NativeTextFaultStage.NativePostingWritten || Interlocked.Exchange(ref armed, 0) != 1)
        {
            return;
        }
        entered.Set();
        if (!resume.Wait(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("Native generation test barrier expired.");
        }
    }

    internal void WaitUntilEntered(CancellationToken cancellation)
    {
        if (!entered.Wait(TimeSpan.FromSeconds(30), cancellation))
        {
            throw new TimeoutException("Native generation did not reach the posting barrier.");
        }
    }

    internal void Resume() => resume.Set();

    public void Dispose()
    {
        resume.Set();
        entered.Dispose();
        resume.Dispose();
    }
}
