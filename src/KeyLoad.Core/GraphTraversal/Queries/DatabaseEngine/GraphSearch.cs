using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes GraphSearch operations against the caller's database context.</summary>
public static class GraphSearch
{
    internal static GraphSearchReachability[] ReadGraphSearchReachability(this DatabaseEngine engine, IKeyValueView view, PrincipalRecord principal, PartitionRef partition, GraphWalkSpec walk, ReadExecutionBudget budget) => new GraphSearchReachabilityReader(engine, view, principal, partition, walk, budget).Read();
}
