using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;
/// <summary>Executes ProjectionMaintenanceConfiguration operations against the caller's database context.</summary>
public static class ProjectionMaintenanceConfiguration
{
    internal const int MinimumMaintenanceDimension = 1;
    internal const int MaximumMaintenanceDimension = 4_096;
    internal const string InvalidMaintenanceProfile = "The ANN projection maintenance profile is invalid.";
    internal const string InvalidMaintenanceAuthority = "The ANN projection maintenance authority is invalid.";
    internal static readonly string[] MaintenanceMutationKinds = [MutationDiscriminatorNames.PutDocument, MutationDiscriminatorNames.PatchDocument, MutationDiscriminatorNames.DeleteDocument, MutationDiscriminatorNames.PutVector, MutationDiscriminatorNames.ApplyVectorProjection];
    internal static ProjectionMaintenanceAuthority? CaptureProjectionMaintenanceAuthority(this DatabaseEngine engine, global::KeyLoad.Storage.IAtomicTransaction transaction, ConfigureProjectionConsumerRequest request, Command<DatabaseCommand> operation)
    {
        engine.RequireInitialMaintenanceReservation(transaction, request, operation);
        if (request.Definition.AnnMaintenance is not { } profile)
        {
            return null;
        }

        RequireProjectionMaintenanceProfile(request.Definition, profile);
        var principal = engine.Principal(transaction, operation.UserId!, engine.Clock.GetUtcNow());
        ProjectionBatchExecution.RequireProjectionAdministrator(principal);
        engine.Authorization.Require(principal, request.Consumer.Partition, profile.Collection, Capability.VectorSearch);
        var resource = engine.Resource(transaction, request.Consumer.Partition, profile.Collection, ResourceKind.Collection);
        engine.Authorization.RequireFieldUse(principal, resource, profile.Field);
        RequireProjectionMaintenanceResource(resource, profile);
        return new(principal.Id, request.CommandId, engine.Store.Identity.Incarnation, principal.PolicyEpoch);
    }

    internal static void RequireInitialMaintenanceReservation(this DatabaseEngine engine, global::KeyLoad.Storage.IKeyValueView view, ConfigureProjectionConsumerRequest configure, Command<DatabaseCommand> operation)
    {
        if (configure.InitialAnnMaintenance is not { } original)
        {
            return;
        }

        var profile = configure.Definition.AnnMaintenance;
        if (profile is null || original.CommandId == Guid.Empty || original.Mode != AnnMaintenanceMode.Build || original.Consumer != configure.Consumer || original.IndexGeneration != configure.Definition.IndexGeneration || original.MaintenanceProfile != profile || original.Collection != profile.Collection || original.Field != profile.Field || original.Space != profile.Space)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidMaintenanceProfile);
        }

        var principal = engine.Principal(view, operation.UserId!, engine.Clock.GetUtcNow());
        Search.AnnProjectionPinValidation.RequireOwner(engine, view, principal, configure.Consumer.Partition, original);
    }

    internal static void RequireInitialMaintenanceReservation(ProjectionConsumerInfo previous, ConfigureProjectionConsumerRequest configure)
    {
        if (previous.InitialAnnMaintenance is not null && !previous.InitialAnnMaintenanceSettled && configure.CommandId != previous.MaintenanceAuthority?.ConfigureCommandId)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidMaintenanceAuthority);
        }

        if (configure.InitialAnnMaintenance is not { } supplied)
        {
            return;
        }

        if (previous.InitialAnnMaintenance is not { } original || !global::KeyLoad.AnnMaintenanceRequestIdentity.Same(original, supplied))
        {
            throw Errors.Fail(ErrorCode.Conflict, InvalidMaintenanceProfile);
        }
    }

    internal static void RequireProjectionMaintenanceProfile(ProjectionConsumerDefinition definition, AnnProjectionMaintenanceProfile profile)
    {
        if (profile.Space is null || definition.Resources.IsDefault || definition.MutationKinds.IsDefault || !definition.Resources.IsEmpty || !definition.MutationKinds.SequenceEqual(MaintenanceMutationKinds, StringComparer.Ordinal) || profile.Space.Dimension is < MinimumMaintenanceDimension or > MaximumMaintenanceDimension || !Enum.IsDefined(profile.Space.Metric))
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidMaintenanceProfile);
        }

        JsonData.Identifier(profile.Collection);
        JsonData.Identifier(profile.Field);
        JsonData.Identifier(profile.Space.Id);
        JsonData.Identifier(profile.Space.Model);
        JsonData.Identifier(profile.Space.Version);
    }

    internal static void RequireProjectionMaintenanceResource(ResourceDefinition resource, AnnProjectionMaintenanceProfile profile)
    {
        Search.VectorFieldProfiles.Validate(resource, ErrorCode.Corruption);
        var configured = resource.VectorProfiles.FirstOrDefault(item => item.Field == profile.Field);
        if (configured is not null && configured.Space != profile.Space)
        {
            throw Errors.Fail(ErrorCode.Validation, InvalidMaintenanceProfile);
        }
    }

    internal static void RequireProjectionMaintenanceAuthority(ProjectionConsumerInfo state, ProjectionMaintenanceAuthority? admitted)
    {
        if (state.MaintenanceAuthority is not { } original ? admitted is not null : admitted is null || original.ConfigureCommandId == Guid.Empty || original.PrincipalId != admitted.PrincipalId || original.Incarnation != admitted.Incarnation || original.PolicyEpoch != admitted.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, InvalidMaintenanceAuthority);
        }
    }
}
