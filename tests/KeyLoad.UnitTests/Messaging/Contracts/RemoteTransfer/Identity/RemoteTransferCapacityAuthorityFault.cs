namespace KeyLoad.UnitTests.Features.Messaging;

internal enum RemoteTransferCapacityAuthorityFault
{
    Missing,
    Empty,
    UndersizedBytes,
    UndersizedCount,
    MissingRecords
}
