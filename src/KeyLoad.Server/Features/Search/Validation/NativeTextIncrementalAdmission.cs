using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.Search;

internal static class NativeTextIncrementalAdmission
{
    private const long EmptyGeneration = 0;
    private const string Invalid = "The native text maintenance identity or phase is invalid.";

    internal static void Request(TextMaintenanceCapabilityRequest request, Guid nodeId)
    {
        var maintenance = request.Maintenance;
        if (maintenance.CommandId == Guid.Empty || request.SessionId == Guid.Empty
            || maintenance.NodeId != nodeId || maintenance.IndexGeneration <= EmptyGeneration
            || !Enum.IsDefined(maintenance.Mode) || !Enum.IsDefined(request.Kind)
            || (request.Kind == TextMaintenanceCapabilityKind.Release) != (maintenance.Mode == TextIndexMaintenanceMode.Release))
        { throw Errors.Fail(ErrorCode.Validation, Invalid); }
    }
}
