using System.Collections.Immutable;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.QueryExecution;

internal static class QueueViewLeaseIsolationImages
{
    internal static async Task<QueueViewLeaseIsolationImage> ReadAsync(RequestCqrsClusterCallers root,
        QueueViewLeaseIsolationState state, CancellationToken token)
    {
        var ids = new[] { SqlModelViewClusterScenario.FirstMessageId, SqlModelViewClusterScenario.SecondMessageId };
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
        await SqlClusterProtocol.EqualAsync(state.Scenario.Stream, source.Stream);
        await Assert.That(source.HasMore).IsFalse();
        await Assert.That(source.Cursor).IsNull();
        var ordinal = QueueViewLeaseIsolationProtocol.InitialVersion;
        foreach (var item in source.Events)
        {
            await SqlClusterProtocol.EqualAsync(state.Scenario.Stream, item.Stream);
            await Assert.That(item.Revision).IsEqualTo(ordinal);
            await Assert.That(item.EventSequence).IsEqualTo(ordinal);
            ordinal++;
        }
        await Assert.That(source.Events.Length).IsEqualTo(SqlModelViewClusterScenario.ExpectedEvents.Length);
        await SqlClusterProtocol.EqualAsync(new StreamHead(QueueViewLeaseIsolationProtocol.LeasedVersion,
            QueueViewLeaseIsolationProtocol.InitialVersion, QueueViewLeaseIsolationProtocol.InitialVersion), source.Head);
        await SqlClusterProtocol.EqualAsync(SqlModelViewClusterScenario.ExpectedEvents, source.Events.Select(item => item.Data).ToImmutableArray());
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.OutboxStatusAsync(state.Scenario.Partition, token));
        return new([.. messages], source with
        {
            CutPosition = QueueViewLeaseIsolationProtocol.Empty,
            SnapshotCutPosition = QueueViewLeaseIsolationProtocol.Empty
        }, outbox);
    }

    internal static async Task ObserveAsync(RequestCqrsClusterCallers root, QueueViewLeaseIsolationState state,
        Func<Task> operation, CancellationToken token)
    {
        var before = await ReadAsync(root, state, token);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
            await SqlClusterProtocol.EqualAsync(before, await ReadAsync(root, state, token)), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal static async Task AssertLeasedAsync(RequestCqrsClusterCallers root, QueueViewLeaseIsolationState state, CancellationToken token)
    {
        var image = await ReadAsync(root, state, token);
        var first = image.Messages[QueueViewLeaseIsolationProtocol.Empty];
        await SqlClusterProtocol.EqualAsync(new MessageMetadata(SqlModelViewClusterScenario.FirstMessageId,
            MessageState.Leased, QueueViewLeaseIsolationProtocol.One, state.Version, QueueViewLeaseIsolationProtocol.InitialVersion,
            SqlModelViewClusterScenario.NotBefore, null, QueueViewLeaseIsolationProtocol.Administrator,
            QueueViewLeaseIsolationProtocol.InitialVersion, state.Delivery.LeaseUntil), first.Metadata);
        await Assert.That(first.PayloadJson == state.Delivery.PayloadJson && first.HeadersJson == state.Delivery.HeadersJson).IsTrue();
        var second = image.Messages[QueueViewLeaseIsolationProtocol.One];
        await Assert.That(second.PayloadJson == QueueViewLeaseIsolationProtocol.SecondPayload
            && second.HeadersJson == QueueViewLeaseIsolationProtocol.SecondHeaders).IsTrue();
        await SqlClusterProtocol.EqualAsync(new MessageMetadata(SqlModelViewClusterScenario.SecondMessageId,
            MessageState.Ready, QueueViewLeaseIsolationProtocol.Empty, QueueViewLeaseIsolationProtocol.InitialVersion,
            QueueViewLeaseIsolationProtocol.LeasedVersion, SqlModelViewClusterScenario.NotBefore, null), second.Metadata);
    }
}
