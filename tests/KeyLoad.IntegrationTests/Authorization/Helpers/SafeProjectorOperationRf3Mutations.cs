using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationRf3Mutations
{
    internal static async Task RoundtripAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, long epoch, CancellationToken token)
    {
        string? projected = null;
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            var document = await PublicReadAssertions.DocumentAsync(await reader.Sdk.GetAsync(history.State.First, token))
                ?? throw new InvalidOperationException(SafeProjectorOperationRf3Protocol.MissingDocument);
            await Assert.That(document.Redacted).IsTrue();
            await Assert.That(document.Json?.Contains(SafeProjectorOperationRf3Protocol.Canary, StringComparison.Ordinal) is true).IsFalse();
            projected = document.Json;
        }, token);
        var actualJson = projected ?? throw new InvalidOperationException(SafeProjectorOperationRf3Protocol.MissingDocument);
        foreach (var explicitReplacement in new[] { false, true })
        {
            await BothRefusedAsync(root, reader, history, epoch,
                [new PutDocument(history.State.Collection, SafeProjectorOperationRf3Protocol.First,
                    actualJson, history.Revision, ExplicitReplacement: explicitReplacement)],
                SafeProjectorOperationRf3Protocol.ReplacementDenied, token);
        }
    }

    internal static async Task ProtectedRefusedAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, long epoch, CancellationToken token)
    {
        await BothRefusedAsync(root, reader, history, epoch,
            [new PatchDocument(history.State.Collection, SafeProjectorOperationRf3Protocol.First,
                [new(SafeProjectorOperationRf3Protocol.LiteralPath, PatchKind.Set,
                    JsonSerializer.Serialize(SafeProjectorOperationRf3Protocol.Canary, JsonDefaults.Options))], history.Revision)],
            SafeProjectorOperationRf3Protocol.WriteDenied, token);
    }

    private static async Task BothRefusedAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, long epoch, ImmutableArray<Mutation> mutations, string detail, CancellationToken token)
    {
        foreach (var official in new[] { false, true })
        {
            var request = new CommandRequest(Guid.NewGuid(), history.State.Partition, mutations);
            history.Refusals.Add((request, detail, epoch));
            await SafeProjectorOperationRf3Images.MutationAsync(root, history, async () =>
            {
                if (official)
                {
                    await SafeProjectorOperationRf3Errors.McpAsync(await reader.Mcp.CallAsync(
                        McpCallerTools.DocumentsCommit, request, token), ErrorCode.PermissionDenied, history.State);
                    return;
                }
                await SafeProjectorOperationRf3Errors.SdkAsync(await reader.Sdk.CommitAsync(request, token),
                    ErrorCode.PermissionDenied, detail);
            }, token);
        }
    }

    internal static async Task PatchAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, bool healthy, CancellationToken token)
    {
        var fields = ImmutableArray.CreateBuilder<FieldPatch>();
        fields.Add(new(SafeProjectorOperationRf3Protocol.StatusPath, PatchKind.Set, JsonSerializer.Serialize(
            healthy ? SafeProjectorOperationRf3Protocol.HealthyStatus : SafeProjectorOperationRf3Protocol.UpdatedStatus, JsonDefaults.Options)));
        if (healthy)
        {
            fields.Add(new(SafeProjectorOperationRf3Protocol.LiteralPath, PatchKind.Set,
                JsonSerializer.Serialize(SafeProjectorOperationRf3Protocol.Canary, JsonDefaults.Options)));
        }
        var request = new CommandRequest(Guid.NewGuid(), history.State.Partition,
            [new PatchDocument(history.State.Collection, SafeProjectorOperationRf3Protocol.First, fields.ToImmutable(), history.Revision)]);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(
            new(SafeProjectorOperationRf3Protocol.PlacementReadVersion, history.State.Partition), token));
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
        await ServerFailureObserver.ObserveAsync(async () => await SafeProjectorOperationRf3Receipts.PatchAsync(
            await McpCallerAssertions.SdkSuccessAsync(actual), history.State, request.CommandId, history.Revision, placement), failures);
        await ServerFailureObserver.ObserveAsync(async () => await SafeProjectorOperationRf3Images.CaptureAsync(root, history, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await SafeProjectorOperationRf3Replay.PatchAsync(root, reader, history, healthy,
            healthy ? SafeProjectorOperationRf3Protocol.FullWriteEpoch : SafeProjectorOperationRf3Protocol.WithdrawnUseEpoch, token);
    }
}
