using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int AnnSettlementEffectsLength = 0;
    private const string InvalidAnnSettlement = "The original ANN enrollment settlement is unavailable.";

    internal ProjectionConsumerInfo ReadAnnProjectionConsumer(PrincipalRecord expected, AnnMaintenanceRequest request)
        => Store.Read(view =>
        {
            var principal = Principal(view, expected.Id, Clock.GetUtcNow());
            RequireProjectionAdministrator(principal);
            if (principal.PolicyEpoch != expected.PolicyEpoch || principal.Revoked)
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, InvalidAnnSettlement);
            }
            Features.Search.AnnProjectionPinValidation.RequireOwner(this, view, principal,
                request.Consumer.Partition, request);
            return ProjectionConsumer(view, request.Consumer);
        });

    internal CommitProjectionBatchRequest? CreateAnnInitialSettlementIntent(PrincipalRecord principal,
        AnnMaintenanceRequest original, AnnMaintenanceResult result, Guid childCommandId)
    {
        var initial = Store.Read(view => ProjectionConsumer(view, original.Consumer));
        if (initial.InitialAnnMaintenance is not { } reserved)
        {
            return null;
        }
        if (!global::KeyLoad.AnnMaintenanceRequestIdentity.Same(reserved, original))
        {
            if (!initial.InitialAnnMaintenanceSettled)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidAnnSettlement);
            }
            return null;
        }
        var checkpoint = result.Checkpoint?.Checkpoint ?? result.Source?.ThroughSequence
            ?? throw Errors.Fail(ErrorCode.Corruption, InvalidAnnSettlement);
        var page = ReadProjectionBatch(principal.Id, new(original.Consumer, ThroughSequence: checkpoint));
        if (!page.Entries.IsEmpty || page.ThroughSequence != checkpoint)
        {
            throw Errors.Fail(ErrorCode.Conflict, InvalidAnnSettlement);
        }
        return Store.Read(view =>
            CreateValidatedAnnSettlementIntent(principal, original, result, childCommandId, view, checkpoint, page));
    }

    private CommitProjectionBatchRequest CreateValidatedAnnSettlementIntent(PrincipalRecord principal,
        AnnMaintenanceRequest original, AnnMaintenanceResult result, Guid childCommandId, IKeyValueView view,
        long checkpoint, ProjectionBatch page)
    {
        var now = Clock.GetUtcNow();
        var fresh = Principal(view, principal.Id, now);
        RequireProjectionAdministrator(fresh);
        var state = ProjectionConsumer(view, original.Consumer);
        var authority = RequireAnnSettlementAuthority(state, fresh);
        Features.Search.AnnProjectionPinValidation.RequireOwner(this, view, fresh,
            original.Consumer.Partition, original);
        if (state.Released || state.Checkpoint != checkpoint
            || state.Definition.IndexGeneration != original.IndexGeneration
            || state.InitialAnnMaintenance is not { } exact
            || !global::KeyLoad.AnnMaintenanceRequestIdentity.Same(exact, original)
            || childCommandId == Guid.Empty || result.CommandId != original.CommandId
            || result.Consumer != original.Consumer || result.IndexGeneration != original.IndexGeneration)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidAnnSettlement);
        }
        var batch = Verify<ProjectionBatchClaims>(page.Token);
        if (batch.After != checkpoint || batch.Through != checkpoint || batch.ExpiresAt <= now)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidAnnSettlement);
        }
        var proof = new AnnProjectionSettlementClaims(AnnProjectionSettlementClaims.SettlementPurpose,
            Store.Identity.Incarnation, fresh.Id, fresh.PolicyEpoch, authority.ConfigureCommandId,
            original.Consumer, original.IndexGeneration, original.CommandId,
            System.Security.Cryptography.SHA256.HashData(NativeSerialization.Serialize(exact)),
            System.Security.Cryptography.SHA256.HashData(NativeSerialization.Serialize(result)),
            checkpoint, original.NodeId, original.Placement, batch.ExpiresAt, childCommandId);
        if (CoreNativeClaims.MeasureCharacters(proof) > ClaimsExecution.MaximumTokenCharacters)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidAnnSettlement);
        }
        var intent = new CommitProjectionBatchRequest(childCommandId, original.Consumer, page.Token, [])
        {
            InitialAnnMaintenanceSettlement = Sign(proof)
        };
        if (NativeSerialization.Measure(intent) > Limits.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidAnnSettlement);
        }
        return intent;
    }

    private static ProjectionMaintenanceAuthority RequireAnnSettlementAuthority(
        ProjectionConsumerInfo state, PrincipalRecord principal)
    {
        var authority = state.MaintenanceAuthority;
        if (authority is null || authority.PrincipalId != principal.Id
            || authority.PolicyEpoch != principal.PolicyEpoch || authority.ConfigureCommandId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, InvalidAnnSettlement);
        }
        return authority;
    }

    private ProjectionConsumerInfo? RequireAnnInitialSettlement(IKeyValueView view, PrincipalRecord principal,
        CommitProjectionBatchRequest request, ProjectionBatchClaims batch, DateTimeOffset now, bool completed)
    {
        if (request.InitialAnnMaintenanceSettlement is not { } token)
        {
            return null;
        }
        var proof = Verify<AnnProjectionSettlementClaims>(token);
        var state = ProjectionConsumer(view, request.Consumer);
        var authority = RequireAnnSettlementAuthority(state, principal);
        var original = state.InitialAnnMaintenance;
        if (original is null || state.Released || request.Effects.Length != AnnSettlementEffectsLength
            || proof.Purpose != AnnProjectionSettlementClaims.SettlementPurpose
            || proof.Incarnation != Store.Identity.Incarnation || proof.Incarnation != authority.Incarnation
            || proof.PrincipalId != principal.Id || proof.PolicyEpoch != principal.PolicyEpoch
            || proof.ConfigureCommandId != authority.ConfigureCommandId || proof.Consumer != request.Consumer
            || proof.IndexGeneration != state.Definition.IndexGeneration
            || proof.InitialCommandId != original.CommandId || proof.SettlementCommandId != request.CommandId
            || proof.NodeId != original.NodeId || proof.Checkpoint != state.Checkpoint
            || batch.After != proof.Checkpoint || batch.Through != proof.Checkpoint
            || proof.ExpiresAt != batch.ExpiresAt || !completed && proof.ExpiresAt <= now
            || proof.InitialRequestSha256 is null || proof.OriginalResultSha256 is null
            || proof.InitialRequestSha256.Length != System.Security.Cryptography.SHA256.HashSizeInBytes
            || proof.OriginalResultSha256.Length != System.Security.Cryptography.SHA256.HashSizeInBytes
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(proof.InitialRequestSha256,
                System.Security.Cryptography.SHA256.HashData(NativeSerialization.Serialize(original)))
            || !NativeSerialization.Serialize(proof.PhysicalPlacement).AsSpan()
                .SequenceEqual(NativeSerialization.Serialize(original.Placement))
            || completed && !state.InitialAnnMaintenanceSettled)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidAnnSettlement);
        }
        return state;
    }

    private static byte[] ProjectionReceiptKey(ProjectionBatchClaims claims, CommitProjectionBatchRequest request)
        => request.InitialAnnMaintenanceSettlement is null ? ProjectionReceiptKey(claims)
            : KeySpace.Partition(ProjectionReceiptSpace, claims.Consumer.Partition, claims.Consumer.Name,
                claims.IndexGeneration, AnnProjectionSettlementClaims.SettlementPurpose, request.CommandId);
}
