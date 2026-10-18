using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal sealed class EventCurrentReadAuthorityClusterState(McpEventStreamScenario scenario,
    McpPersistedIdentity identity, CommandRequest seed, CommitReceipt seedReceipt, StreamPage original, OutboxStatus outbox)
{
    internal McpEventStreamScenario Scenario { get; } = scenario;
    internal McpPersistedIdentity Identity { get; } = identity;
    internal CommandRequest Seed { get; } = seed;
    internal CommitReceipt SeedReceipt { get; } = seedReceipt;
    internal PrincipalRecord CurrentPrincipal { get; set; } = identity.Principal;
    internal StreamPage CurrentPage { get; set; } = original;
    internal OutboxStatus CurrentOutbox { get; set; } = outbox;
    internal StreamPage? OriginalSdkCapture
    {
        get; set;
    }
    internal StreamPage? OriginalMcpCapture
    {
        get; set;
    }
    internal StreamPage? RepairedSdkCapture
    {
        get; set;
    }
    internal StreamPage? RepairedMcpCapture
    {
        get; set;
    }
    internal CommandRequest? FreshCommand
    {
        get; set;
    }
    internal CommitReceipt? FreshReceipt
    {
        get; set;
    }
    internal string BeforeNode { get; set; } = string.Empty;
    internal Guid BeforeIncarnation
    {
        get; set;
    }
    internal bool Updated
    {
        get; set;
    }
}
