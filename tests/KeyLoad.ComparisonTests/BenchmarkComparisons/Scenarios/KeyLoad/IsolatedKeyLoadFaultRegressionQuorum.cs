using System.Net;
using Aspire.Hosting;
using KeyLoad.Client;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-005: loss of actual majority never yields a public cut or ACK.</summary>
internal static class IsolatedKeyLoadFaultRegressionQuorum
{
    internal static async Task<IsolatedKeyLoadFaultRegressionRejection> RejectAsync(DistributedApplication app, int liveNode, string admin,
        IsolatedKeyLoadFaultRegressionSeed seed, CommandRequest command, bool hasSurvivor, CancellationToken token)
    {
        using var http = IsolatedKeyLoadPublicRegressionProtocol.CreateHttp(app, liveNode);
        var sdk = new KeyLoadClient(http, admin, ComparisonClientOptions.Execution());
        string? writeCode;
        using (var attempt = IsolatedKeyLoadFaultRegressionProtocol.Deadline(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultAttemptTimeout, token))
        {
            var read = await sdk.GetAsync(seed.Sentinel, attempt.Token);
            attempt.Token.ThrowIfCancellationRequested();
            await IsolatedKeyLoadPublicRegressionAssertions.ErrorAsync(read, ErrorCode.OwnershipLost, admin);
        }
        using (var attempt = IsolatedKeyLoadFaultRegressionProtocol.Deadline(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultAttemptTimeout, token))
        {
            var write = await sdk.CommitAsync(command, attempt.Token);
            attempt.Token.ThrowIfCancellationRequested();
            writeCode = write.Problem?.ErrorCode;
            await Assert.That(write.IsSuccess).IsFalse();
            var expected = hasSurvivor && writeCode == nameof(ErrorCode.OwnershipLost)
                ? ErrorCode.OwnershipLost : ErrorCode.UnknownWriteOutcome;
            await IsolatedKeyLoadPublicRegressionAssertions.ErrorAsync(write, expected, admin);
        }
        return new(writeCode!, hasSurvivor && await McpAsync(app, liveNode, admin, seed, token));
    }

    private static async Task<bool> McpAsync(DistributedApplication app, int node, string admin,
        IsolatedKeyLoadFaultRegressionSeed seed, CancellationToken token)
    {
        using var attempt = IsolatedKeyLoadFaultRegressionProtocol.Deadline(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultAttemptTimeout, token);
        try
        {
            await using var mcp = await IsolatedKeyLoadPublicRegressionMcp.ConnectAsync(app, node, admin, attempt.Token);
            await mcp.ErrorAsync(IsolatedKeyLoadPublicRegressionProtocol.Get, new GetDocumentRequest(seed.Sentinel),
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
        using var deadline = IsolatedKeyLoadFaultRegressionProtocol.Deadline(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultReadinessTimeout, token);
        while (true)
        {
            using var attempt = IsolatedKeyLoadFaultRegressionProtocol.Deadline(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultAttemptTimeout, deadline.Token);
            var result = await sdk.CommitAsync(command, attempt.Token);
            attempt.Token.ThrowIfCancellationRequested();
            if (result.IsSuccess)
            {
                return result.Value!;
            }
            IsolatedKeyLoadFaultRegressionProtocol.Require(result.Problem?.ErrorCode is nameof(ErrorCode.OwnershipLost)
                or nameof(ErrorCode.UnknownWriteOutcome));
            await Task.Delay(NativeExecutionPolicyFixture.Harness().Value.KeyLoadFaultPollInterval, TimeProvider.System, deadline.Token);
        }
    }
}

internal sealed record IsolatedKeyLoadFaultRegressionRejection(string WriteCode, bool NativeMcpAuthentication503);
