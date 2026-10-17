namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Owns provider-backed operation deadlines linked to the actual TUnit test lifetime.</summary>
internal sealed class McpCallerDeadline : IDisposable
{
    private readonly CancellationTokenSource timeout = new(McpCallerProtocol.Deadline, TimeProvider.System);
    private readonly CancellationTokenSource deadline;

    private McpCallerDeadline()
    {
        try
        {
            deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken, timeout.Token);
        }
        catch (Exception)
        {
            timeout.Dispose();
            throw;
        }
    }

    internal CancellationToken Token => deadline.Token;
    internal static McpCallerDeadline Create() => new();

    public void Dispose()
    {
        deadline.Dispose();
        timeout.Dispose();
    }
}
