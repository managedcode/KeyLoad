using KeyLoad.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

internal static class GrainControlledReadCapabilities
{
    internal static bool Handles(GrainReadKind kind)
        => kind is GrainReadKind.ControlledDocument or GrainReadKind.PartitionMovementOutcome;

    internal static object Execute(DatabaseEngine database, IServiceProvider services,
        TimeProvider clock, PrincipalRecord principal, DecodedGrainRequest request,
        CancellationToken cancellationToken)
    {
        var limits = services.GetRequiredService<IOptions<DatabaseLimits>>();
        return request.Envelope.ReadKind switch
        {
            GrainReadKind.ControlledDocument => ControlledDocumentReadExecution.Execute(database,
                limits, clock, principal, request.Envelope,
                GrainNativePayload.Read<ControlledDocumentReadRequest>(request.Payload), cancellationToken),
            GrainReadKind.PartitionMovementOutcome => PartitionMovementOutcomeExecution.Execute(database,
                principal, GrainNativePayload.Read<PartitionMovementOutcomeQuery>(request.Payload),
                limits, clock, cancellationToken),
            _ => throw Errors.Fail(ErrorCode.UnsupportedCapability, GrainRoutingProtocol.InvalidRequest)
        };
    }
}
