namespace KeyLoad;

[Orleans.GenerateSerializer]
[Orleans.Alias(GraphCrossPartitionInternalAliases.CapacityDirection)]
internal enum GraphCrossPartitionCapacityDirection
{
    PendingIntents = 0,
    ReceiverStates = 1
}
