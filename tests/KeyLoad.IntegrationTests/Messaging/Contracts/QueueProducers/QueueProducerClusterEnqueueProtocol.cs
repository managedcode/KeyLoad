namespace KeyLoad.IntegrationTests.Messaging;

internal static class QueueProducerClusterEnqueueProtocol
{
    internal const long NoPosition = 0;
    internal const string PutKind = "putDocument";
    internal const string AppendKind = "appendEvents";
    internal const string EnqueueKind = "enqueue";
    internal const string UnexpectedMutation = "Unexpected durable enqueue producer mutation.";
}
