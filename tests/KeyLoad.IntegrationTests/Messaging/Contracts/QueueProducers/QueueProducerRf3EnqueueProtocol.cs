namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class QueueProducerRf3EnqueueProtocol
{
    internal const int PlacementVersion = 1;
    internal const long NoPosition = 0;
    internal const string PutKind = "putDocument";
    internal const string AppendKind = "appendEvents";
    internal const string EnqueueKind = "enqueue";
    internal const string UnexpectedMutation = "Unexpected durable enqueue producer mutation.";
}
