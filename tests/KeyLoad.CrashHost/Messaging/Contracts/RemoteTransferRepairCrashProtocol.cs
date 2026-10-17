namespace KeyLoad.CrashHost.Features.Messaging;

internal static class RemoteTransferRepairCrashProtocol
{
    internal const string AcceptMode = "remote-transfer-policy-accept-repair";
    internal const string CompleteMode = "remote-transfer-policy-complete-repair";
    internal const string OperationFile = "transfer-repair-operation.json";
    internal const string OriginalFile = "transfer-repair-original-command.json";
    internal const string FailureFile = "transfer-repair-original-failure.bin";
    internal const string Missing = "The actual policy repair process state is missing.";
    internal const int Ceiling = 3;
    internal const long Step = 1;
    internal const long InitialGeneration = 1;
    internal const long RepairedGeneration = 2;
    internal const long OneRecord = 1;
    internal const string EmptyReceipt = "";
}
