using Aspire.Hosting;
using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-005: reconnects genuine public clients to retained persisted credentials and completed delivery tokens.</summary>
internal static class IsolatedKeyLoadFaultChecksAuthority
{
    internal static async Task VerifyAsync(DistributedApplication app, int node, string admin,
        IsolatedKeyLoadFaultChecksSeed seed, CancellationToken token)
    {
        using var http = IsolatedKeyLoadPublicChecksProtocol.CreateHttp(app, node);
        var sdk = new KeyLoadClient(http, admin, ComparisonClientOptions.Execution());
        await IsolatedKeyLoadFaultChecksAssertions.PersistedAsync(sdk, seed, token);
        await using var mcp = await IsolatedKeyLoadFaultChecksCalls.ConnectAsync(app, node, admin, token);
        await IsolatedKeyLoadPublicChecksAssertions.EqualAsync(seed.AckReceipt,
            await IsolatedKeyLoadFaultChecksCalls.RunAsync(
                attempt => mcp.SuccessAsync<CommitReceipt>(IsolatedKeyLoadPublicChecksProtocol.Complete, seed.Ack, attempt), token));
        await IsolatedKeyLoadFaultChecksCalls.RunAsync(attempt => mcp.ErrorAsync(IsolatedKeyLoadPublicChecksProtocol.Complete,
            seed.Ack with
            {
                CommandId = Guid.NewGuid()
            }, ErrorCode.StaleLease, attempt), token);
        await ReaderAsync(app, node, seed, token);
    }

    private static async Task ReaderAsync(DistributedApplication app, int node,
        IsolatedKeyLoadFaultChecksSeed seed, CancellationToken token)
    {
        using var http = IsolatedKeyLoadPublicChecksProtocol.CreateHttp(app, node);
        var sdk = new KeyLoadClient(http, seed.Reader.Secret, ComparisonClientOptions.Execution());
        await using var mcp = await IsolatedKeyLoadFaultChecksCalls.ConnectAsync(app, node, seed.Reader.Secret, token);
        await IsolatedKeyLoadPublicChecksAssertions.DocumentAsync(
            (await IsolatedKeyLoadPublicChecksAssertions.DocumentReadAsync(await IsolatedKeyLoadFaultChecksCalls.RunAsync(
                attempt => sdk.GetAsync(seed.Sentinel, attempt), token)))!,
            seed.Sentinel, IsolatedKeyLoadFaultChecksSeed.Payload, 1);
        await IsolatedKeyLoadPublicChecksAssertions.DocumentAsync(
            await IsolatedKeyLoadFaultChecksCalls.RunAsync(attempt => mcp.RequiredDocumentAsync(
                IsolatedKeyLoadPublicChecksProtocol.Get, new GetDocumentRequest(seed.Sentinel), attempt), token),
            seed.Sentinel, IsolatedKeyLoadFaultChecksSeed.Payload, 1);
        var denied = new CommandRequest(Guid.NewGuid(), seed.Partition,
            [new PutDocument(seed.Sentinel.Collection, seed.Sentinel.Id, IsolatedKeyLoadFaultChecksSeed.Payload, 1, ExplicitReplacement: true)]);
        await IsolatedKeyLoadPublicChecksAssertions.ErrorAsync(await IsolatedKeyLoadFaultChecksCalls.RunAsync(
            attempt => sdk.CommitAsync(denied, attempt), token),
            ErrorCode.PermissionDenied, seed.Reader.Secret);
        await IsolatedKeyLoadFaultChecksCalls.RunAsync(attempt => mcp.ErrorAsync(IsolatedKeyLoadPublicChecksProtocol.Commit,
            denied, ErrorCode.PermissionDenied, attempt, seed.Reader.Secret), token);
        await IsolatedKeyLoadPublicChecksAssertions.DocumentAsync(
            (await IsolatedKeyLoadPublicChecksAssertions.DocumentReadAsync(await IsolatedKeyLoadFaultChecksCalls.RunAsync(
                attempt => sdk.GetAsync(seed.Sentinel, attempt), token)))!, seed.Sentinel, IsolatedKeyLoadFaultChecksSeed.Payload, 1);
    }
}
