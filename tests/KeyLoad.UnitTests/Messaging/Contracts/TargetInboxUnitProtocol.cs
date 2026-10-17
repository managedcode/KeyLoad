namespace KeyLoad.UnitTests.Features.Messaging;

internal static class TargetInboxUnitProtocol
{
    internal const string SourcePartition = "source";
    internal const string Input = "input";
    internal const string Target = "target";
    internal const string Output = "output";
    internal const string Collection = "results";
    internal const string Events = "events";
    internal const string Stream = "processed";
    internal const string Message = "work";
    internal const string Handler = "handler";
    internal const string Document = "effect";
    internal const string Changed = "changed";
    internal const string EventType = "Processed";
    internal const string Payload = "{\"done\":true}";
    internal const string Worker = "worker";
    internal const string Root = "root";
    internal const long Generation = 1;
    internal const long FirstRevision = 1;
    internal const long BadRevision = 9;
    internal const long EmptyRevision = 0;
    internal const long ReceiptCapacity = 2;
    internal const long ByteCapacity = 65_536;
    internal const long DemotedEpoch = 2;
    internal const long RestoredEpoch = 3;
    internal const int MaximumFixtureRows = 256;
}
