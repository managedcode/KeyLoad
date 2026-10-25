namespace KeyLoad.FunctionalTests.Messaging;

internal sealed record QueueExpiredDlqState(QueueLifecycleTestState Operations, DateTimeOffset Expiry,
    byte[] ParkedBody, byte[] PendingBody);
