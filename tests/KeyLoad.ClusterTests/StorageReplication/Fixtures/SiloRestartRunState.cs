using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.ClusterTests.StorageReplication;

internal sealed record SiloRestartRunState(KeyLoadClient[] Clients, PartitionRef Partition,
    ProjectionConsumerRef ProjectionConsumer, SubscriptionRef Subscription, CommandRequest Command,
    CommitReceipt Receipt, AstQueryRequest LiveRequest, LiveQuerySnapshot LiveSnapshot, int[] Survivors);
