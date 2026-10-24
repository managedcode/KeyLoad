namespace KeyLoad.IntegrationTests.ClientApi;

/// <summary>Owns the operation lifetime of one test, bound to the actual TUnit test lifetime only. A test takes as long
/// as its real operations take (cold cluster restarts included); a hang is bounded by the test run, not by a fixed
/// test-side cap.</summary>
internal sealed class McpCallerDeadline : IDisposable
{
    private readonly CancellationTokenSource deadline =
        CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);

    private McpCallerDeadline()
    {
    }

    internal CancellationToken Token => deadline.Token;
    internal static McpCallerDeadline Create() => new();

    public void Dispose() => deadline.Dispose();
}
