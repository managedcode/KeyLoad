using System.Security.Cryptography;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core.Features.ClusterRouting.Queries;

internal static class PartitionControlOutcomeReferences
{
    internal static PartitionControlOutcomeReference Capture(IKeyValueView view,
        PartitionControlCommandIdentity identity, PhysicalShardRecord controlOwner, ReadExecutionBudget work)
    {
        work.Check();
        var key = PartitionControlCommandKeys.Original(identity);
        var admitted = work.CreateView(view);
        RequireControlOwner(admitted, controlOwner);
        var selection = CommandOutcomeKeyResolver.Select(admitted, identity.PrincipalId, identity.CommandId,
            new(identity.ScopeKind, identity.Partition));
        var outcome = selection.Outcome
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (outcome.Incarnation != controlOwner.Incarnation
            || !PartitionMoveSourceFenceValidation.ValidDigest(outcome.Fingerprint))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        var bytes = work.Read(view, key)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid);
        return new(PartitionMoveProtocol.Version, identity, outcome.Fingerprint, controlOwner,
            key, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    internal static StoredOutcome Read(IKeyValueView view, PartitionControlOutcomeReference reference,
        ReadExecutionBudget work)
    {
        RequireReference(reference);
        var key = PartitionControlCommandKeys.Original(reference.Identity);
        var bytes = work.Read(view, key)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid);
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(reference.OutcomeDigest), SHA256.HashData(bytes)))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        var admitted = work.CreateView(view);
        RequireControlOwner(admitted, reference.ControlOwner);
        var selection = CommandOutcomeKeyResolver.Select(admitted, reference.Identity.PrincipalId,
            reference.Identity.CommandId, new(reference.Identity.ScopeKind, reference.Identity.Partition));
        var outcome = selection.Outcome
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid);
        if (outcome.Incarnation != reference.ControlOwner.Incarnation || outcome.Fingerprint != reference.Fingerprint)
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
        work.Check();
        return outcome;
    }

    private static void RequireControlOwner(IKeyValueView view, PhysicalShardRecord owner)
    {
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        if (!PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, owner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.OwnerMismatch); }
    }

    private static void RequireReference(PartitionControlOutcomeReference reference)
    {
        if (reference is null || reference.Version != PartitionMoveProtocol.Version
            || reference.ControlOwner is null || reference.ControlOwner.Incarnation == Guid.Empty
            || !PartitionMoveSourceFenceValidation.ValidDigest(reference.Fingerprint)
            || !PartitionMoveSourceFenceValidation.ValidDigest(reference.OutcomeDigest)
            || !PartitionControlCommandKeys.Original(reference.Identity).AsSpan().SequenceEqual(reference.OutcomeKey.Span))
        { throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.Invalid); }
    }
}
