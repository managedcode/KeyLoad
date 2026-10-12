using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferNativeReceiptAuthorityAssertions
{
    internal static async Task RequireAsync(TwoRf3MembershipWave wave,
        RemoteTransferNativeAdministrator administrator, RemoteTransferNativeCallers calls,
        RemoteTransferColdSeed seed, PrincipalRecord current, NativeCanonicalModelCutRequest scope,
        IReadOnlyDictionary<string, NativeCanonicalModelCutWitness> before, CommitReceipt accepted,
        CancellationToken token)
    {
        var denied = MessagingRf3Identity.WithCapability(current, seed.Scenario.DestinationQueue,
            RemoteTransferColdProtocol.Target & ~Capability.QueueInspect);
        await SqlRf3Protocol.EqualAsync(denied, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Source.ConfigurePrincipalAsync(Guid.NewGuid(), denied, token)));
        await calls.DeniedReadAsync(seed.Scenario.DestinationPartition, RemoteTransferColdProtocol.InspectReceipt,
            seed.ReceiptRequest, ct => calls.Sdk.InspectQueueTransferReceiptAsync(seed.ReceiptRequest, ct),
            ErrorCode.PermissionDenied, token);
        RemoteTransferNativeCutLifecycle.RequireUnchanged(before,
            await RemoteTransferNativeCutLifecycle.LiveAsync(wave, scope, token), token);
        var repaired = MessagingRf3Identity.WithCapability(denied, seed.Scenario.DestinationQueue,
            RemoteTransferColdProtocol.Target);
        await SqlRf3Protocol.EqualAsync(repaired, await McpCallerAssertions.SdkSuccessAsync(
            await administrator.Source.ConfigurePrincipalAsync(Guid.NewGuid(), repaired, token)));
        await RemoteTransferNativeStateAssertions.ReceiptAsync(calls, seed, accepted, token);
        RemoteTransferNativeCutLifecycle.RequireUnchanged(before,
            await RemoteTransferNativeCutLifecycle.LiveAsync(wave, scope, token), token);
    }
}
