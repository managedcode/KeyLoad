using System.Collections.Immutable;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal static class QueueViewLeaseIsolationImages
{
    internal static async Task<QueueViewLeaseIsolationImage> ReadAsync(RequestCqrsRf3Callers root,
        QueueViewLeaseIsolationState state, CancellationToken token)
    {
        var ids = new[] { SqlModelViewRf3Scenario.FirstMessageId, SqlModelViewRf3Scenario.SecondMessageId };
        if (state.Healthy)
        {
            ids = [.. ids, QueueViewLeaseIsolationProtocol.Healthy];
        }
        var messages = new List<MessageInspection>();
        foreach (var id in ids)
        {
            messages.Add(await PublicReadAssertions.MessageAsync(await root.Sdk.InspectAsync(state.Scenario.Inspect(id), token))
                ?? throw new InvalidOperationException(QueueViewLeaseIsolationProtocol.Absent));
        }
        var source = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadStreamAsync(
            new(state.Scenario.Stream, Limit: QueueViewLeaseIsolationProtocol.SourceLimit), token));
        await Assert.That(source.CutPosition).IsGreaterThan((long)QueueViewLeaseIsolationProtocol.Empty);
        await Assert.That(source.SnapshotCutPosition).IsEqualTo(source.CutPosition);
        await SqlRf3Protocol.EqualAsync(state.Scenario.Stream, source.Stream);
        await Assert.That(source.HasMore).IsFalse();
        await Assert.That(source.Cursor).IsNull();
        var ordinal = QueueViewLeaseIsolationProtocol.InitialVersion;
        foreach (var item in source.Events)
        {
            await SqlRf3Protocol.EqualAsync(state.Scenario.Stream, item.Stream);
            await Assert.That(item.Revision).IsEqualTo(ordinal);
            await Assert.That(item.EventSequence).IsEqualTo(ordinal);
            ordinal++;
        }
        await Assert.That(source.Events.Length).IsEqualTo(SqlModelViewRf3Scenario.ExpectedEvents.Length);
        await SqlRf3Protocol.EqualAsync(new StreamHead(QueueViewLeaseIsolationProtocol.LeasedVersion,
            QueueViewLeaseIsolationProtocol.InitialVersion, QueueViewLeaseIsolationProtocol.InitialVersion), source.Head);
        await SqlRf3Protocol.EqualAsync(SqlModelViewRf3Scenario.ExpectedEvents, source.Events.Select(item => item.Data).ToImmutableArray());
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
        return new([.. messages], source with
        {
            CutPosition = QueueViewLeaseIsolationProtocol.Empty,
            SnapshotCutPosition = QueueViewLeaseIsolationProtocol.Empty
        }, outbox);
    }

    internal static async Task ObserveAsync(RequestCqrsRf3Callers root, QueueViewLeaseIsolationState state,
        Func<Task> operation, CancellationToken token)
    {
        var before = await ReadAsync(root, state, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await SqlRf3Protocol.EqualAsync(before, await ReadAsync(root, state, token)), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task AssertLeasedAsync(RequestCqrsRf3Callers root, QueueViewLeaseIsolationState state, CancellationToken token)
    {
        var image = await ReadAsync(root, state, token);
        var first = image.Messages[QueueViewLeaseIsolationProtocol.Empty];
        await SqlRf3Protocol.EqualAsync(new MessageMetadata(SqlModelViewRf3Scenario.FirstMessageId,
            MessageState.Leased, QueueViewLeaseIsolationProtocol.One, state.Version, QueueViewLeaseIsolationProtocol.InitialVersion,
            SqlModelViewRf3Scenario.NotBefore, null, QueueViewLeaseIsolationProtocol.Administrator,
            QueueViewLeaseIsolationProtocol.InitialVersion, state.Delivery.LeaseUntil), first.Metadata);
        await Assert.That(first.PayloadJson == state.Delivery.PayloadJson && first.HeadersJson == state.Delivery.HeadersJson).IsTrue();
        var second = image.Messages[QueueViewLeaseIsolationProtocol.One];
        await Assert.That(second.PayloadJson == QueueViewLeaseIsolationProtocol.SecondPayload
            && second.HeadersJson == QueueViewLeaseIsolationProtocol.SecondHeaders).IsTrue();
        await SqlRf3Protocol.EqualAsync(new MessageMetadata(SqlModelViewRf3Scenario.SecondMessageId,
            MessageState.Ready, QueueViewLeaseIsolationProtocol.Empty, QueueViewLeaseIsolationProtocol.InitialVersion,
            QueueViewLeaseIsolationProtocol.LeasedVersion, SqlModelViewRf3Scenario.NotBefore, null), second.Metadata);
    }
}
