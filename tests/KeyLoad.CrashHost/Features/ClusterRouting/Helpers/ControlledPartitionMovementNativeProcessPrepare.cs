using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal static class ControlledPartitionMovementNativeProcessPrepare
{
    private const int Version = 1;
    private const int FirstVoter = 0;
    private const int FirstPage = 0;
    private const long InitialRevision = 0;
    private const string PrincipalId = "root";

    internal static PartitionMovementTransportRequest Create(ControlledPartitionMovementNativeHosts hosts,
        long expectedRevision = InitialRevision)
    {
        var source = hosts.Source;
        var placement = source.Database.ReadAtomicPartitionPlacement(PrincipalId,
            new(Version, ControlledPartitionMovementProcessCorpus.Partition));
        var request = new PartitionMoveRequest(ControlledPartitionMovementProcessPrepareRequest.MoveId,
            ControlledPartitionMovementProcessCorpus.Partition, hosts.Destination.Owner.PhysicalShardId,
            expectedRevision, PartitionMoveMode.Transfer);
        var body = NativeSerialization.Serialize(new PartitionMovePrepareBody(PrincipalId, request));
        var envelope = new PartitionMovePeerEnvelope(Version, request.MoveId, request.Partition,
            hosts.Control.Owner, placement, hosts.Destination.Owner,
            Convert.ToHexStringLower(SHA256.HashData(body)), PartitionMovePeerStage.ControlPrepare,
            FirstPage, source.Clock.GetUtcNow() + source.Runtime.GrainRouting.Value.RequestLifetime,
            Guid.NewGuid(), body);
        return new(ControlledPartitionMovementProcessPrepareRequest.CommandId, envelope, null,
            source.Partition.Configuration.LocalId, source.CallerSiloAddress,
            PartitionMovementTransportAction.Apply, Guid.Empty, FirstPage);
    }

    internal static async Task<PartitionMovementAuthenticatedReply> SendAsync(
        ControlledPartitionMovementNativeHosts hosts, PartitionMovementTransportRequest original,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var maximum = hosts.Source.Database.Limits.MaxBatchBytes;
        if (NativeSerialization.Measure(original) > maximum)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, PartitionMovementProtocol.Unavailable); }
        var bytes = NativeSerialization.Serialize(original);
        if (bytes.Length > maximum)
        { throw Errors.Fail(ErrorCode.ResourceExhausted, PartitionMovementProtocol.Unavailable); }
        PartitionMovementAuthenticatedReply? reply = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var transport = hosts.Source.BorrowTransport();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                reply = await transport.ExchangeAsync(FirstVoter, hosts.Control, original, bytes,
                    local: true, cancellationToken).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return reply ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMovementProtocol.Unavailable);
    }
}
