using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class UnacknowledgedArchiveRepair
{
    internal static async Task RunAsync(RequestCqrsRf3Callers callers, UnacknowledgedArchiveState state,
        CancellationToken token)
    {
        var seek = new SeekSubscriptionRequest(Guid.NewGuid(), state.Group, UnacknowledgedArchiveProtocol.InitialGeneration, SubscriptionStart.FromBeginning);
        var result = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.SeekSubscriptionAsync(seek, token));
        state.Generation = UnacknowledgedArchiveProtocol.RepairedGeneration;
        state.Issued = UnacknowledgedArchiveProtocol.BeforeFirst;
        await SqlRf3Protocol.EqualAsync(UnacknowledgedArchiveCurrent.Expected(state), result);
        await UnacknowledgedArchiveActions.ChangedAsync(callers, state, token);
        await UnacknowledgedArchiveReplay.SuccessAsync(callers, state, McpCallerTools.SubscriptionsSeek,
            seek, result, () => callers.Sdk.SeekSubscriptionAsync(seek, token), token);
        state.Replays.Add((McpCallerTools.SubscriptionsSeek, seek, result));
        await UnacknowledgedArchiveActions.PauseAsync(callers, state, false, token);
        await UnacknowledgedArchiveReceive.RunAsync(callers, state, token);
        await UnacknowledgedArchiveActions.AckAsync(callers, state, UnacknowledgedArchiveProtocol.ThirdDelivery, UnacknowledgedArchiveProtocol.BeforeFirst, token);
        await UnacknowledgedArchiveActions.AckAsync(callers, state, UnacknowledgedArchiveProtocol.FirstDelivery, UnacknowledgedArchiveProtocol.FirstPosition, token);
        var second = UnacknowledgedArchiveActions.Refusal(state);
        state.CurrentFailures.Add(second);
        await UnacknowledgedArchiveActions.RefusedAsync(callers, state, second, token);
        await UnacknowledgedArchiveActions.AckAsync(callers, state, UnacknowledgedArchiveProtocol.SecondDelivery, UnacknowledgedArchiveProtocol.ThirdPosition, token);
        await PurgeAsync(callers, state, token);
        await HealthyAsync(callers, state, token);
    }

    private static async Task PurgeAsync(RequestCqrsRf3Callers callers, UnacknowledgedArchiveState state,
        CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Original.Partition, [new PurgeTopic(state.Source.Resource, UnacknowledgedArchiveProtocol.PurgeThrough)]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, token));
        await UnacknowledgedArchiveCurrent.ReceiptAsync(state, receipt, command.CommandId,
            new(UnacknowledgedArchiveProtocol.PurgeKind, state.Source.Resource, UnacknowledgedArchiveProtocol.PurgeThroughText, UnacknowledgedArchiveProtocol.PurgeThrough));
        state.First = UnacknowledgedArchiveProtocol.ThirdPosition;
        state.Rows = [state.Rows[UnacknowledgedArchiveProtocol.ThirdDelivery]];
        await UnacknowledgedArchiveActions.ChangedAsync(callers, state, token);
        await UnacknowledgedArchiveReplay.SuccessAsync(callers, state, McpCallerTools.DocumentsCommit,
            command, receipt, () => callers.Sdk.CommitAsync(command, token), token);
        state.Replays.Add((McpCallerTools.DocumentsCommit, command, receipt));
        await UnacknowledgedArchiveReplay.AllAsync(callers, state, token);
        await UnacknowledgedArchiveHistory.RunAsync(callers, state, token);
    }

    private static async Task HealthyAsync(RequestCqrsRf3Callers callers, UnacknowledgedArchiveState state,
        CancellationToken token)
    {
        var data = UnacknowledgedArchiveProtocol.Healthy();
        var command = new CommandRequest(Guid.NewGuid(), state.Original.Partition, [new PublishTopic(state.Source.Resource, [data])]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(command, token));
        await UnacknowledgedArchiveCurrent.ReceiptAsync(state, receipt, command.CommandId,
            new(UnacknowledgedArchiveProtocol.PublishKind, state.Source.Resource, UnacknowledgedArchiveProtocol.HealthyEventId, UnacknowledgedArchiveProtocol.HealthyTail));
        var page = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadEventSourceAsync(new(state.Source, AfterPosition: UnacknowledgedArchiveProtocol.PurgeThrough), token));
        await SqlRf3Protocol.EqualAsync(state.Source, page.Source);
        await Assert.That(page.Cursor).IsNotEmpty();
        await Assert.That(page.CutPosition).IsGreaterThan(UnacknowledgedArchiveProtocol.PositivePositionBoundary);
        await Assert.That(page.HasMore).IsFalse();
        await SqlRf3Protocol.EqualAsync(new EventSourceHead(UnacknowledgedArchiveProtocol.HealthyTail, UnacknowledgedArchiveProtocol.ThirdPosition, UnacknowledgedArchiveProtocol.InitialGeneration), page.Head);
        await Assert.That(page.Events.Length).IsEqualTo(UnacknowledgedArchiveProtocol.HealthyRows);
        await SqlRf3Protocol.EqualAsync(state.Rows[UnacknowledgedArchiveProtocol.RetainedRow], page.Events[UnacknowledgedArchiveProtocol.RetainedRow]);
        var row = page.Events[UnacknowledgedArchiveProtocol.AddedRow];
        await Assert.That(row.EventSequence).IsGreaterThan(UnacknowledgedArchiveProtocol.PositivePositionBoundary);
        await Assert.That(row.RecordedAt).IsNotEqualTo(default(DateTimeOffset));
        await SqlRf3Protocol.EqualAsync(new SourceEventRecord(state.Source, UnacknowledgedArchiveProtocol.HealthyTail, row.EventSequence, data, row.RecordedAt), row);
        state.Rows = page.Events;
        state.Tail = UnacknowledgedArchiveProtocol.HealthyTail;
        await UnacknowledgedArchiveActions.ChangedAsync(callers, state, token);
        await UnacknowledgedArchiveReplay.SuccessAsync(callers, state, McpCallerTools.DocumentsCommit,
            command, receipt, () => callers.Sdk.CommitAsync(command, token), token);
        state.Replays.Add((McpCallerTools.DocumentsCommit, command, receipt));
    }
}
