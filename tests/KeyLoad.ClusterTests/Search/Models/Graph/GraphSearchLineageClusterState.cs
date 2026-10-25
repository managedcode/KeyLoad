using KeyLoad.ClusterTests.Messaging;

namespace KeyLoad.ClusterTests.Search;

internal sealed record GraphSearchLineageClusterState(PartitionRef Partition, MessagingClusterIdentity Identity,
    CommandRequest Seed, CommitReceipt Receipt, OutboxStatus Outbox, bool Expansion, bool EmptyLabels,
    EntityRefLifetime Lifetime)
{
    internal EntityRef StoredVertex(string id) => GraphSearchLineageClusterRecipe.Vertex(Partition, id) with { Lifetime = Lifetime };

    internal GraphSearchRequest Request => GraphSearchLineageClusterRecipe.Request(Partition, Expansion,
        EmptyLabels ? [] : [GraphSearchLineageClusterProtocol.Label]);
    internal GraphSearchRequest MissingRequest
    {
        get
        {
            var request = Request;
            return request with
            {
                Scope = new(request.Scope!.Walk with
                {
                    Seeds = [GraphSearchLineageClusterRecipe.Vertex(Partition, GraphSearchLineageClusterProtocol.Absent)]
                })
            };
        }
    }
}
