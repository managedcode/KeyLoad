namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class RetentionAckArchiveR53Protocol
{
    internal const string Group = "contiguous-archive";
    internal const string PinDetail = "The topic purge cut is pinned by a subscription checkpoint.";
    internal const string SafeCapacity = "The operation exceeds the available resource capacity.";
    internal const string PurgeKind = "purgeTopic";
    internal const string AckKind = "Ack";
    internal const string Missing = "The authentic retention archive state is absent.";
}
