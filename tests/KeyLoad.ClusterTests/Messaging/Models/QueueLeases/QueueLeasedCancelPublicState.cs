using System.Text.Json;

namespace KeyLoad.ClusterTests.Messaging;

internal sealed record QueueLeasedCancelPublicState(QueueLifecyclePublicState Owner,
    ReceiveRequest Claim, ReceiveResult Claimed, MessageInspection Leased)
{
    internal CommandRequest? Denied
    {
        get; set;
    }
    internal JsonElement? DeniedProblem
    {
        get; set;
    }
    internal List<(CommandRequest Command, ErrorCode Code, JsonElement Problem)> Failures { get; } = [];
}
