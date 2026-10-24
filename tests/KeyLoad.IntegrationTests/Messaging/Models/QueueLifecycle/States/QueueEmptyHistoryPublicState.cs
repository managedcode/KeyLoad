using System.Text.Json;

namespace KeyLoad.IntegrationTests.Messaging;

internal sealed record QueueEmptyHistoryPublicState(QueueLifecyclePublicState Owner,
    ReceiveRequest EmptyRequest, ReceiveResult EmptyResult)
{
    internal JsonElement? ChangedProblem
    {
        get; set;
    }
}
