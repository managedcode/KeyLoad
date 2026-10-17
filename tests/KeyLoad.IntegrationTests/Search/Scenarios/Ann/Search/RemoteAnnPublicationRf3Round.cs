using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class RemoteAnnPublicationRf3Round
{
    internal static async Task RunAsync(TwoRf3MembershipWave wave, string administrator,
        RemoteAnnPublicationRf3Reader reader, string ownerNode, string sourceNode,
        RemoteAnnPublicationRf3State state, long epoch, bool repairPolicy, CancellationToken token)
    {
        using var ownerHttp = McpCallerHttp.Create(wave.Application, ownerNode);
        using var sourceHttp = McpCallerHttp.Create(wave.Application, sourceNode);
        var owner = new KeyLoadClient(ownerHttp, administrator, IntegrationClientOptions.Execution());
        var adminSource = new KeyLoadClient(sourceHttp, administrator, IntegrationClientOptions.Execution());
        var destination = new KeyLoadClient(ownerHttp, reader.Secret, IntegrationClientOptions.Execution());
        var source = new KeyLoadClient(sourceHttp, reader.Secret, IntegrationClientOptions.Execution());
        await using var official = await McpOfficialClient.ConnectAsync(wave.Application, sourceNode, reader.Secret, token);
        await using var privileged = await McpOfficialClient.ConnectAsync(wave.Application, ownerNode, administrator, token);
        await NativeAnnMaintenanceRf3Assertions.CanonicalAsync(owner, privileged, state.Scenario, token);
        await RemoteAnnPublicationRf3Assertions.ReadsAsync(destination, source, official, owner, adminSource,
            state, epoch, token);
        var original = await McpCallerAssertions.SdkSuccessAsync(await owner.MaintainAnnIndexAsync(state.Original, token));
        await RemoteAnnPublicationRf3Assertions.EqualAsync(state.Completed, original);
        await RequireCancelledAsync(owner, source, state, token);
        if (!repairPolicy)
        {
            return;
        }
        var changed = await McpCallerAssertions.SdkStatusAsync(await owner.ConfigurePrincipalAsync(
            Guid.NewGuid(), reader.Denied(), token));
        await Assert.That(changed.IsSuccess).IsTrue();
        var before = await McpCallerAssertions.SdkSuccessAsync(await owner.OutboxStatusAsync(state.Scenario.Partition, token));
        var canonical = await McpCallerAssertions.SdkSuccessAsync(await owner.SearchAsync(state.Scenario.Search(), token));
        await RemoteAnnPublicationRf3Assertions.RefusedAsync(source, official, state, token);
        await RemoteAnnPublicationRf3Assertions.EqualAsync(before,
            await McpCallerAssertions.SdkSuccessAsync(await owner.OutboxStatusAsync(state.Scenario.Partition, token)));
        await RemoteAnnPublicationRf3Assertions.EqualAsync(canonical,
            await McpCallerAssertions.SdkSuccessAsync(await owner.SearchAsync(state.Scenario.Search(), token)));
        var restored = await McpCallerAssertions.SdkStatusAsync(await owner.ConfigurePrincipalAsync(
            Guid.NewGuid(), reader.Repaired(), token));
        await Assert.That(restored.IsSuccess).IsTrue();
        await RemoteAnnPublicationRf3Assertions.ReadsAsync(destination, source, official, owner, adminSource,
            state, RemoteAnnPublicationRf3Protocol.RepairedEpoch, token);
        await RemoteAnnPublicationRf3Assertions.EqualAsync(state.Completed,
            await McpCallerAssertions.SdkSuccessAsync(await owner.MaintainAnnIndexAsync(state.Original, token)));
        var noField = await McpCallerAssertions.SdkStatusAsync(await owner.ConfigurePrincipalAsync(
            Guid.NewGuid(), reader.FieldDenied(), token));
        await Assert.That(noField.IsSuccess).IsTrue();
        var fieldBefore = await McpCallerAssertions.SdkSuccessAsync(await owner.OutboxStatusAsync(state.Scenario.Partition, token));
        await RemoteAnnPublicationRf3Assertions.RefusedAsync(source, official, state, token);
        await RemoteAnnPublicationRf3Assertions.EqualAsync(fieldBefore,
            await McpCallerAssertions.SdkSuccessAsync(await owner.OutboxStatusAsync(state.Scenario.Partition, token)));
        await RemoteAnnPublicationRf3Assertions.EqualAsync(canonical,
            await McpCallerAssertions.SdkSuccessAsync(await owner.SearchAsync(state.Scenario.Search(), token)));
        var fieldRepair = await McpCallerAssertions.SdkStatusAsync(await owner.ConfigurePrincipalAsync(
            Guid.NewGuid(), reader.FieldRepaired(), token));
        await Assert.That(fieldRepair.IsSuccess).IsTrue();
        await RemoteAnnPublicationRf3Assertions.ReadsAsync(destination, source, official, owner, adminSource,
            state, RemoteAnnPublicationRf3Protocol.FieldRepairedEpoch, token);
        await NativeAnnMaintenanceRf3Assertions.CanonicalAsync(owner, privileged, state.Scenario, token);
    }

    internal static async Task RestoreAsync(TwoRf3MembershipWave wave, string administrator,
        string ownerNode, RemoteAnnPublicationRf3State state, CancellationToken token)
    {
        using var http = McpCallerHttp.Create(wave.Application, ownerNode);
        var sdk = new KeyLoadClient(http, administrator, IntegrationClientOptions.Execution());
        await RemoteAnnPublicationRf3Assertions.EqualAsync(state.SeedReceipt,
            await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(state.Seed, token)));
        var request = state.Original with
        {
            CommandId = Guid.NewGuid(),
            Mode = AnnMaintenanceMode.Restore
        };
        var restored = await McpCallerAssertions.SdkSuccessAsync(await sdk.MaintainAnnIndexAsync(request, token));
        await NativeAnnMaintenanceRf3Assertions.ResultAsync(restored, request);
        await Assert.That(restored.IndexSha256).IsEqualTo(state.Completed.IndexSha256);
        await Assert.That(restored.Source!.CorpusSha256).IsEqualTo(state.Completed.Source!.CorpusSha256);
        await RemoteAnnPublicationRf3Assertions.EqualAsync(state.Completed,
            await McpCallerAssertions.SdkSuccessAsync(await sdk.MaintainAnnIndexAsync(state.Original, token)));
    }

    private static async Task RequireCancelledAsync(KeyLoadClient owner, KeyLoadClient source,
        RemoteAnnPublicationRf3State state, CancellationToken token)
    {
        var cancelBefore = await McpCallerAssertions.SdkSuccessAsync(await owner.OutboxStatusAsync(state.Scenario.Partition, token));
        using (var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token))
        {
            await cancelled.CancelAsync();
            var cancellation = await source.ApproximateSearchAsync(state.Approximate(), cancelled.Token);
            await Assert.That(cancellation.IsSuccess).IsFalse();
            await Assert.That(cancellation.Value).IsNull();
            await Assert.That(cancellation.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Cancelled));
        }
        await RemoteAnnPublicationRf3Assertions.EqualAsync(cancelBefore,
            await McpCallerAssertions.SdkSuccessAsync(await owner.OutboxStatusAsync(state.Scenario.Partition, token)));
    }
}
