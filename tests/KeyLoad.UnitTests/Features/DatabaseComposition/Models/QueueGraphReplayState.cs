namespace KeyLoad.UnitTests.Features.DatabaseComposition;

internal sealed record QueueGraphReplayState(
    QueueGraphIdentityState Source,
    ReplicatedOperation Reverse,
    byte[] ReverseReceipt,
    ReplicatedOperation Next)
{
    internal byte[] NextReceipt { get; set; } = [];
    internal const string OtherEndpoint = "other";
    internal const string ReversePrefix = "reverse-";
    internal const string NextPrefix = "next-";
    internal const string Continuation = "continuation";
    internal const string ContinuationJson = "{\"state\":\"continued\"}";
}
