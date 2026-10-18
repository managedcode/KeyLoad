using System.Net;
using Aspire.Hosting;
using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-005: unavailable required copies never yield a successful write acknowledgement.</summary>
internal static class IsolatedKeyLoadFaultChecksCopyAvailability
{
    internal static async Task<IsolatedKeyLoadFaultChecksRejection> RejectAsync(DistributedApplication app, int liveNode, string admin,
        IsolatedKeyLoadFaultChecksSeed seed, CommandRequest command, bool hasSurvivor, CancellationToken token)
    {
        using var http = IsolatedKeyLoadPublicChecksProtocol.CreateHttp(app, liveNode);
        var sdk = new KeyLoadClient(http, admin, ComparisonClientOptions.Execution());
        string? writeCode;
        using (var attempt = IsolatedKeyLoadFaultChecksProtocol.Deadline(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultAttemptTimeout, token))
        {
            var read = await sdk.GetAsync(seed.Sentinel, attempt.Token);
            attempt.Token.ThrowIfCancellationRequested();
            await IsolatedKeyLoadPublicChecksAssertions.ErrorAsync(read, ErrorCode.OwnershipLost, admin);
        }
        using (var attempt = IsolatedKeyLoadFaultChecksProtocol.Deadline(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultAttemptTimeout, token))
        {
            var write = await sdk.CommitAsync(command, attempt.Token);
            attempt.Token.ThrowIfCancellationRequested();
            writeCode = write.Problem?.ErrorCode;
            await Assert.That(write.IsSuccess).IsFalse();
            var expected = hasSurvivor && writeCode == nameof(ErrorCode.OwnershipLost)
                ? ErrorCode.OwnershipLost : ErrorCode.UnknownWriteOutcome;
            await IsolatedKeyLoadPublicChecksAssertions.ErrorAsync(write, expected, admin);
        }
        return new(writeCode!, hasSurvivor && await McpAsync(app, liveNode, admin, seed, token));
    }

    private static async Task<bool> McpAsync(DistributedApplication app, int node, string admin,
        IsolatedKeyLoadFaultChecksSeed seed, CancellationToken token)
    {
        using var attempt = IsolatedKeyLoadFaultChecksProtocol.Deadline(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultAttemptTimeout, token);
        try
        {
            await using var mcp = await IsolatedKeyLoadPublicChecksMcp.ConnectAsync(app, node, admin, attempt.Token);
            await mcp.ErrorAsync(IsolatedKeyLoadPublicChecksProtocol.Get, new GetDocumentRequest(seed.Sentinel),
                ErrorCode.OwnershipLost, attempt.Token, admin);
            attempt.Token.ThrowIfCancellationRequested();
            return false;
        }
        catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.ServiceUnavailable)
        {
            attempt.Token.ThrowIfCancellationRequested();
            return true;
        }
    }

    internal static async Task<CommitReceipt> ResolveAsync(KeyLoadClient sdk, CommandRequest command, CancellationToken token)
    {
        using var deadline = IsolatedKeyLoadFaultChecksProtocol.Deadline(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultReadinessTimeout, token);
        while (true)
        {
            using var attempt = IsolatedKeyLoadFaultChecksProtocol.Deadline(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultAttemptTimeout, deadline.Token);
            var result = await sdk.CommitAsync(command, attempt.Token);
            attempt.Token.ThrowIfCancellationRequested();
            if (result.IsSuccess)
            {
                return result.Value!;
            }
            IsolatedKeyLoadFaultChecksProtocol.Require(result.Problem?.ErrorCode is nameof(ErrorCode.OwnershipLost)
                or nameof(ErrorCode.UnknownWriteOutcome));
            await Task.Delay(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultPollInterval, TimeProvider.System, deadline.Token);
        }
    }
}
