namespace KeyLoad.CrashHost.Features.Messaging;

internal static class TargetInboxCrashProtocol
{
    internal const string Mode = "target-inbox-processing";
    internal const string OperationFile = "target-inbox-operation.json";
    internal const string DeliveryFile = "target-inbox-delivery.json";
    internal const string SourceKey = "source";
    internal const string TargetKey = "target";
    internal const string Input = "input";
    internal const string Target = "inbox";
    internal const string Output = "output";
    internal const string Collection = "results";
    internal const string Message = "work";
    internal const string Handler = "handler";
    internal const string Effect = "effect";
    internal const string Payload = "{\"processed\":true}";
    internal const string Command = "1051ca9f-16f5-4f59-a306-70a463d9577f";
    internal const long Generation = 1;
    internal const long EmptyRevision = 0;
    internal const long ReceiptLimit = 2;
    internal const long ByteLimit = 65536;
    internal const long PositionStep = 1;
}
