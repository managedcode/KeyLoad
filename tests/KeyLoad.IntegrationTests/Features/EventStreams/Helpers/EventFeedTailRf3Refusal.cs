using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using ManagedCode.Communication.CQRS;
using Microsoft.Extensions.Options;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class EventFeedTailRf3Refusal
{
    private const int NoFrames = 0;
    private const int OneFailure = 1;
    private const string Tool = "keyload_tail_event_feed";
    private const string Invalid = "The original refused Tail frame is not a typed failure.";

    internal static async Task RunAsync(TwoRf3MembershipWave wave, string credential,
        EventFeedControlRequest offered, EventFeedControlResult original, CancellationToken token)
    {
        var request = EventFeedTailRf3Flow.Request(offered, original);
        var policy = new CqrsStreamClientOptions { MaximumStreamBytes = new DatabaseLimits().MaxQueryReadBytes };
        using var http = McpCallerHttp.Create(wave.Application, TwoRf3MembershipProtocol.Node1);
        var sdk = new KeyLoadClient(http, credential, IntegrationClientOptions.Execution(), Options.Create(policy));
        var failures = NoFrames;
        await foreach (var frame in sdk.TailEventFeedAsync(request, token).ConfigureAwait(false))
        {
            await Assert.That(frame.Kind).IsEqualTo(CqrsStreamChunkKind.Failed);
            if (frame.Final is not { IsSuccess: false, Problem: { } problem })
            { throw new InvalidOperationException(Invalid); }
            await Assert.That(problem.ErrorCode).IsEqualTo(ErrorCode.PermissionDenied.ToString());
            await Assert.That(problem.StatusCode).IsEqualTo(Errors.Status(ErrorCode.PermissionDenied));
            await Assert.That(frame.ProgressResult.HasValue).IsFalse();
            ++failures;
        }
        await Assert.That(failures).IsEqualTo(OneFailure);
        await using var caller = await McpOfficialClient.ConnectAsync(wave.Application,
            TwoRf3MembershipProtocol.Node1, credential, token).ConfigureAwait(false);
        var progress = new EventFeedTailRf3Progress(policy.MaximumFrameBytes);
        var terminal = await caller.Client.CallToolAsync(McpCallerProtocol.GatewayInvoke,
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [McpCallerProtocol.ToolId] = Tool,
                [McpCallerProtocol.Arguments] = McpOfficialClient.Arguments(request with { CommandId = Guid.NewGuid() })
            }, progress: progress, cancellationToken: token).ConfigureAwait(false);
        await McpCallerAssertions.ErrorAsync(terminal, ErrorCode.PermissionDenied, dispatched: true);
        await progress.RequireAbsentAsync();
    }
}
