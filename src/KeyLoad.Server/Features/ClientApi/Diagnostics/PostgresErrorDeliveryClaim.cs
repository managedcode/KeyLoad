namespace KeyLoad.Server;

internal sealed class PostgresErrorDeliveryClaim(PostgresErrorDeliveryArm arm, Guid connection,
    KeyLoadException primary, PostgresErrorDeliveryFiles files)
{
    internal Guid Arm => arm.Id;
    internal Guid Connection { get; } = connection;
    internal KeyLoadException Primary { get; } = primary;
    private bool receiverClosed;
    private bool ledgerJoined;
    private bool sessionJoined;

    internal void Publish(PostgresErrorDeliveryPhase phase, bool primaryFirst, int failures)
        => files.Write(new(arm.Session, arm.Id, Connection, phase, primaryFirst, Primary.Code, failures));

    internal void ReceiverClosed()
    {
        receiverClosed = true;
        Publish(PostgresErrorDeliveryPhase.ReceiverClosed, false, PostgresErrorDeliveryProtocol.None);
    }

    internal void Ledger(IReadOnlyList<Exception> failures)
    {
        if (!receiverClosed || failures.Count <= PostgresErrorDeliveryProtocol.FirstFlushFailure
            || !ReferenceEquals(failures[PostgresErrorDeliveryProtocol.PrimaryIndex], Primary)
            || !PostgresErrorDeliveryErrors.HasCancellation(failures[PostgresErrorDeliveryProtocol.FirstFlushFailure]))
        { throw new InvalidOperationException(PostgresErrorDeliveryProtocol.Invalid); }
        ledgerJoined = true;
        Publish(PostgresErrorDeliveryPhase.LedgerJoined, true, failures.Count);
    }

    internal void Session(IReadOnlyList<Exception> failures)
    {
        if (!ledgerJoined || !PostgresErrorDeliveryErrors.IsFirst(failures, Primary))
        { throw new InvalidOperationException(PostgresErrorDeliveryProtocol.Invalid); }
        sessionJoined = true;
        Publish(PostgresErrorDeliveryPhase.SessionJoined, true, PostgresErrorDeliveryErrors.Count(failures));
    }

    internal void ConnectionJoined(IReadOnlyList<Exception> failures)
    {
        if (!sessionJoined || !PostgresErrorDeliveryErrors.IsFirst(failures, Primary))
        { throw new InvalidOperationException(PostgresErrorDeliveryProtocol.Invalid); }
        Publish(PostgresErrorDeliveryPhase.ConnectionJoined, true, PostgresErrorDeliveryErrors.Count(failures));
    }
}
