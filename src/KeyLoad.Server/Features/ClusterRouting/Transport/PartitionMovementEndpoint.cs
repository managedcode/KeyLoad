using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed class PartitionMovementEndpoint(PartitionMovementRuntime runtime,
    PartitionMovementPeerAdmission admission, PartitionMovementReceiver receiver,
    IOptions<NodeOptions> options, IOptions<GrainRoutingOptions> routing, IOptions<DatabaseLimits> limits, TimeProvider clock)
{
    internal PartitionMovementRuntime Runtime => runtime;
    internal PartitionMovementPeerAdmission Admission => admission;
    internal PartitionMovementReceiver Receiver => receiver;
    internal IOptions<NodeOptions> Options => options;
    internal IOptions<GrainRoutingOptions> Routing => routing;
    internal IOptions<DatabaseLimits> Limits => limits;
    internal TimeProvider Clock => clock;

    internal Task HandleOutcomeAsync(HttpContext context)
        => PartitionMovementEndpointOutcomeOperations.HandleOutcomeAsync(this, context);

    internal Task HandleReceiverIssueAsync(HttpContext context, bool query)
        => PartitionMovementEndpointReceiverIssueOperations.HandleReceiverIssueAsync(this, context, query);

    internal Task HandleTransferDataAsync(HttpContext context)
        => PartitionMovementEndpointTransferReadOperations.HandleTransferDataAsync(this, context);

    internal Task HandleAsync(HttpContext context)
        => PartitionMovementEndpointPhaseOperations.HandleAsync(this, context);

    internal Task HandleRetireCancellationAsync(HttpContext context, bool query)
        => PartitionMovementEndpointRetireCancellationOperations.HandleRetireCancellationAsync(this, context, query);
}
