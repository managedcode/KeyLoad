namespace KeyLoad.UnitTests.Messaging;

internal enum RemoteTransferCapacityAuthorityFault
{
    Missing,
    Empty,
    UndersizedBytes,
    UndersizedCount,
    MissingRecords
}
