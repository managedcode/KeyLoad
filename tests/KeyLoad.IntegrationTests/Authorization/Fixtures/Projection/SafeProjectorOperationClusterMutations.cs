using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Authorization;

internal static class SafeProjectorOperationClusterMutations
{
    internal static async Task RoundtripAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, long epoch, CancellationToken token)
    {
        string? projected = null;
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            var document = await PublicReadAssertions.DocumentAsync(await reader.Sdk.GetAsync(history.State.First, token))
                ?? throw new InvalidOperationException(SafeProjectorOperationClusterProtocol.MissingDocument);
            await Assert.That(document.Redacted).IsTrue();
            await Assert.That(document.Json?.Contains(SafeProjectorOperationClusterProtocol.Canary, StringComparison.Ordinal) is true).IsFalse();
            projected = document.Json;
        }, token);
        var actualJson = projected ?? throw new InvalidOperationException(SafeProjectorOperationClusterProtocol.MissingDocument);
        foreach (var explicitReplacement in new[] { false, true })
        {
            await BothRefusedAsync(root, reader, history, epoch,
                [new PutDocument(history.State.Collection, SafeProjectorOperationClusterProtocol.First,
                    actualJson, history.Revision, ExplicitReplacement: explicitReplacement)],
                SafeProjectorOperationClusterProtocol.ReplacementDenied, token);
        }
    }

    internal static async Task ProtectedRefusedAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, long epoch, CancellationToken token)
    {
        await BothRefusedAsync(root, reader, history, epoch,
            [new PatchDocument(history.State.Collection, SafeProjectorOperationClusterProtocol.First,
                [new(SafeProjectorOperationClusterProtocol.LiteralPath, PatchKind.Set,
                    JsonSerializer.Serialize(SafeProjectorOperationClusterProtocol.Canary, JsonDefaults.Options))], history.Revision)],
            SafeProjectorOperationClusterProtocol.WriteDenied, token);
    }

    private static async Task BothRefusedAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, long epoch, ImmutableArray<Mutation> mutations, string detail, CancellationToken token)
    {
        foreach (var official in new[] { false, true })
        {
            var request = new CommandRequest(Guid.NewGuid(), history.State.Partition, mutations);
            history.Refusals.Add((request, detail, epoch));
            await SafeProjectorOperationClusterImages.MutationAsync(root, history, async () =>
            {
                if (official)
                {
                    await SafeProjectorOperationClusterErrors.McpAsync(await reader.Mcp.CallAsync(
                        McpCallerTools.DocumentsCommit, request, token), ErrorCode.PermissionDenied, history.State);
                    return;
                }
                await SafeProjectorOperationClusterErrors.SdkAsync(await reader.Sdk.CommitAsync(request, token),
                    ErrorCode.PermissionDenied, detail);
            }, token);
        }
    }

    internal static async Task PatchAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, bool healthy, CancellationToken token)
    {
        var fields = ImmutableArray.CreateBuilder<FieldPatch>();
        fields.Add(new(SafeProjectorOperationClusterProtocol.StatusPath, PatchKind.Set, JsonSerializer.Serialize(
            healthy ? SafeProjectorOperationClusterProtocol.HealthyStatus : SafeProjectorOperationClusterProtocol.UpdatedStatus, JsonDefaults.Options)));
        if (healthy)
        {
            fields.Add(new(SafeProjectorOperationClusterProtocol.LiteralPath, PatchKind.Set,
                JsonSerializer.Serialize(SafeProjectorOperationClusterProtocol.Canary, JsonDefaults.Options)));
        }
        var request = new CommandRequest(Guid.NewGuid(), history.State.Partition,
            [new PatchDocument(history.State.Collection, SafeProjectorOperationClusterProtocol.First, fields.ToImmutable(), history.Revision)]);
        var actual = await reader.Sdk.CommitAsync(request, token);
        if (healthy)
        {
            history.HealthyPatch = request;
            history.HealthyReceipt = actual.Value;
        }
        else
        {
            history.PublicPatch = request;
            history.PublicReceipt = actual.Value;
        }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await SafeProjectorOperationClusterReceipts.PatchAsync(
            await McpCallerAssertions.SdkSuccessAsync(actual), history.State, request.CommandId, history.Revision), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SafeProjectorOperationClusterImages.CaptureAsync(root, history, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await SafeProjectorOperationClusterReplay.PatchAsync(root, reader, history, healthy,
            healthy ? SafeProjectorOperationClusterProtocol.FullWriteEpoch : SafeProjectorOperationClusterProtocol.WithdrawnUseEpoch, token);
    }
}
