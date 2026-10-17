using KeyLoad.IntegrationTests.Features.BackupRestore;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class UnacknowledgedArchiveSeed
{
    internal static async Task RunAsync(RequestCqrsRf3Callers callers, ClusterRestoreMixedRetentionRf3State original,
        CancellationToken token)
    {
        var state = new UnacknowledgedArchiveState(original);
        original.UnackedRetention = state;
        var definition = new ResourceDefinition(state.Source.Resource, ResourceKind.Topic,
            original.Partition.TransactionDomainId);
        var configuredResource = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureResourceAsync(Guid.NewGuid(),
            new(original.Partition.TenantId, original.Partition.DatabaseId, definition), token));
        await SqlRf3Protocol.EqualAsync(definition, configuredResource);
        await UnacknowledgedArchiveActions.ChangedAsync(callers, state, token);
        state.Publish = new(Guid.NewGuid(), original.Partition,
            [new PublishTopic(state.Source.Resource, [.. UnacknowledgedArchiveProtocol.Events()])]);
        state.Published = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.CommitAsync(state.Publish, token));
        state.Tail = UnacknowledgedArchiveProtocol.ThirdPosition;
        await UnacknowledgedArchiveCurrent.ReceiptAsync(state, state.Published, state.Publish.CommandId,
            new(UnacknowledgedArchiveProtocol.PublishKind, state.Source.Resource, UnacknowledgedArchiveProtocol.ThirdEventId, UnacknowledgedArchiveProtocol.ThirdPosition));
        var page = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ReadEventSourceAsync(new(state.Source), token));
        await ClusterRestoreMixedRetentionRf3Literal.PageAsync(page, state.Source, UnacknowledgedArchiveProtocol.Events(), UnacknowledgedArchiveProtocol.FirstPosition, UnacknowledgedArchiveProtocol.InitialGeneration);
        state.Rows = page.Events;
        await UnacknowledgedArchiveActions.ChangedAsync(callers, state, token);
        await UnacknowledgedArchiveReplay.SuccessAsync(callers, state, McpCallerTools.DocumentsCommit,
            state.Publish, state.Published, () => callers.Sdk.CommitAsync(state.Publish, token), token);
        state.Replays.Add((McpCallerTools.DocumentsCommit, state.Publish, state.Published));
        var configure = new ConfigureSubscriptionRequest(Guid.NewGuid(), state.Group, state.Definition);
        var configured = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.ConfigureSubscriptionAsync(configure, token));
        state.HasGroup = true;
        await SqlRf3Protocol.EqualAsync(UnacknowledgedArchiveCurrent.Expected(state), configured);
        await UnacknowledgedArchiveActions.ChangedAsync(callers, state, token);
        await UnacknowledgedArchiveReplay.SuccessAsync(callers, state, McpCallerTools.SubscriptionsConfigure,
            configure, configured, () => callers.Sdk.ConfigureSubscriptionAsync(configure, token), token);
        state.Replays.Add((McpCallerTools.SubscriptionsConfigure, configure, configured));
        await UnacknowledgedArchiveReceive.RunAsync(callers, state, token);
        state.SourceReceive = state.Receive;
        state.SourceReceived = state.Received;
        await UnacknowledgedArchiveActions.AckAsync(callers, state, UnacknowledgedArchiveProtocol.ThirdDelivery, UnacknowledgedArchiveProtocol.BeforeFirst, token);
        await UnacknowledgedArchiveActions.PauseAsync(callers, state, true, token);
        var refusal = UnacknowledgedArchiveActions.Refusal(state);
        state.SourceFailures.Add(refusal);
        await UnacknowledgedArchiveActions.RefusedAsync(callers, state, refusal, token);
        await UnacknowledgedArchiveReplay.AllAsync(callers, state, token);
    }
}
