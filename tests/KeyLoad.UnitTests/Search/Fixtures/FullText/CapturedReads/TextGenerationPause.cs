using KeyLoad.Server.Search;

namespace KeyLoad.UnitTests.Search;

internal sealed class TextGenerationPause : IDisposable
{
    private readonly ManualResetEventSlim entered = new();
    private readonly ManualResetEventSlim resume = new();
    private int armed;

    internal void Arm() => Interlocked.Exchange(ref armed, 1);

    internal void Observe(TextFaultStage stage)
    {
        if (stage != TextFaultStage.PostingWritten || Interlocked.Exchange(ref armed, 0) != 1)
        {
            return;
        }
        entered.Set();
        if (!resume.Wait(TimeSpan.FromSeconds(30)))
        {
            throw new TimeoutException("Execution generation test barrier expired.");
        }
    }

    internal void WaitUntilEntered(CancellationToken cancellation)
    {
        if (!entered.Wait(TimeSpan.FromSeconds(30), cancellation))
        {
            throw new TimeoutException("Execution generation did not reach the posting barrier.");
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
