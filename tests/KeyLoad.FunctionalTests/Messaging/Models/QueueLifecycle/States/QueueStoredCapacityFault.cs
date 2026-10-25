namespace KeyLoad.FunctionalTests.Messaging;

internal enum QueueStoredCapacityFault
{
    ZeroUsageWithBodies,
    RecordsWithoutBytes,
    BytesWithoutRecords,
    UsageWithoutBodies
}
