using System.Net;
using Aspire.Hosting;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class DueNoQuorumRf3Assertions
{
    private const string Incomplete = "The recurring schedule did not settle at its expected canonical ordinal.";

    internal static async Task AssertOrdinalAsync(DueNoQuorumRf3Callers callers, DueNoQuorumRf3Seed seed,
        long expectedOrdinal, CancellationToken cancellationToken)
    {
        var request = new InspectRecurringScheduleRequest(seed.Lane, seed.ScheduleId);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.InspectRecurringScheduleAsync(request,
            cancellationToken).ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<RecurringScheduleInspection?>(await callers.Mcp.CallAsync(
            DueNoQuorumRf3Protocol.InspectScheduleTool, request, cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
        await Assert.That(sdk).IsNotNull();
        await Assert.That(mcp.Value).IsNotNull();
        await Assert.That(sdk!.Revision).IsEqualTo(1L);
        await Assert.That(sdk.Generation).IsEqualTo(1L);
        await Assert.That(sdk.NextOrdinal).IsEqualTo(expectedOrdinal);
        await Assert.That(sdk.Cancelled).IsFalse();
        await Assert.That(sdk.Definition).IsEqualTo(seed.Definition);
        await Assert.That(sdk.Redacted).IsFalse();
        await Assert.That(sdk.Definition.PayloadJson).IsEqualTo(DueNoQuorumRf3Protocol.Payload);
        await Assert.That(sdk.Definition.HeadersJson).IsEqualTo(DueNoQuorumRf3Protocol.Headers);
        await Assert.That(JsonDefaults.Serialize(sdk).AsSpan().SequenceEqual(JsonDefaults.Serialize(mcp.Value!))).IsTrue();
    }

    internal static async Task AssertOccurrenceAsync(DueNoQuorumRf3Callers callers, DueNoQuorumRf3Seed seed,
        bool expectedPresent, CancellationToken cancellationToken)
    {
        var request = new InspectMessageRequest(seed.Lane, seed.OccurrenceId);
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.InspectAsync(request, cancellationToken)
            .ConfigureAwait(false)).ConfigureAwait(false);
        var mcp = await McpCallerAssertions.SuccessAsync<MessageInspection?>(await callers.Mcp.CallAsync(
            DueNoQuorumRf3Protocol.InspectMessageTool, request, cancellationToken).ConfigureAwait(false))
            .ConfigureAwait(false);
        await Assert.That(sdk is not null).IsEqualTo(expectedPresent);
        await Assert.That(mcp.Value is not null).IsEqualTo(expectedPresent);
        if (sdk is not null && mcp.Value is not null)
        {
            await Assert.That(sdk.Metadata.Id).IsEqualTo(seed.OccurrenceId);
            await Assert.That(sdk.Metadata.State).IsEqualTo(MessageState.Ready);
            await Assert.That(sdk.Metadata.NotBefore).IsEqualTo(seed.DueAt);
            await Assert.That(sdk.PayloadJson).IsEqualTo(DueNoQuorumRf3Protocol.Payload);
            await Assert.That(sdk.HeadersJson).IsEqualTo(DueNoQuorumRf3Protocol.Headers);
            await Assert.That(JsonDefaults.Serialize(sdk).AsSpan().SequenceEqual(JsonDefaults.Serialize(mcp.Value))).IsTrue();
        }
    }

    internal static async Task AssertNoQuorumAsync(DistributedApplication app, string survivor,
        DueNoQuorumRf3Seed seed, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(app, survivor);
        using (var response = await http.GetAsync(RequestCqrsRf3Protocol.ReadyUri, cancellationToken).ConfigureAwait(false))
        { await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable); }
        var sdk = new KeyLoadClient(http, seed.Creator.Secret, IntegrationClientOptions.Execution());
        var result = await sdk.InspectRecurringScheduleAsync(new(seed.Lane, seed.ScheduleId), cancellationToken)
            .ConfigureAwait(false);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.OwnershipLost));
        McpOfficialClient? ownedMcp = null;
        var failures = new List<Exception>();
        try
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                McpOfficialClient connected;
                try
                {
                    connected = await McpOfficialClient.ConnectAsync(app, survivor, seed.Creator.Secret,
                        cancellationToken).ConfigureAwait(false);
                }
                catch (HttpRequestException error) when (error.StatusCode == HttpStatusCode.ServiceUnavailable)
                { return; }
                ownedMcp = connected;
                var reply = await connected.CallAsync(DueNoQuorumRf3Protocol.InspectScheduleTool,
                    new InspectRecurringScheduleRequest(seed.Lane, seed.ScheduleId), cancellationToken)
                    .ConfigureAwait(false);
                await McpCallerAssertions.ErrorAsync(reply, ErrorCode.OwnershipLost, dispatched: false)
                    .ConfigureAwait(false);
                await McpCallerAssertions.DoesNotDiscloseAsync(reply, seed.Creator.Secret,
                    DueNoQuorumRf3Protocol.Payload).ConfigureAwait(false);
                await McpCallerAssertions.DoesNotDiscloseAsync(reply, DueNoQuorumRf3Protocol.Headers,
                    DueNoQuorumRf3Protocol.Headers).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }
        finally
        {
            if (ownedMcp is not null)
            { await ServerFailureObserver.ObserveAsync(() => ownedMcp.DisposeAsync().AsTask(), failures).ConfigureAwait(false); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task WaitForOrdinalOneAsync(DueNoQuorumRf3Callers callers, DueNoQuorumRf3Seed seed,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(DueNoQuorumRf3Protocol.ProgressDeadline);
        while (true)
        {
            var read = await callers.Sdk.InspectRecurringScheduleAsync(new(seed.Lane, seed.ScheduleId), deadline.Token)
                .ConfigureAwait(false);
            if (read.IsFailed)
            {
                await Assert.That(read.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.OwnershipLost));
            }
            else if (read.Value is { NextOrdinal: > 1 })
            { throw new InvalidOperationException(Incomplete); }
            if (read.IsSuccess && read.Value is { NextOrdinal: 1 })
            {
                await AssertOrdinalAsync(callers, seed, 1, deadline.Token).ConfigureAwait(false);
                await AssertOccurrenceAsync(callers, seed, expectedPresent: true, deadline.Token).ConfigureAwait(false);
                await AssertOccurrenceAsync(callers, seed with { OccurrenceId = seed.NextOccurrenceId },
                    expectedPresent: false, deadline.Token).ConfigureAwait(false);
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(DueNoQuorumRf3Protocol.PollMilliseconds), deadline.Token)
                .ConfigureAwait(false);
        }
    }

    internal static async Task AssertRecoveredClientsAsync(DueNoQuorumRf3Callers callers,
        DueNoQuorumRf3Seed seed, CancellationToken cancellationToken)
    {
        await AssertOrdinalAsync(callers, seed, 1, cancellationToken).ConfigureAwait(false);
        await AssertOccurrenceAsync(callers, seed, expectedPresent: true, cancellationToken).ConfigureAwait(false);
        await AssertOccurrenceAsync(callers, seed with { OccurrenceId = seed.NextOccurrenceId },
            expectedPresent: false, cancellationToken).ConfigureAwait(false);
    }

}
