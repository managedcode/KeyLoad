using KeyLoad.Clustering;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class NativeSubmissionFaultRf3Flow
{
    internal static async Task ExecuteAsync(NativeSubmissionFaultRf3Owner owner, bool officialMcp, CancellationToken token)
    {
        var wave = owner.Wave ?? throw NativeSubmissionFaultRf3Owner.Missing();
        var profile = owner.Profile ?? throw NativeSubmissionFaultRf3Owner.Missing();
        var identity = owner.Identity ?? throw NativeSubmissionFaultRf3Owner.Missing();
        var controls = owner.Controls ?? throw NativeSubmissionFaultRf3Owner.Missing();
        var originalStatus = await NodeEpochRf3StatusOracle.CaptureAsync(wave.App, profile, token).ConfigureAwait(false);
        var leader = NativeSubmissionFaultRf3Assertions.Leader(originalStatus);
        owner.Discovery = await ReadDiscoveryAsync(owner, token).ConfigureAwait(false);
        var nativeLeader = owner.Discovery.Single(value => value.VoterId == leader.Admin.LocalVoter);
        await owner.ConnectCallerAsync(leader.Name, token).ConfigureAwait(false);
        var caller = owner.Caller ?? throw NativeSubmissionFaultRf3Owner.Missing();
        var oracle = await RequestCqrsFaultReceiptOracle.CaptureAsync(caller, identity.Partition,
            RequestCqrsRf3Protocol.AdminCollection, RequestCqrsRf3Protocol.DocumentId,
            RequestCqrsRf3Protocol.DocumentJson, RequestCqrsRf3Protocol.ChangedDocumentJson, token).ConfigureAwait(false);
        var command = RequestCqrsPhaseFaultProvisioning.UpdateCommand(identity, Guid.NewGuid());
        owner.SourceArm = controls.WriteArm(identity.PrincipalId, command.CommandId, null,
            RequestCqrsProbePhase.BeforeSubmit, RequestCqrsProbeAction.Hold);
        StartOriginal(owner, officialMcp, command);
        var source = await controls.WaitForMarkerAsync(owner.SourceArm, RequestCqrsProbePhase.BeforeSubmit,
            RequestCqrsProbeOutcome.Observed, owner.Discovery, token).ConfigureAwait(false);
        await NativeSubmissionFaultRf3Assertions.SourceAsync(source, command, nativeLeader).ConfigureAwait(false);
        var heldStatus = await NodeEpochRf3StatusOracle.CaptureAsync(wave.App, profile, token).ConfigureAwait(false);
        await NodeEpochRf3StatusOracle.AssertSamePhysicalTopologyAsync(originalStatus, heldStatus).ConfigureAwait(false);
        await Assert.That(NativeSubmissionFaultRf3Assertions.Leader(heldStatus).Admin.LocalVoter)
            .IsEqualTo(nativeLeader.VoterId);
        var current = (await ReadDiscoveryAsync(owner, token).ConfigureAwait(false))
            .Single(value => value.VoterId == nativeLeader.VoterId);
        await Assert.That(current.SiloAddress).IsEqualTo(nativeLeader.SiloAddress);
        owner.DescendantArm = controls.WriteArm(identity.PrincipalId, command.CommandId, null,
            RequestCqrsProbePhase.ReplicaEntryAcknowledged, RequestCqrsProbeAction.ThrowOrdinary,
            partition: identity.Partition, sourceRequestId: source.RequestId,
            targetVoter: nativeLeader.VoterId, sourceArmId: owner.SourceArm);
        controls.WriteRelease(owner.SourceArm, source.RequestId);
        var acknowledged = await controls.WaitForMarkerAsync(owner.DescendantArm,
            RequestCqrsProbePhase.ReplicaEntryAcknowledged, RequestCqrsProbeOutcome.FaultRequested,
            owner.Discovery, token).ConfigureAwait(false);
        await NativeSubmissionFaultRf3Assertions.DescendantAsync(source, acknowledged, owner.DescendantArm)
            .ConfigureAwait(false);
        await owner.Original.ConfigureAwait(false);
        await (owner.AssertOriginal ?? throw NativeSubmissionFaultRf3Owner.Missing())().ConfigureAwait(false);
        await JoinAndRetireAsync(owner, source, acknowledged, token).ConfigureAwait(false);
        var retry = await oracle.RetryAndVerifyAsync(caller, owner.Administrator ?? throw NativeSubmissionFaultRf3Owner.Missing(),
            command, token).ConfigureAwait(false);
        await NativeSubmissionFullOutcomeInspection.RequireAsync(owner, command, retry, originalStatus, acknowledged, token)
            .ConfigureAwait(false);
    }

    private static void StartOriginal(NativeSubmissionFaultRf3Owner owner, bool officialMcp, CommandRequest command)
    {
        var caller = owner.Caller ?? throw NativeSubmissionFaultRf3Owner.Missing();
        if (officialMcp)
        {
            var original = RequestCqrsFaultCallers.CommitMcpAsync(caller.Mcp, command, owner.OperationToken);
            owner.McpCall = original;
            owner.AssertOriginal = async () =>
            {
                var observation = await original.ConfigureAwait(false);
                await Assert.That(observation.TransportFailure).IsNull();
                var result = observation.ToolResult ?? throw NativeSubmissionFaultRf3Owner.Missing();
                var requestId = await McpCallerAssertions.ErrorAsync(result, ErrorCode.UnknownWriteOutcome, dispatched: true);
                await Assert.That(requestId).IsEqualTo(owner.Controls!.ArmFor(owner.SourceArm).RequestId!.Value);
            };
            return;
        }
        var sdk = RequestCqrsFaultCallers.CommitSdkAsync(caller.Sdk, command, owner.OperationToken);
        owner.SdkCall = sdk;
        owner.AssertOriginal = async () =>
        {
            var result = await sdk.ConfigureAwait(false);
            await RequestCqrsFaultOutcome.AssertSdkUnknownWriteAsync(result).ConfigureAwait(false);
            await Assert.That(result.Value).IsNull();
        };
    }

    private static async Task JoinAndRetireAsync(NativeSubmissionFaultRf3Owner owner,
        RequestCqrsProbeMarkerRecord source, RequestCqrsProbeMarkerRecord acknowledged, CancellationToken token)
    {
        var controls = owner.Controls ?? throw NativeSubmissionFaultRf3Owner.Missing();
        var disposed = await controls.WaitForMarkerAsync(owner.DescendantArm, RequestCqrsProbePhase.ReplicaEntryObserverDisposed,
            RequestCqrsProbeOutcome.Observed, owner.Discovery, token).ConfigureAwait(false);
        await NativeSubmissionFaultRf3Assertions.DisposedAsync(acknowledged, disposed).ConfigureAwait(false);
        await RequestCqrsPhaseFaultAssertions.VerifySettledAsync(controls, owner.SourceArm, source.RequestId,
            source.CommandId, owner.Discovery, token).ConfigureAwait(false);
        await controls.RetireArmAsync(owner.DescendantArm, token).ConfigureAwait(false);
        await controls.RetireArmAsync(owner.SourceArm, token).ConfigureAwait(false);
    }

    private static async Task<ReplicaSiloDiscovery[]> ReadDiscoveryAsync(NativeSubmissionFaultRf3Owner owner, CancellationToken token)
    {
        var records = new ReplicaSiloDiscovery[RequestCqrsRf3Protocol.NodeCount];
        for (var index = 0; index < records.Length; index++)
        {
            records[index] = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(
                (owner.Wave ?? throw NativeSubmissionFaultRf3Owner.Missing()).App, RequestCqrsRf3Protocol.NodeName(index),
                owner.Profile ?? throw NativeSubmissionFaultRf3Owner.Missing(), token).ConfigureAwait(false);
        }
        return records;
    }
}
