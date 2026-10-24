namespace KeyLoad.UnitTests.Messaging;

internal enum QueueStoredCapacityFault
{
    ZeroUsageWithBodies,
    RecordsWithoutBytes,
    BytesWithoutRecords,
    UsageWithoutBodies
}
