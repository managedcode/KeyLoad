using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes GraphTraversalQueriesGraphSearch operations against the caller's database context.</summary>
public static class GraphTraversalQueriesGraphSearch
{
    internal static GraphSearchReachability[] ReadGraphSearchReachability(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, GraphWalkSpec walk, ReadExecutionBudget budget) => new GraphSearchReachabilityReader(engine, view, principal, partition, walk, budget).Read();
}
