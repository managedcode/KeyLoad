using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferColdRefusals
{
    internal static async Task ExecuteAsync(ClusterFixture fixture, RemoteTransferColdSeed seed,
        QueueTransferInspection intent, CancellationToken token)
    {
        await RemoteTransferColdCallers.WithAsync(fixture, seed, async (sdk, mcp) =>
        {
            var changed = seed.Create with
            {
                CommandId = Guid.NewGuid(),
                Mutations =
                [new CreateQueueTransfer(seed.Scenario.SourceQueue, seed.TransferId, seed.Scenario.DestinationQueue,
                    seed.Message with { PayloadJson = RemoteTransferColdProtocol.ChangedPayload })]
            };
            await RefusedAsync(sdk, mcp, changed, ErrorCode.Conflict, token);
            var replacement = intent.IntentToken[RemoteTransferColdProtocol.FirstTokenCharacter]
                == RemoteTransferColdProtocol.FirstTokenReplacement ? RemoteTransferColdProtocol.SecondTokenReplacement
                : RemoteTransferColdProtocol.FirstTokenReplacement;
            var tampered = seed.Accept(intent) with
            {
                Mutations = [new AcceptQueueTransfer(seed.Scenario.DestinationQueue,
                string.Concat(replacement.ToString(), intent.IntentToken[RemoteTransferColdProtocol.TokenSuffixOffset..]))]
            };
            await RefusedAsync(sdk, mcp, tampered, ErrorCode.TokenInvalidated, token);
            await UnchangedAsync(sdk, mcp, seed, intent, token);
        }, token);
        var revoked = MessagingRf3Identity.WithCapability(seed.Identity.Principal, seed.Scenario.DestinationQueue,
            RemoteTransferColdProtocol.Target & ~Capability.QueuePublish);
        await MessagingRf3Identity.UpdateAsync(fixture, revoked, token);
        await RemoteTransferColdCallers.WithAsync(fixture, seed, async (sdk, mcp) =>
        {
            await RefusedAsync(sdk, mcp, seed.Accept(intent), ErrorCode.PermissionDenied, token);
            await UnchangedAsync(sdk, mcp, seed, intent, token);
        }, token);
        await MessagingRf3Identity.UpdateAsync(fixture, MessagingRf3Identity.WithCapability(revoked,
            seed.Scenario.DestinationQueue, RemoteTransferColdProtocol.Target), token);
    }

    internal static Task OriginalEpochRefusedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        RemoteTransferColdSeed seed, CancellationToken token)
        => RefusedAsync(sdk, mcp, seed.Create, ErrorCode.PermissionDenied, token);

    private static async Task RefusedAsync(KeyLoadClient sdk, McpOfficialClient mcp, CommandRequest command,
        ErrorCode expected, CancellationToken token)
    {
        await QueueProducerRf3Assertions.DeniedAsync(await sdk.CommitAsync(command, token), expected);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token), expected, true);
        await AtomicProducerRf3Routes.RefusedAsync(sdk, mcp, command, expected, token);
    }

    private static async Task UnchangedAsync(KeyLoadClient sdk, McpOfficialClient mcp, RemoteTransferColdSeed seed,
        QueueTransferInspection intent, CancellationToken token)
    {
        await RemoteTransferColdAssertions.SourceAsync(sdk, mcp, seed, intent, token);
        await RemoteTransferColdAssertions.ReceiptAsync(sdk, mcp, seed, null, token);
        await RemoteTransferColdAssertions.MessageAsync(sdk, mcp, seed, null, token);
    }
}
