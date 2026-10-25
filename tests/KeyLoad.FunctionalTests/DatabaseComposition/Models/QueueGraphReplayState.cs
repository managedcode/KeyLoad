using ManagedCode.Communication.Commands;
namespace KeyLoad.FunctionalTests.DatabaseComposition;

internal sealed record QueueGraphReplayState(
    QueueGraphIdentityState Source,
    Command<DatabaseCommand> Reverse,
    byte[] ReverseReceipt,
    Command<DatabaseCommand> Next,
    EntityRef ContinuationReference)
{
    internal byte[] NextReceipt { get; set; } = [];
    internal const string OtherEndpoint = "other";
    internal const string ReversePrefix = "reverse-";
    internal const string NextPrefix = "next-";
    internal const string Continuation = "continuation";
    internal const string ContinuationJson = "{\"state\":\"continued\"}";
}
