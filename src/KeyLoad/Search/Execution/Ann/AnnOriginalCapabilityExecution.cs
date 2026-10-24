using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server.Search;

internal static class AnnOriginalCapabilityExecution
{
    private const int EmptyCount = 0;
    private const long EmptySequence = 0;

    internal static void Require(AnnMaintenanceCapabilityRequest request)
    {
        if (request.Kind is not (AnnMaintenanceCapabilityKind.AdmitOriginal or AnnMaintenanceCapabilityKind.SettleOriginalResult))
        {
            if (request.OriginalConfigure is not null || request.FullOriginalResult is not null)
            {
                throw Errors.Fail(ErrorCode.Validation, AnnProtocol.InvalidSource);
            }
            return;
        }
        if (request.SessionId == Guid.Empty || request.Maintenance.Mode == AnnMaintenanceMode.Release
            || request.Page is not null || request.CheckpointIntent is not null
            || request.Kind == AnnMaintenanceCapabilityKind.AdmitOriginal
                && (request.OriginalConfigure is null || request.FullOriginalResult is not null)
            || request.Kind == AnnMaintenanceCapabilityKind.SettleOriginalResult
                && (request.FullOriginalResult is null || request.OriginalConfigure is not null))
        {
            throw Errors.Fail(ErrorCode.Validation, AnnProtocol.InvalidSource);
        }
    }

    internal static async Task<AnnMaintenanceCapabilityResult> ExecuteAsync(AnnMaintenanceService execution,
        PrincipalRecord principal, AnnMaintenanceCapabilityRequest request, CancellationToken token, ReadExecutionBudget? originalBudget = null)
    {
        if (request.Kind == AnnMaintenanceCapabilityKind.SettleOriginalResult)
        {
            execution.RequireOriginalCapabilitySession(principal, request);
        }
        var original = await execution.ExecuteOriginalCapabilityAsync(principal, request, token, originalBudget);
        var result = original.Result;
        return new(request.SessionId, result?.Source, result?.Source?.ThroughSequence ?? EmptySequence,
            result?.Count ?? EmptyCount, null, result?.IndexSha256,
            result?.Checkpoint?.Checkpoint ?? EmptySequence, false, result)
        {
            EnrollmentSettlementIntent = original.Intent
        };
    }
}
