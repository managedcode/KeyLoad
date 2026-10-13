namespace KeyLoad.Server;

internal enum PostgresErrorDeliveryPhase
{
    Buffered,
    ReceiverClosed,
    LedgerJoined,
    SessionJoined,
    ConnectionJoined
}
