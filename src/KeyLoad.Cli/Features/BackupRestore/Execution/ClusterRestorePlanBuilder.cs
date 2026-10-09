using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using Microsoft.Extensions.Options;

namespace KeyLoad.Cli.Features.BackupRestore;

internal static class ClusterRestorePlanBuilder
{
    private const int FirstOrdinal = 0;
    private const int NextOrdinal = 1;
    private const int SigningKeyBytes = 32;
    private const string Invalid = "The complete original operation/node/path/signer restore binding is invalid.";

    internal static ClusterRestorePlan Build(ClusterRestoreOperatorConfiguration options, string destination,
        ClusterRestoreSourceReader.Result original, Dictionary<Guid, byte[]> signing,
        IOptions<ZoneTreeStorageExecutionOptions> storage, IOptions<DatabaseLimits> limits,
        TimeProvider clock, ClusterRestorePlan? previous)
    {
        if (options.OperationId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
        var cuts = original.Sources.Select(source => source.OriginalCut).ToImmutableArray();
        var mappings = options.Mappings.Select(mapping => mapping.ToNative()).ToImmutableArray();
        ClusterRestoreMappingValidation.Require(cuts.First().CaptureId, cuts, mappings);
        ClusterRestoreCoordinator.RequireNodes(destination, options, mappings);
        ClusterRestoreCoordinator.RequireCredentials(options, mappings);
        var slots = Slots(options, original, mappings, signing, previous);
        var actualLimits = limits.Value;
        actualLimits.Validate();
        var policy = NativeSerialization.Deserialize<DatabaseLimits>(NativeSerialization.Serialize(actualLimits));
        return new(ClusterRestorePlan.CurrentVersion, options.OperationId, cuts.First().CaptureId, original.Sources,
            destination, mappings, slots, ClusterRestorePolicies.Capture(storage.Value), policy,
            original.Subjects, previous?.CreatedAtUtc ?? clock.GetUtcNow());
    }

    private static ImmutableArray<ClusterRestoreSlot> Slots(ClusterRestoreOperatorConfiguration options,
        ClusterRestoreSourceReader.Result original, ImmutableArray<ClusterRestoreOwnerMapping> mappings,
        Dictionary<Guid, byte[]> signing, ClusterRestorePlan? previous)
    {
        var slots = ImmutableArray.CreateBuilder<ClusterRestoreSlot>(options.Nodes.Length);
        var ordinal = FirstOrdinal;
        foreach (var source in original.Sources)
        {
            var mapping = mappings.Single(value => value.Source.PhysicalShardId == source.OriginalCut.Owner.PhysicalShardId);
            var key = signing[mapping.Source.PhysicalShardId];
            var fingerprint = Convert.ToHexStringLower(SHA256.HashData(key));
            if (key.Length != SigningKeyBytes || original.SourceSignerFingerprints.Contains(fingerprint, StringComparer.Ordinal))
            { throw Errors.Fail(ErrorCode.Validation, Invalid); }
            foreach (var voter in mapping.Target.VoterIds)
            {
                var node = options.Nodes.Single(value => value.SourceOwnerId == mapping.Source.PhysicalShardId
                    && value.VoterId == voter);
                var nodeId = previous is null ? Guid.NewGuid() : PreviousNode(previous, ordinal);
                slots.Add(new(ClusterRestoreSlot.CurrentVersion, ordinal, mapping.Source.PhysicalShardId,
                    mapping.Target.PhysicalShardId, nodeId, mapping.Target.Incarnation, fingerprint,
                    node.RelativeDataDirectory));
                ordinal = checked(ordinal + NextOrdinal);
            }
        }
        return slots.ToImmutable();
    }

    private static Guid PreviousNode(ClusterRestorePlan plan, int ordinal)
    {
        if (plan.Slots.IsDefault || ordinal >= plan.Slots.Length || plan.Slots[ordinal].SlotOrdinal != ordinal
            || plan.Slots[ordinal].TargetNodeId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        return plan.Slots[ordinal].TargetNodeId;
    }
}
