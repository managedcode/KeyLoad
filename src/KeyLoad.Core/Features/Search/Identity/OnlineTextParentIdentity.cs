using System.Text.Json;

namespace KeyLoad.Core.Features.Search;

internal static class OnlineTextParentIdentity
{
    internal static string Fingerprint(string principalId, OnlineTextIndexMaintenanceRequest request)
        => JsonData.Fingerprint(new
        {
            Id = request.CommandId,
            Kind = OperationKind.MaintainOnlineTextIndex,
            PrincipalId = principalId,
            PayloadJson = JsonSerializer.Serialize(request, JsonDefaults.Options)
        });
}
