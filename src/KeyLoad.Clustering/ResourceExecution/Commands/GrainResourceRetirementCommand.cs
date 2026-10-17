using KeyLoad.Core;

namespace KeyLoad.Clustering;

internal static class GrainResourceRetirementCommand
{
    internal static string Resolve(DecodedGrainRequest request)
    {
        _ = Read(request);
        return GrainRoutingProtocol.CatalogPartition;
    }

    internal static Task<OperationResult> SubmitAsync(DatabaseEngine database, ICommitCoordinator coordinator,
        DecodedGrainRequest request, PrincipalRecord principal, ReadExecutionBudget work, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        token.ThrowIfCancellationRequested();
        return coordinator.SubmitVerifiedAsync(Verify(database, request, principal), token, work);
    }

    internal static ReplicatedOperation Verify(DatabaseEngine database, DecodedGrainRequest request, PrincipalRecord principal)
    {
        GrainRequestAuthority.RequireAdministrator(principal);
        var operation = database.VerifyOperationAuthority(Read(request));
        if (operation.PrincipalId != principal.Id)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
        return operation;
    }

    private static ReplicatedOperation Read(DecodedGrainRequest request)
    {
        if (!ResourceRetirementCommandScope.Validate(request.Envelope))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementReadScope.InvalidRequest);
        }
        var operation = GrainNativePayload.Read<ReplicatedOperation>(request.Payload);
        if (operation.Kind != OperationKind.ResourceRetirementPhase
            || operation.Id != request.Envelope.CommandId
            || operation.PrincipalId != request.Envelope.PrincipalId)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceRetirementReadScope.InvalidRequest);
        }
        return operation;
    }
}
