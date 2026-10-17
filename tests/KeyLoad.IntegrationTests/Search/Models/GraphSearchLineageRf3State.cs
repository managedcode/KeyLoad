using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.Search;

internal sealed record GraphSearchLineageRf3State(PartitionRef Partition, MessagingRf3Identity Identity,
    CommandRequest Seed, CommitReceipt Receipt, OutboxStatus Outbox, bool Expansion, bool EmptyLabels)
{
    internal GraphSearchRequest Request => GraphSearchLineageRf3Recipe.Request(Partition, Expansion,
        EmptyLabels ? [] : [GraphSearchLineageRf3Protocol.Label]);
    internal GraphSearchRequest MissingRequest
    {
        get
        {
            var request = Request;
            return request with
            {
                Scope = new(request.Scope!.Walk with
                {
                    Seeds = [GraphSearchLineageRf3Recipe.Vertex(Partition, GraphSearchLineageRf3Protocol.Absent)]
                })
            };
        }
    }
}
