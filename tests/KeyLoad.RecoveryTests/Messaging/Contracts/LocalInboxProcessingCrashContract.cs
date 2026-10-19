namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class LocalInboxProcessingCrashContract
{
    internal const string Mode = "local-queue-processing";
    internal const string OperationFile = "local-processing-operation.bin";
    internal const string InputFile = "local-processing-input.bin";
    internal const string PositionFile = "local-processing-seed-position.bin";
    internal const string TargetFile = "local-processing-canonical-target.bin";
    internal const string CollectionFile = "local-processing-collection-command.bin";
    internal const string TailFile = "local-processing-seed-tail.bin";
    internal const string Input = "local-input";
    internal const string Output = "local-output";
    internal const string Documents = "local-documents";
    internal const string Events = "local-events";
    internal const string Message = "work";
    internal const string Effect = "effect";
    internal const string Healthy = "healthy";
    internal const string Handler = "handler";
    internal const string Payload = "{\"processed\":true}";
    internal const long Generation = 1L;
    internal const long FinalVersion = 3L;
    internal const int MutationCount = 3;
    private const string OriginalProcessingCommandIdentity = "352d5cb7-9af5-47c1-94f8-bdb98a532581";
    internal static Guid CommandId { get; } = Guid.Parse(OriginalProcessingCommandIdentity);
    internal static PartitionRef Partition
    {
        get;
    } = new(CrashFixtureValues.Tenant, CrashFixtureValues.Database,
        CrashFixtureValues.Orders, Mode);
    internal static QueueLaneRef InputLane { get; } = new(Partition, Input);
    internal static QueueLaneRef OutputLane { get; } = new(Partition, Output);
}
