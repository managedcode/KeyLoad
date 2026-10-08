using KeyLoad.Core.Features.Search;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnMaintenanceAdmission
{
    private const long EmptyGeneration = 0;

    internal static IOptions<AnnSeedOptions> Seeds(IOptions<AnnSeedOptions> configured, long available)
        => NativeAnnSeedOptionsFactory.Create(configured, available);

    internal static void Request(AnnMaintenanceRequest request, Guid sessionId, Guid nodeId)
    {
        if (request.CommandId == Guid.Empty || sessionId == Guid.Empty || request.NodeId != nodeId
            || !Enum.IsDefined(request.Mode) || request.IndexGeneration <= EmptyGeneration)
        { throw Errors.Fail(ErrorCode.Validation, NativeAnnProtocol.InvalidSource); }
    }
}
