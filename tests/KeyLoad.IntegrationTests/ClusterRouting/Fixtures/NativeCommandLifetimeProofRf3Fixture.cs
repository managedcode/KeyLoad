using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using P = KeyLoad.Server.Features.ClusterRouting.RequestCqrsControlledLifetimeProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class NativeCommandLifetimeProofRf3Fixture(TwoRf3MembershipWave wave,
    PartitionMovementPublicParentRf3Seed seed)
{
    private const int LastSdkStep = 2;

    internal Guid Arm(CommandRequest command, Guid? donor)
    {
        var arm = wave.QueryControls.WriteArm(PartitionMovementPublicParentRf3Administrator.PrincipalId,
            command.CommandId, null, RequestCqrsProbePhase.ControlledLifetimeReplayBeforeDispatch,
            RequestCqrsProbeAction.Hold, command.Partition, sourceArmId: donor);
        NativeCommandLifetimeStepFiles.Plan(wave, arm, donor is null
            ? ControlledLifetimeReplayVariation.Substituted : ControlledLifetimeReplayVariation.Foreign, donor);
        return arm;
    }

    internal async Task DriveAsync(PartitionMovementLifetimeProofRf3Original original,
        PartitionMovementLifetimeProofRf3Original? donor, CancellationToken token)
    {
        var owners = await TwoRf3MembershipFingerprintOracle.ObserveSixAsync(wave.Application, wave.Profile, token);
        var sourceOwners = owners.Where(owner => owner.Incarnation == wave.Profile.Incarnation).ToArray();
        await Assert.That(sourceOwners.Length).IsEqualTo(TwoRf3MembershipProtocol.MembersPerGroup);
        var marker = await MarkerAsync(original, sourceOwners, token);
        var last = donor is null ? LastSdkStep : P.LastNegativeStep;
        Dictionary<string, NativeCommandLifetimeCutWitness>? baseline = null;
        for (var step = P.BaselineStep; step <= last; step += P.IndexStep)
        {
            var observed = await NativeCommandLifetimeStepFiles.WaitAsync(wave, marker, step, original, token);
            await NativeCommandLifetimeCarrierAssertions.RequireAsync(wave, original, donor, marker, observed, owners);
            var actual = await NativeCommandLifetimeCutClient.ReadSixAsync(wave, seed.Credential,
                original.ArmId, original.Command, observed.EffectCommandId, token);
            await PartitionMovementLifetimeProofRf3Assertions.LiveAsync(original, observed, actual, baseline);
            baseline ??= actual;
            NativeCommandLifetimeStepFiles.Ack(wave, observed);
        }
        await wave.QueryControls.WaitForSettlementAsync(original.ArmId, sourceOwners, token);
    }

    internal async Task JoinProducerAsync(PartitionMovementLifetimeProofRf3Original original, CancellationToken token)
    {
        await original.Producer;
        var owners = await TwoRf3MembershipFingerprintOracle.ObserveSixAsync(wave.Application, wave.Profile, token);
        var source = owners.Where(owner => owner.Incarnation == wave.Profile.Incarnation).ToArray();
        var disposed = await wave.QueryControls.WaitForMarkerAsync(original.ArmId,
            RequestCqrsProbePhase.ProducerDisposed, RequestCqrsProbeOutcome.Observed, source, token);
        await Assert.That(disposed.RequestId).IsEqualTo(original.Baseline!.RequestId);
        await Assert.That(disposed.CommandId).IsEqualTo(original.Command.CommandId);
        await Assert.That(wave.QueryControls.ArmFor(original.ArmId).ProducerDisposedSeen).IsTrue();
    }

    private async Task<RequestCqrsProbeMarkerRecord> MarkerAsync(PartitionMovementLifetimeProofRf3Original original,
        ReplicaSiloDiscovery[] owners, CancellationToken token)
    {
        using var poll = CancellationTokenSource.CreateLinkedTokenSource(token);
        var pending = wave.QueryControls.WaitForMarkerAsync(original.ArmId,
            RequestCqrsProbePhase.ControlledLifetimeReplayBeforeDispatch, RequestCqrsProbeOutcome.Observed, owners, poll.Token);
        var failures = new List<Exception>();
        RequestCqrsProbeMarkerRecord? observed = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            if (await Task.WhenAny(pending, original.Producer) == original.Producer)
            {
                _ = await original.Result();
                throw new InvalidDataException(P.Invalid);
            }
            observed = await pending;
        }, failures);
        await ServerFailureObserver.ObserveAsync(poll.CancelAsync, failures);
        try
        {
            _ = await pending;
        }
        catch (OperationCanceledException) when (poll.IsCancellationRequested && !token.IsCancellationRequested) { }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure)) { failures.Add(failure); }
        ServerFailureObserver.ThrowIfAny(failures);
        return observed ?? throw new InvalidDataException(P.Invalid);
    }
}
