using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class AnnMaintenanceLifecycle
{
    private const int Empty = 0;

    internal static async Task<AnnMaintenanceCapabilityResult> ReleaseAsync(DatabaseEngine database,
        PrincipalRecord principal, AnnMaintenanceCapabilityRequest request, string directory,
        Func<AnnGenerationOwner?> getOwner, Func<AnnGenerationOwner> openOwner, CancellationToken token)
    {
        if (request.Maintenance.Mode != AnnMaintenanceMode.Release)
        {
            throw Errors.Fail(ErrorCode.Validation, AnnProtocol.InvalidSource);
        }
        AnnReleaseValidation.Require(database, principal, request.Maintenance, token);
        var actual = getOwner();
        if (actual is null && Directory.Exists(Path.Combine(directory, AnnProtocol.RootDirectory)))
        {
            actual = openOwner();
        }
        if (actual is not null)
        {
            await actual.RunAsync(_ =>
            {
                AnnReleaseValidation.Require(database, principal, request.Maintenance, token);
                actual.ReleaseGeneration(new(request.Maintenance.Consumer, request.Maintenance.IndexGeneration));
                return true;
            });
        }
        return new(request.SessionId, null, Empty, Empty, null, null, Empty, false);
    }

    internal static async Task DisposeAsync(Task originalOperations,
        Func<AnnGenerationOwner?> getOwner, Action clearSession)
    {
        await originalOperations;
        var actual = getOwner();
        if (actual is not null)
        {
            await actual.DisposeAsync();
        }
        clearSession();
    }
}
