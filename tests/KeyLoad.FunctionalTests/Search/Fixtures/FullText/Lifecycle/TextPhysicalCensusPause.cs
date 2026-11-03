using KeyLoad.Server.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class TextPhysicalCensusPause(string root, Guid node, IOptions<TextExecutionOptions> options) : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);
    private readonly ManualResetEventSlim entered = new();
    private readonly ManualResetEventSlim resumed = new();
    private int armed;

    internal void Arm() => Interlocked.Exchange(ref armed, 1);

    internal void Verify(ReadExecutionBudget? budget)
    {
        budget?.Check();
        TextFileIO.VerifyDirectory(root);
        TextRootFiles.VerifyReceipt(root, node, options);
        if (Interlocked.Exchange(ref armed, 0) != 1)
        {
            return;
        }
        entered.Set();
        if (!resumed.Wait(Wait))
        {
            throw new TimeoutException("The physical census was not released.");
        }
    }

    internal void WaitUntilEntered(CancellationToken token)
    {
        if (!entered.Wait(Wait, token))
        {
            throw new TimeoutException("The physical census did not enter the resource owner.");
        }
    }

    internal void Resume() => resumed.Set();

    public void Dispose()
    {
        Interlocked.Exchange(ref armed, 0);
        resumed.Set();
        entered.Dispose();
        resumed.Dispose();
    }
}
