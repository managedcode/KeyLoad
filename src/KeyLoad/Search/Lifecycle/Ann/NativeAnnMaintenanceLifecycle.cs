using KeyLoad.Clustering;
using KeyLoad.Core;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnMaintenanceLifecycle
{
    private const int Empty = 0;

    internal static async Task<AnnMaintenanceCapabilityResult> ReleaseAsync(DatabaseEngine database,
        PrincipalRecord principal, AnnMaintenanceCapabilityRequest request, string directory,
        Func<NativeAnnGenerationOwner?> getOwner, Func<NativeAnnGenerationOwner> openOwner, CancellationToken token)
    {
        if (request.Maintenance.Mode != AnnMaintenanceMode.Release)
        {
            throw Errors.Fail(ErrorCode.Validation, NativeAnnProtocol.InvalidSource);
        }
        NativeAnnReleaseValidation.Require(database, principal, request.Maintenance, token);
        var actual = getOwner();
        if (actual is null && Directory.Exists(Path.Combine(directory, NativeAnnProtocol.RootDirectory)))
        {
            actual = openOwner();
        }
        if (actual is not null)
        {
            await actual.RunAsync(_ =>
            {
                NativeAnnReleaseValidation.Require(database, principal, request.Maintenance, token);
                actual.ReleaseGeneration(new(request.Maintenance.Consumer, request.Maintenance.IndexGeneration));
                return true;
            }).ConfigureAwait(false);
        }
        return new(request.SessionId, null, Empty, Empty, null, null, Empty, false);
    }

    internal static async Task DisposeAsync(Task originalOperations,
        Func<NativeAnnGenerationOwner?> getOwner, Action clearSession)
    {
        await originalOperations.ConfigureAwait(false);
        var actual = getOwner();
        if (actual is not null)
        {
            await actual.DisposeAsync().ConfigureAwait(false);
        }
        clearSession();
    }
}
