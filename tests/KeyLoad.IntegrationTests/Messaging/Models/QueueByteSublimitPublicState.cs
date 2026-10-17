using System.Text.Json;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueByteSublimitPublicState(QueueLifecyclePublicState Owner,
    ReceiveRequest Request, ReceiveResult Claim, Delivery Delivery)
{
    internal List<(CommandRequest Request, JsonElement Problem)> Refusals { get; } = [];
}
