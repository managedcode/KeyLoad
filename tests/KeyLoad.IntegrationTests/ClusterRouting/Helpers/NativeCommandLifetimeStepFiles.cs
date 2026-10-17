using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;
using P = KeyLoad.Server.Features.ClusterRouting.RequestCqrsControlledLifetimeProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

/// <summary>Uses only the original immutable owner/session directories and bounded native probe files.</summary>
internal static class NativeCommandLifetimeStepFiles
{
    internal static void Plan(TwoRf3MembershipWave wave, Guid armId,
        ControlledLifetimeReplayVariation variation, Guid? donorArm)
    {
        var arm = wave.QueryControls.ArmFor(armId);
        var plan = new RequestCqrsControlledLifetimePlan(RequestCqrsProbeProtocol.Version, P.PlanKind,
            wave.QueryControls.SessionId, armId, arm.CommandId, arm.PrincipalId, variation, donorArm);
        var bytes = RequestCqrsControlledLifetimeCodec.WritePlan(plan);
        foreach (var node in RequestCqrsProbeFixtureProtocol.Nodes)
        {
            var owned = wave.QueryControls.NodeFor(node);
            RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
            RequestCqrsProbeFileStore.WriteAtomic(owned.Directory, RequestCqrsControlledLifetimeNames.PlanName(armId), bytes);
        }
    }

    internal static async Task<RequestCqrsControlledLifetimeStepWitness> WaitAsync(TwoRf3MembershipWave wave,
        RequestCqrsProbeMarkerRecord marker, int step, PartitionMovementLifetimeProofRf3Original original, CancellationToken token)
    {
        using var poll = CancellationTokenSource.CreateLinkedTokenSource(token);
        var failures = new List<Exception>();
        RequestCqrsControlledLifetimeStepWitness? observed = null;
        var pending = PollAsync(wave, marker, step, poll.Token);
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

    private static async Task<RequestCqrsControlledLifetimeStepWitness> PollAsync(TwoRf3MembershipWave wave,
        RequestCqrsProbeMarkerRecord marker, int step, CancellationToken token)
    {
        var options = new RequestProbeExecutionOptions();
        options.Validate();
        var owned = wave.QueryControls.NodeFor(new Uri(marker.Voter).Host);
        var name = RequestCqrsControlledLifetimeNames.StepName(marker.ArmId, marker.RequestId, step);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
            var path = Path.Combine(owned.Directory, name);
            if (File.Exists(path))
            {
                return RequestCqrsControlledLifetimeCodec.ReadWitness(RequestCqrsProbeFileStore.ReadRecord(path));
            }
            await Task.Delay(options.PollInterval, TimeProvider.System, token);
        }
    }

    internal static byte[] Parts(TwoRf3MembershipWave wave, RequestCqrsControlledLifetimeStepWitness witness,
        string role, int step, int bytes, int count, string digest)
    {
        var maximum = checked(IntegrationExecutionOptions.DatabaseLimits().Value.MaxBatchBytes
            + ReplicaTransportProtocol.MaximumEnvelopeOverheadBytes);
        if (bytes <= P.EmptyBytes || bytes > maximum || count != checked((bytes + RequestCqrsProbeFixtureProtocol.MaximumRecordBytes
            - P.IndexStep) / RequestCqrsProbeFixtureProtocol.MaximumRecordBytes))
        {
            throw new InvalidDataException(P.Invalid);
        }
        var owned = wave.QueryControls.NodeFor(new Uri(witness.CallerVoter).Host);
        RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
        using var output = new MemoryStream(bytes);
        for (var index = P.FirstIndex; index < count; index += P.IndexStep)
        {
            var name = RequestCqrsControlledLifetimeNames.PartName(witness.ArmId, witness.RequestId, step, role, index);
            var part = RequestCqrsProbeFileStore.ReadRecord(Path.Combine(owned.Directory, name));
            if (part.Length != Math.Min(RequestCqrsProbeFixtureProtocol.MaximumRecordBytes, bytes - output.Length))
            {
                throw new InvalidDataException(P.Invalid);
            }
            output.Write(part);
        }
        var value = output.ToArray();
        if (value.Length != bytes || Convert.ToHexStringLower(SHA256.HashData(value)) != digest)
        {
            throw new InvalidDataException(P.Invalid);
        }
        return value;
    }

    internal static void Ack(TwoRf3MembershipWave wave, RequestCqrsControlledLifetimeStepWitness witness)
    {
        var owned = wave.QueryControls.NodeFor(new Uri(witness.CallerVoter).Host);
        RequestCqrsProbeFileStore.VerifyOwnerFile(owned.Directory, owned.OwnerBytes);
        var ack = new RequestCqrsControlledLifetimeStepAck(RequestCqrsProbeProtocol.Version, P.AckKind,
            witness.SessionId, witness.ArmId, witness.RequestId, witness.Step);
        RequestCqrsProbeFileStore.WriteAtomic(owned.Directory,
            RequestCqrsControlledLifetimeNames.AckName(witness.ArmId, witness.RequestId, witness.Step),
            RequestCqrsControlledLifetimeCodec.WriteAck(ack));
    }
}
