namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Owns system-time operation deadlines linked to the actual TUnit test lifetime.</summary>
internal static class McpCallerDeadline
{
    /// <summary>Creates a caller-owned bounded token source without changing any cluster clock.</summary>
    /// <returns>A source disposed by the test after its native client drains.</returns>
    internal static CancellationTokenSource Create()
    {
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current!.Execution.CancellationToken);
        deadline.CancelAfter(McpCallerProtocol.Deadline);
        return deadline;
    }
}
