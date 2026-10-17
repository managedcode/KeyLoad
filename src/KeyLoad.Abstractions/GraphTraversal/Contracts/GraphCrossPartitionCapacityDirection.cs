namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionInternalAliases.CapacityDirection)]
internal enum GraphCrossPartitionCapacityDirection
{
    [Orleans.Id(0)] PendingIntents = 0,
    [Orleans.Id(1)] ReceiverStates = 1
}
