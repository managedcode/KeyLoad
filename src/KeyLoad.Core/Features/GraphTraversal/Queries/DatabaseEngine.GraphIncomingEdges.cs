using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Validation;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Reads the bounded eventual reverse projection for one fully qualified vertex.</summary>
    /// <param name="principalId">The persisted caller identity.</param>
    /// <param name="request">The target graph vertex and bounded result limit.</param>
    /// <param name="timeProvider">The operation clock used for its deadline.</param>
    /// <param name="cancellationToken">Cancels all scan and point-read work.</param>
    /// <returns>Visible incoming canonical edges at one local read cut.</returns>
    public GraphIncomingEdgesPageV1 ReadIncomingGraphEdges(string principalId,
        ReadIncomingGraphEdgesRequestV1 request, TimeProvider? timeProvider = null,
        CancellationToken cancellationToken = default)
    {
        var budget = new ReadExecutionBudget(Limits, timeProvider ?? Clock, cancellationToken);
        budget.Check();
        GraphCrossPartitionValidation.ValidateIncoming(request, Limits, budget);
        return Store.Read(view =>
        {
            budget.Check();
            var grant = budget.CreateReadGrant(Limits.MaxQueryReadBytes, Limits.MaxScanRecords);
            var principal = GraphIncomingReadScope.ReadPrincipal(view, grant, principalId,
                (timeProvider ?? Clock).GetUtcNow());
            return new GraphIncomingEdgesReader(this, view, principal, request, budget, grant,
                Store.Position).Read();
        });
    }
}
