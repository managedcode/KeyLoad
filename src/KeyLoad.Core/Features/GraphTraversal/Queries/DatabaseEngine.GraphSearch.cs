using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal GraphSearchReachability[] ReadGraphSearchReachability(IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, GraphWalkSpec walk, ReadExecutionBudget budget)
        => new GraphSearchReachabilityReader(this, view, principal, partition, walk, budget).Read();
}
