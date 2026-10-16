using System.Security.Cryptography;
using System.Text;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Joins only the original fence-owned work, outside all storage and replication gates.</summary>
internal static class ResourceRetirementWorkRead
{
    private static readonly byte[] SnapshotDomain = Encoding.ASCII.GetBytes("keyload.retirement.work-snapshot.v1\0");
    private const int Empty = 0;

    internal static Task<ResourceRetirementReadReplyV1> ExecuteAsync(OrleansNode node, PartitionHost partition,
        ResourceRetirementReadRequestV1 query, PrincipalRecord principal, IOptions<DatabaseLimits> limits,
        TimeProvider clock, CancellationToken token)
        => ExecuteAsync(node, partition, query, principal, limits, clock, null, token);

    internal static async Task<ResourceRetirementReadReplyV1> ExecuteAsync(OrleansNode node, PartitionHost partition,
        ResourceRetirementReadRequestV1 query, PrincipalRecord principal, IOptions<DatabaseLimits> limits,
        TimeProvider clock, Action? beforeCapture, CancellationToken token)
    {
        var work = new ReadExecutionBudget(limits, clock, token);
        work.ConstrainLifetime(query.ExpiresAt);
        work.ConstrainNativeWork(query.MaximumReadBytes, query.MaximumExaminedRecords);
        work.ConstrainResultBytes(query.MaximumResultBytes);
        var before = partition.Database.Store.Identity;
        beforeCapture?.Invoke();
        var admitted = partition.Database.CaptureResourceRetirementWorkObservation(principal.Id, query.Control!, work);
        var fence = admitted.Fence;
        RequireSame(fence, query.WorkFence);
        var stored = admitted.Owner;
        RequireOrigin(node, query, stored, before.NodeId, before.Incarnation, before.ReadGeneration, limits.Value);
        var original = node.RequestWork.FindRetainedSettlement(fence);
        ResourceRetirementWorkJoinWitnessV1 witness;
        if (query.AcknowledgedWork is { } acknowledged)
        {
            RequireSame(stored, acknowledged);
            stored = stored ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
            witness = stored.Evidence.Single(item => item.Witness.VoterId == query.DestinationVoter).Witness;
            if (original is not null)
            {
                await original.CancelAndJoinAsync().WaitAsync(token).ConfigureAwait(false);
                RequireSame(witness, Capture(original, query, before.NodeId, before.ReadGeneration));
                node.RequestWork.ConsumeSettlement(fence, original);
            }
        }
        else if (stored is not null)
        {
            witness = stored.Evidence.Single(item => item.Witness.VoterId == query.DestinationVoter).Witness;
        }
        else
        {
            if (original is null)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
            }
            await original.CancelAndJoinAsync().WaitAsync(token).ConfigureAwait(false);
            witness = Capture(original, query, before.NodeId, before.ReadGeneration);
        }
        var after = partition.Database.Store.Identity;
        if (after.NodeId != before.NodeId || after.ReadGeneration != before.ReadGeneration)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementNativeProtocol.Unavailable);
        }
        RequireSame(fence, partition.Database.CaptureResourceRetirementWorkObservation(principal.Id, query.Control!, work).Fence);
        var reply = new ResourceRetirementReadReplyV1(null, null, null, work.ReadBytes, work.ConstrainedNativeRecords)
        {
            WorkJoinWitness = witness,
            AcknowledgedOwner = stored
        };
        work.CheckResult(reply);
        return reply;
    }

    private static void RequireOrigin(OrleansNode node, ResourceRetirementReadRequestV1 query,
        ResourceRetirementWorkOwnerReceiptV1? stored, Guid nodeId, Guid incarnation, long generation, DatabaseLimits limits)
    {
        if (stored is not null)
        {
            return;
        }
        var actual = node.Discovery?.Read()
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementNativeProtocol.Unavailable);
        ResourceRetirementWorkOriginValidation.RequireCurrent(query.Control!, query.ObservedOwner.Owner,
            actual.VoterId, actual.SiloAddress, nodeId, incarnation, generation, limits);
    }

    private static ResourceRetirementWorkJoinWitnessV1 Capture(NativeResourceWorkClosure original,
        ResourceRetirementReadRequestV1 query, Guid node, long generation)
    {
        var report = original.TerminalReport
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, ResourceRetirementNativeProtocol.Unavailable);
        NativeRequestWorkSettlement.Rethrow(report.Failure);
        var snapshot = report.Receipt.Admissions.OrderBy(item => item.AdmissionGeneration)
            .Select(item => new NativeResourceWorkPublicationSnapshotV1(item.RequestId, (int)item.Kind,
                item.AdmissionGeneration, item.LastPublicationGeneration)).ToArray();
        var bytes = NativeSerialization.Serialize(snapshot);
        using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        digest.AppendData(SnapshotDomain);
        digest.AppendData(bytes);
        return new(ResourceRetirementNativeProtocol.Version, original.Fence, report.Receipt.ActualOwner,
            query.DestinationVoter, query.DestinationSiloAddress, node, generation, snapshot.Length,
            snapshot.Length == Empty ? Empty : snapshot.Max(item => item.LastPublicationGeneration), digest.GetHashAndReset());
    }

    private static void RequireSame<T>(T actual, T expected)
    {
        if (!ResourceRetirementValidation.Same(actual, expected))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ResourceRetirementNativeProtocol.Unavailable);
        }
    }
}
