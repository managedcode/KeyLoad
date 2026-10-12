namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const int MinimumMaintenanceDimension = 1;
    private const int MaximumMaintenanceDimension = 4_096;
    private const string InvalidMaintenanceProfile = "The ANN projection maintenance profile is invalid.";
    private const string InvalidMaintenanceAuthority = "The ANN projection maintenance authority is invalid.";
    private static readonly string[] MaintenanceMutationKinds =
    [
        MutationDiscriminatorNames.PutDocument,
        MutationDiscriminatorNames.PatchDocument,
        MutationDiscriminatorNames.DeleteDocument,
        MutationDiscriminatorNames.PutVector,
        MutationDiscriminatorNames.ApplyVectorProjection
    ];

    private ProjectionMaintenanceAuthority? CaptureProjectionMaintenanceAuthority(
        global::KeyLoad.Storage.IAtomicTransaction transaction, ConfigureProjectionConsumerRequest request, ReplicatedOperation operation)
    {
        RequireInitialMaintenanceReservation(transaction, request, operation);
        if (request.Definition.AnnMaintenance is not { } profile)
        {
            return null;
        }
        RequireProjectionMaintenanceProfile(request.Definition, profile);
        var principal = Principal(transaction, operation.PrincipalId, operation.EvaluatedAt);
        RequireProjectionAdministrator(principal);
        Authorization.Require(principal, request.Consumer.Partition, profile.Collection, Capability.VectorSearch);
        var resource = Resource(transaction, request.Consumer.Partition, profile.Collection, ResourceKind.Collection);
        Authorization.RequireFieldUse(principal, resource, profile.Field);
        RequireProjectionMaintenanceResource(resource, profile);
        return new(principal.Id, request.CommandId, Store.Identity.Incarnation, principal.PolicyEpoch);
    }

    private void RequireInitialMaintenanceReservation(global::KeyLoad.Storage.IKeyValueView view,
        ConfigureProjectionConsumerRequest configure, ReplicatedOperation operation)
    {
        if (configure.InitialAnnMaintenance is not { } original) { return; }
        var profile = configure.Definition.AnnMaintenance;
        if (profile is null || original.CommandId == Guid.Empty || original.Mode != AnnMaintenanceMode.Build
            || original.Consumer != configure.Consumer || original.IndexGeneration != configure.Definition.IndexGeneration
            || original.MaintenanceProfile != profile || original.Collection != profile.Collection
            || original.Field != profile.Field || original.Space != profile.Space)
        { throw Errors.Fail(ErrorCode.Validation, InvalidMaintenanceProfile); }
        var principal = Principal(view, operation.PrincipalId, operation.EvaluatedAt);
        Features.Search.AnnProjectionPinValidation.RequireOwner(this, view, principal,
            configure.Consumer.Partition, original);
        if (NativeSerialization.Measure(original) > Limits.MaxBatchBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, InvalidMaintenanceProfile); }
    }

    private static void RequireInitialMaintenanceReservation(ProjectionConsumerInfo previous,
        ConfigureProjectionConsumerRequest configure)
    {
        if (previous.InitialAnnMaintenance is not null && !previous.InitialAnnMaintenanceSettled
            && configure.CommandId != previous.MaintenanceAuthority?.ConfigureCommandId)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidMaintenanceAuthority); }
        if (configure.InitialAnnMaintenance is not { } supplied) { return; }
        if (previous.InitialAnnMaintenance is not { } original
            || !NativeSerialization.Serialize(original).AsSpan().SequenceEqual(NativeSerialization.Serialize(supplied)))
        { throw Errors.Fail(ErrorCode.Conflict, InvalidMaintenanceProfile); }
    }

    private static void RequireProjectionMaintenanceProfile(ProjectionConsumerDefinition definition,
        AnnProjectionMaintenanceProfile profile)
    {
        if (profile.Space is null || definition.Resources.IsDefault || definition.MutationKinds.IsDefault
            || !definition.Resources.IsEmpty
            || !definition.MutationKinds.SequenceEqual(MaintenanceMutationKinds, StringComparer.Ordinal)
            || profile.Space.Dimension is < MinimumMaintenanceDimension or > MaximumMaintenanceDimension
            || !Enum.IsDefined(profile.Space.Metric))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidMaintenanceProfile);
        }
        JsonData.Identifier(profile.Collection);
        JsonData.Identifier(profile.Field);
        JsonData.Identifier(profile.Space.Id);
        JsonData.Identifier(profile.Space.Model);
        JsonData.Identifier(profile.Space.Version);
    }

    private static void RequireProjectionMaintenanceResource(ResourceDefinition resource,
        AnnProjectionMaintenanceProfile profile)
    {
        Features.Search.VectorFieldProfiles.Validate(resource, ErrorCode.Corruption);
        var configured = resource.VectorProfiles.FirstOrDefault(item => item.Field == profile.Field);
        if (configured is not null && configured.Space != profile.Space)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidMaintenanceProfile);
        }
    }

    private static void RequireProjectionMaintenanceAuthority(ProjectionConsumerInfo state,
        ProjectionMaintenanceAuthority? admitted)
    {
        if (state.MaintenanceAuthority is not { } original
            ? admitted is not null
            : admitted is null || original.ConfigureCommandId == Guid.Empty
                || original.PrincipalId != admitted.PrincipalId || original.Incarnation != admitted.Incarnation
                || original.PolicyEpoch != admitted.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidMaintenanceAuthority);
        }
    }
}
