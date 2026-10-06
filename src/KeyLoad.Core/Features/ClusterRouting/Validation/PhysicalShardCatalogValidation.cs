using System.Collections.Immutable;
using System.Text;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.Core.Features.ClusterRouting.Validation;

/// <summary>Validates bootstrap requests and committed catalog records.</summary>
internal static class PhysicalShardCatalogValidation
{
    private const string InvalidRequest = "The physical shard catalog bootstrap request is invalid.";
    private const string RequestTooLarge = "The physical shard catalog request exceeds its byte budget.";
    private const string InvalidStoredCatalog = "The committed physical shard catalog is malformed.";
    private const string EmptyGuid = "A physical shard identity component is empty.";
    private const string InvalidVoters = "The physical shard voter list is invalid.";
    internal const string HostMismatch = "The physical shard catalog does not match this host.";

    internal static void ValidateEncodedRequestLength(long encodedLength)
    {
        const int EncodedLengthValidationBoundary = 0;

        if (encodedLength < EncodedLengthValidationBoundary || encodedLength > PhysicalShardCatalogProtocol.MaximumEncodedBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RequestTooLarge);
        }
    }

    internal static void ValidateRequest(BootstrapPhysicalShardCatalogRequest? request)
    {
        if (request is null || request.Version != PhysicalShardCatalogProtocol.CurrentVersion
            || request.PhysicalShardId == Guid.Empty
            || request.Incarnation == Guid.Empty || request.VoterIds.IsDefaultOrEmpty
            || request.VoterIds.Length > PhysicalShardCatalogProtocol.MaximumVoters)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidRequest);
        }

        ValidateVoters(request.VoterIds, ErrorCode.Validation);
        ValidateEncodedRequestLength(NativeSerialization.Serialize(request).LongLength);
    }

    internal static void ValidateCatalog(PhysicalShardCatalog? catalog)
    {
        const int RevisionValidationBoundary = 0;
        const int PlacementEpochValidationBoundary = 0;

        if (catalog is null || catalog.Version != PhysicalShardCatalogProtocol.CurrentVersion
            || catalog.Revision <= RevisionValidationBoundary
            || catalog.DefaultShard is null
            || catalog.DefaultShard.PlacementEpoch <= PlacementEpochValidationBoundary)
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidStoredCatalog);
        }

        if (catalog.DefaultShard.PhysicalShardId == Guid.Empty || catalog.DefaultShard.Incarnation == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.Corruption, EmptyGuid);
        }

        if (!ValidVoters(catalog.DefaultShard.VoterIds))
        {
            throw Errors.Fail(ErrorCode.Corruption, InvalidVoters);
        }

    }

    internal static bool MatchesInitial(PhysicalShardRecord record,
        BootstrapPhysicalShardCatalogRequest request)
        => record.PhysicalShardId == request.PhysicalShardId
            && record.Incarnation == request.Incarnation
            && record.PlacementEpoch == PhysicalShardCatalogProtocol.InitialPlacementEpoch
            && record.VoterIds.SequenceEqual(request.VoterIds, StringComparer.Ordinal);

    internal static bool MatchesConfiguredHost(PhysicalShardRecord record, Guid physicalShardId,
        Guid incarnation, ImmutableArray<string> voterIds)
        => record.PhysicalShardId == physicalShardId
            && record.Incarnation == incarnation
            && record.PlacementEpoch == PhysicalShardCatalogProtocol.InitialPlacementEpoch
            && record.VoterIds.SequenceEqual(voterIds, StringComparer.Ordinal);

    private static void ValidateVoters(ImmutableArray<string> voterIds, ErrorCode errorCode)
    {
        if (!ValidVoters(voterIds))
        {
            throw Errors.Fail(errorCode, InvalidVoters);
        }
    }

    private static bool ValidVoters(ImmutableArray<string> voterIds)
        => !voterIds.IsDefaultOrEmpty && voterIds.Length <= PhysicalShardCatalogProtocol.MaximumVoters
            && voterIds.All(static id => !string.IsNullOrWhiteSpace(id)
                && id.Length <= PhysicalShardCatalogProtocol.MaximumVoterIdBytes
                && Encoding.UTF8.GetByteCount(id) <= PhysicalShardCatalogProtocol.MaximumVoterIdBytes)
            && voterIds.Distinct(StringComparer.Ordinal).Count() == voterIds.Length;
}
