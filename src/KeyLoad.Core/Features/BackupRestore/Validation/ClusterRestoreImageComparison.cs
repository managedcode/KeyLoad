using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

/// <summary>Verifies the entire recovered image against an independently recovered original native archive view.</summary>
public static class ClusterRestoreImageComparison
{
    private const string Invalid = "The native restore image or current persisted operator no longer matches its original plan.";

    /// <summary>Requires exact unchanged canonical bytes and only explicitly defined native metadata transformations.</summary>
    /// <param name="source">Independently recovered original native archive view under its read gate.</param>
    /// <param name="sourceIdentity">Verified original native archive identity.</param>
    /// <param name="metadata">Original checksum-verified native archive metadata.</param>
    /// <param name="target">Actual recovered unpublished or published target view under its read gate.</param>
    /// <param name="targetIdentity">Actual current source or admitted target file identity.</param>
    /// <param name="position">Actual recovered target native position.</param>
    /// <param name="secret">Separately supplied current persisted operator credential.</param>
    /// <param name="cuts">Complete original independently verified owner vector.</param>
    /// <param name="mappings">Exact original target vector.</param>
    /// <param name="context">Original admitted operation slot.</param>
    /// <param name="limits">Original centrally validated policy owner.</param>
    /// <param name="clock">Actual current operator clock.</param>
    /// <param name="work">Original downward work, never renewed here.</param>
    public static void Require(IKeyValueView source, StoreIdentity sourceIdentity, ReadOnlyMemory<byte> metadata,
        IKeyValueView target, StoreIdentity targetIdentity, long position, string secret,
        ImmutableArray<ClusterBackupOwnerCut> cuts, ImmutableArray<ClusterRestoreOwnerMapping> mappings,
        ClusterRestoreSlotContext context, IOptions<DatabaseLimits> limits, TimeProvider clock, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sourceIdentity);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(targetIdentity);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(work);
        var original = ClusterBackupNativeCutVerification.Require(source, sourceIdentity, context.SourcePosition,
            metadata, secret, limits, clock, work);
        ClusterRestoreSlotValidation.RequireSource(context, sourceIdentity, context.SourcePosition, mappings);
        ClusterRestoreMappingValidation.Require(original.CaptureId, cuts, mappings);
        ClusterRestorePlanVerification.RequireOriginalCut(cuts.Single(cut =>
            cut.Owner.PhysicalShardId == original.Owner.PhysicalShardId), original);
        var admittedSource = work.CreateView(source);
        var admittedTarget = work.CreateView(target);
        var sourcePrincipal = ClusterRestoreOperatorValidation.RequireAdministrator(admittedSource, secret, clock.GetUtcNow());
        var targetPrincipal = ClusterRestoreOperatorValidation.RequireAdministrator(admittedTarget, secret, clock.GetUtcNow());
        if (sourcePrincipal.Id != targetPrincipal.Id || sourcePrincipal.PolicyEpoch != targetPrincipal.PolicyEpoch)
        { throw Errors.Fail(ErrorCode.PermissionDenied, Invalid); }
        var reset = ClusterRestoreImageCommits.Require(admittedTarget, original, context,
            ClusterRestoreMappingDigest.Compute(mappings), position);
        var sourceFile = targetIdentity.NodeId == context.SourceNodeId && targetIdentity.Incarnation == context.SourceIncarnation;
        var targetFile = targetIdentity.NodeId == context.TargetNodeId && targetIdentity.Incarnation == context.TargetIncarnation;
        if (!sourceFile && !targetFile || reset && !targetFile || targetFile && !targetIdentity.DispatchPaused)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        var state = new ClusterRestoreImageState(admittedSource, admittedTarget, original, mappings,
            context, position, reset, limits, work);
        ClusterRestoreImageMetadata.Require(state);
        if (reset)
        { ClusterRestoreImageOrigins.Require(state); }
        ClusterRestoreImageRows.Require(state, ClusterRestoreImageMetadata.ChangedKeys(state));
        work.Check();
    }
}
