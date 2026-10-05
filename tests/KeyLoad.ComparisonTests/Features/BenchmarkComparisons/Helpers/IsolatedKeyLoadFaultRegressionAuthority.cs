using Aspire.Hosting;
using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-005: reconnects genuine public clients to retained persisted credentials and completed delivery tokens.</summary>
internal static class IsolatedKeyLoadFaultRegressionAuthority
{
    internal static async Task VerifyAsync(DistributedApplication app, int node, string admin,
        IsolatedKeyLoadFaultRegressionSeed seed, CancellationToken token)
    {
        using var http = IsolatedKeyLoadPublicRegressionProtocol.CreateHttp(app, node);
        var sdk = new KeyLoadClient(http, admin, ComparisonClientOptions.Execution());
        await IsolatedKeyLoadFaultRegressionAssertions.PersistedAsync(sdk, seed, token);
        await using var mcp = await IsolatedKeyLoadFaultRegressionCalls.ConnectAsync(app, node, admin, token);
        await IsolatedKeyLoadPublicRegressionAssertions.EqualAsync(seed.AckReceipt,
            await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
                attempt => mcp.SuccessAsync<CommitReceipt>(IsolatedKeyLoadPublicRegressionProtocol.Complete, seed.Ack, attempt), token));
        await IsolatedKeyLoadFaultRegressionCalls.RunAsync(attempt => mcp.ErrorAsync(IsolatedKeyLoadPublicRegressionProtocol.Complete,
            seed.Ack with { CommandId = Guid.NewGuid() }, ErrorCode.StaleLease, attempt), token);
        await ReaderAsync(app, node, seed, token);
    }

    private static async Task ReaderAsync(DistributedApplication app, int node,
        IsolatedKeyLoadFaultRegressionSeed seed, CancellationToken token)
    {
        using var http = IsolatedKeyLoadPublicRegressionProtocol.CreateHttp(app, node);
        var sdk = new KeyLoadClient(http, seed.Reader.Secret, ComparisonClientOptions.Execution());
        await using var mcp = await IsolatedKeyLoadFaultRegressionCalls.ConnectAsync(app, node, seed.Reader.Secret, token);
        await IsolatedKeyLoadPublicRegressionAssertions.DocumentAsync(
            (await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
                attempt => sdk.GetAsync(seed.Sentinel, attempt), token)))!,
            seed.Sentinel, IsolatedKeyLoadFaultRegressionSeed.Payload, 1);
        await IsolatedKeyLoadPublicRegressionAssertions.DocumentAsync(
            await IsolatedKeyLoadFaultRegressionCalls.RunAsync(attempt => mcp.SuccessAsync<DocumentResult>(
                IsolatedKeyLoadPublicRegressionProtocol.Get, new GetDocumentRequest(seed.Sentinel), attempt), token),
            seed.Sentinel, IsolatedKeyLoadFaultRegressionSeed.Payload, 1);
        var denied = new CommandRequest(Guid.NewGuid(), seed.Partition,
            [new PutDocument(seed.Sentinel.Collection, seed.Sentinel.Id, IsolatedKeyLoadFaultRegressionSeed.Payload, 1, ExplicitReplacement: true)]);
        await IsolatedKeyLoadPublicRegressionAssertions.ErrorAsync(await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
            attempt => sdk.CommitAsync(denied, attempt), token),
            ErrorCode.PermissionDenied, seed.Reader.Secret);
        await IsolatedKeyLoadFaultRegressionCalls.RunAsync(attempt => mcp.ErrorAsync(IsolatedKeyLoadPublicRegressionProtocol.Commit,
            denied, ErrorCode.PermissionDenied, attempt, seed.Reader.Secret), token);
        await IsolatedKeyLoadPublicRegressionAssertions.DocumentAsync(
            (await IsolatedKeyLoadPublicRegressionAssertions.SuccessAsync(await IsolatedKeyLoadFaultRegressionCalls.RunAsync(
                attempt => sdk.GetAsync(seed.Sentinel, attempt), token)))!, seed.Sentinel, IsolatedKeyLoadFaultRegressionSeed.Payload, 1);
    }
}
