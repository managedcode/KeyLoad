namespace KeyLoad.UnitTests.Features.Messaging;

internal enum QueueStoredCapacityFault
{
    ZeroUsageWithBodies,
    RecordsWithoutBytes,
    BytesWithoutRecords,
    UsageWithoutBodies
}
