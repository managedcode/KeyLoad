using KeyLoad.Core.InternalSerialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes ChangeFeedsExecution operations against the caller's database context.</summary>
public static class ChangeFeedsExecution
{
    internal const int AnnSettlementEffectsLength = 0;
    internal const string InvalidAnnSettlement = "The original ANN enrollment settlement is unavailable.";
    internal static ProjectionConsumerInfo ReadAnnProjectionConsumer(this DatabaseEngine engine, PrincipalRecord expected, AnnMaintenanceRequest request) => engine.Store.Read(view =>
    {
        var principal = engine.Principal(view, expected.Id, engine.Clock.GetUtcNow());
        ProjectionBatchExecution.RequireProjectionAdministrator(principal);
        if (principal.PolicyEpoch != expected.PolicyEpoch || principal.Revoked)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, InvalidAnnSettlement);
        }

        Search.AnnProjectionPinValidation.RequireOwner(engine, view, principal, request.Consumer.Partition, request);
        return ProjectionConsumerIdentity.ProjectionConsumer(view, request.Consumer);
    });
    internal static global::ManagedCode.Communication.Result<CommitProjectionBatchRequest?> CreateAnnInitialSettlementIntent(this DatabaseEngine engine, PrincipalRecord principal, AnnMaintenanceRequest original, AnnMaintenanceResult result, Guid childCommandId)
    {
        var initial = engine.Store.Read(view => ProjectionConsumerIdentity.ProjectionConsumer(view, original.Consumer));
        if (initial.InitialAnnMaintenance is not { } reserved)
        {
            return global::ManagedCode.Communication.Result<CommitProjectionBatchRequest?>.FailNotFound(nameof(CreateAnnInitialSettlementIntent));
        }

        if (!global::KeyLoad.AnnMaintenanceRequestIdentity.Same(reserved, original))
        {
            if (!initial.InitialAnnMaintenanceSettled)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidAnnSettlement);
            }

            return global::ManagedCode.Communication.Result<CommitProjectionBatchRequest?>.FailNotFound(nameof(CreateAnnInitialSettlementIntent));
        }

        var checkpoint = result.Checkpoint?.Checkpoint ?? result.Source?.ThroughSequence ?? throw Errors.Fail(ErrorCode.Corruption, InvalidAnnSettlement);
        var page = engine.ReadProjectionBatch(principal.Id, new(original.Consumer, ThroughSequence: checkpoint));
        if (!page.Entries.IsEmpty || page.ThroughSequence != checkpoint)
        {
            throw Errors.Fail(ErrorCode.Conflict, InvalidAnnSettlement);
        }

        return global::ManagedCode.Communication.Result<CommitProjectionBatchRequest?>.Succeed(((CommitProjectionBatchRequest?)(engine.Store.Read(view => engine.CreateValidatedAnnSettlementIntent(principal, original, result, childCommandId, view, checkpoint, page)))));
    }

    internal static CommitProjectionBatchRequest CreateValidatedAnnSettlementIntent(this DatabaseEngine engine, PrincipalRecord principal, AnnMaintenanceRequest original, AnnMaintenanceResult result, Guid childCommandId, IKeyValueView view, long checkpoint, ProjectionBatch page)
    {
        var now = engine.Clock.GetUtcNow();
        var fresh = engine.Principal(view, principal.Id, now);
        ProjectionBatchExecution.RequireProjectionAdministrator(fresh);
        var state = ProjectionConsumerIdentity.ProjectionConsumer(view, original.Consumer);
        var authority = RequireAnnSettlementAuthority(state, fresh);
        Search.AnnProjectionPinValidation.RequireOwner(engine, view, fresh, original.Consumer.Partition, original);
        if (state.Released || state.Checkpoint != checkpoint || state.Definition.IndexGeneration != original.IndexGeneration || state.InitialAnnMaintenance is not { } exact || !global::KeyLoad.AnnMaintenanceRequestIdentity.Same(exact, original) || childCommandId == Guid.Empty || result.CommandId != original.CommandId || result.Consumer != original.Consumer || result.IndexGeneration != original.IndexGeneration)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidAnnSettlement);
        }

        var batch = engine.Verify<ProjectionBatchClaims>(page.Token);
        if (batch.After != checkpoint || batch.Through != checkpoint || batch.ExpiresAt <= now)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidAnnSettlement);
        }

        var proof = new AnnProjectionSettlementClaims(AnnProjectionSettlementClaims.SettlementPurpose, engine.Store.Identity.Incarnation, fresh.Id, fresh.PolicyEpoch, authority.ConfigureCommandId, original.Consumer, original.IndexGeneration, original.CommandId, System.Security.Cryptography.SHA256.HashData(OrleansSerialization.Serialize(exact)), System.Security.Cryptography.SHA256.HashData(OrleansSerialization.Serialize(result)), checkpoint, original.NodeId, original.Placement, batch.ExpiresAt, childCommandId);
        if (CoreClaims.MeasureCharacters(proof) > engine.ClaimsExecution.MaximumTokenCharacters)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidAnnSettlement);
        }

        var intent = new CommitProjectionBatchRequest(childCommandId, original.Consumer, page.Token, [])
        {
            InitialAnnMaintenanceSettlement = engine.Sign(proof)
        };
        return intent;
    }

    internal static ProjectionMaintenanceAuthority RequireAnnSettlementAuthority(ProjectionConsumerInfo state, PrincipalRecord principal)
    {
        var authority = state.MaintenanceAuthority;
        if (authority is null || authority.PrincipalId != principal.Id || authority.PolicyEpoch != principal.PolicyEpoch || authority.ConfigureCommandId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, InvalidAnnSettlement);
        }

        return authority;
    }

    internal static global::ManagedCode.Communication.Result<ProjectionConsumerInfo?> RequireAnnInitialSettlement(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, CommitProjectionBatchRequest request, ProjectionBatchClaims batch, DateTimeOffset now, bool completed)
    {
        if (request.InitialAnnMaintenanceSettlement is not { } token)
        {
            return global::ManagedCode.Communication.Result<ProjectionConsumerInfo?>.FailNotFound(nameof(RequireAnnInitialSettlement));
        }

        var proof = engine.Verify<AnnProjectionSettlementClaims>(token);
        var state = ProjectionConsumerIdentity.ProjectionConsumer(view, request.Consumer);
        var authority = RequireAnnSettlementAuthority(state, principal);
        var original = state.InitialAnnMaintenance;
        if (original is null || state.Released || request.Effects.Length != AnnSettlementEffectsLength || proof.Purpose != AnnProjectionSettlementClaims.SettlementPurpose || proof.Incarnation != engine.Store.Identity.Incarnation || proof.Incarnation != authority.Incarnation || proof.PrincipalId != principal.Id || proof.PolicyEpoch != principal.PolicyEpoch || proof.ConfigureCommandId != authority.ConfigureCommandId || proof.Consumer != request.Consumer || proof.IndexGeneration != state.Definition.IndexGeneration || proof.InitialCommandId != original.CommandId || proof.SettlementCommandId != request.CommandId || proof.NodeId != original.NodeId || proof.Checkpoint != state.Checkpoint || batch.After != proof.Checkpoint || batch.Through != proof.Checkpoint || proof.ExpiresAt != batch.ExpiresAt || !completed && proof.ExpiresAt <= now || proof.InitialRequestSha256 is null || proof.OriginalResultSha256 is null || proof.InitialRequestSha256.Length != System.Security.Cryptography.SHA256.HashSizeInBytes || proof.OriginalResultSha256.Length != System.Security.Cryptography.SHA256.HashSizeInBytes || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(proof.InitialRequestSha256, System.Security.Cryptography.SHA256.HashData(OrleansSerialization.Serialize(original))) || !OrleansSerialization.Serialize(proof.PhysicalPlacement).AsSpan().SequenceEqual(OrleansSerialization.Serialize(original.Placement)) || completed && !state.InitialAnnMaintenanceSettled)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidAnnSettlement);
        }

        return global::ManagedCode.Communication.Result<ProjectionConsumerInfo?>.Succeed(((ProjectionConsumerInfo?)(state)));
    }

    internal static byte[] ProjectionReceiptKey(ProjectionBatchClaims claims, CommitProjectionBatchRequest request) => request.InitialAnnMaintenanceSettlement is null ? ProjectionBatchExecution.ProjectionReceiptKey(claims) : KeySpace.Partition(ProjectionOutbox.ProjectionReceiptSpace, claims.Consumer.Partition, claims.Consumer.Name, claims.IndexGeneration, AnnProjectionSettlementClaims.SettlementPurpose, request.CommandId);
}
