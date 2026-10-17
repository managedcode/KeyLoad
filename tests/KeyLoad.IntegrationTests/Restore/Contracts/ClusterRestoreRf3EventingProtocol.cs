namespace KeyLoad.IntegrationTests.Features.BackupRestore;

internal static class ClusterRestoreRf3EventingProtocol
{
    internal const string Topic = "restore-topic";
    internal const string Queue = "restore-event-queue";
    internal const string HealthyQueue = "restore-healthy-queue";
    internal const string Documents = "restore-event-documents";
    internal const string DocumentId = "processed";
    internal const string MessageId = "derived";
    internal const string Group = "processed";
    internal const string OtherGroup = "unread";
    internal const string Handler = "restore-event-handler";
    internal const string First = "one";
    internal const string Second = "two";
    internal const string Third = "three";
    internal const string Healthy = "four";
    internal const string EventType = "Created";
    internal const string Json = "{\"retained\":true}";
    internal const long InitialGeneration = 1;
    internal const int SingleDelivery = 1;
    internal const long ReconciledOwnershipEpoch = 2;
    internal const long ReconciledGeneration = 2;
    internal const long InitialOwnershipEpoch = 1;
    internal const long EmptyCheckpoint = 0;
    internal const long FirstPosition = 1;
    internal const long LastSourcePosition = 3;
    internal const long HealthyPosition = 4;
    internal const int SourceEvents = 3;
    internal const int FirstIndex = 0;
    internal const int ThirdIndex = 2;
    internal const long FirstRevision = 1;
    internal const string OutboxStatus = "keyload_outbox_status";
}
