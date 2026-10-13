using System.Text.Json;
namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed record QueueScheduledCancelPublicState(QueueLifecyclePublicState Owner, DateTimeOffset Due)
{
    internal List<(CommandRequest Request, ErrorCode Code, JsonElement Problem)> Failures { get; } = [];
    internal CommandRequest? Denied { get; set; }
    internal JsonElement? DeniedProblem { get; set; }
}
