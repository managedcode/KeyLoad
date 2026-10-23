using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Validation;

namespace KeyLoad.Core;
/// <summary>Executes GraphIncomingEdges operations against the caller's database context.</summary>
public static class GraphIncomingEdges
{
    /// <summary>Reads the bounded eventual reverse projection for one fully qualified vertex.</summary>
    /// <param name = "principalId">The persisted caller identity.</param>
    /// <param name = "request">The target graph vertex and bounded result limit.</param>
    /// <param name = "timeProvider">The operation clock used for its deadline.</param>
    /// <param name = "cancellationToken">Cancels all scan and point-read work.</param>
    /// <returns>Visible incoming canonical edges at one local read cut.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static GraphIncomingEdgesPage ReadIncomingGraphEdges(this DatabaseEngine engine, string principalId, ReadIncomingGraphEdgesRequest request, TimeProvider? timeProvider = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var budget = new ReadExecutionBudget(engine.OperationLimitsOptions, timeProvider ?? engine.Clock, cancellationToken);
        return engine.ReadIncomingGraphEdgesRead(principalId, request, timeProvider ?? engine.Clock, budget);
    }

    internal static GraphIncomingEdgesPage ReadIncomingGraphEdgesRead(this DatabaseEngine engine, string principalId, ReadIncomingGraphEdgesRequest request, TimeProvider clock, ReadExecutionBudget budget)
    {
        budget.Check();
        GraphCrossPartitionValidation.ValidateIncoming(request, engine.Limits, budget);
        return engine.Store.Read(view =>
        {
            budget.Check();
            var grant = budget.CreateReadGrant(engine.Limits.MaxQueryReadBytes, engine.Limits.MaxScanRecords);
            var principal = GraphIncomingReadScope.ReadPrincipal(view, grant, principalId, clock.GetUtcNow());
            return new GraphIncomingEdgesReader(engine, view, principal, request, budget, grant, engine.Store.Position).Read();
        });
    }
}
