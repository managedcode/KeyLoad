using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class ResourcePartitionInventoryPolicyRefusal
{
    private const string ChangedPolicyDetail = "The principal policy changed since this command was evaluated.";

    internal static async Task SdkAsync<T>(Result<T> actual)
    {
        await Assert.That(actual.IsFailed).IsTrue();
        await Assert.That(actual.Value).IsNull();
        await PartitionMovementPublicRefusalRf3Oracle.SdkAsync(actual.Problem,
            ErrorCode.PermissionDenied, ChangedPolicyDetail);
    }

    internal static async Task SdkAsync(Result<JsonElement> actual)
    {
        await Assert.That(actual.IsFailed).IsTrue();
        await Assert.That(actual.Value.ValueKind).IsEqualTo(JsonValueKind.Undefined);
        await PartitionMovementPublicRefusalRf3Oracle.SdkAsync(actual.Problem,
            ErrorCode.PermissionDenied, ChangedPolicyDetail);
    }

    internal static Task McpAsync(CallToolResult actual)
        => PartitionMovementPublicRefusalRf3Oracle.McpAsync(actual, ErrorCode.PermissionDenied,
            PartitionMovementPublicRefusalRf3Oracle.SafePermissionDetail);

    internal static async Task CreationAsync(RequestCqrsRf3Callers callers,
        ResourcePartitionInventoryRf3Creation creation, CancellationToken token)
    {
        await SdkAsync(await callers.Sdk.ConfigureResourceAsync(creation.CommandId, creation.Request, token));
        var arguments = McpOfficialClient.Arguments(creation.Request);
        arguments.Add(McpCallerProtocol.CommandId, creation.CommandId);
        await McpAsync(await callers.Mcp.Client.InvokeKeyLoadToolAsync(
            McpCallerTools.ResourcesConfigure, arguments, token));
    }

    internal static async Task UnchangedAsync(ResourceOwnerInventoryResultV1 before,
        ResourceOwnerInventoryResultV1 after)
    {
        // Each authenticated observation has its own inventory nonce and expiration.
        var sameScope = after.Control.Request with
        {
            InventoryId = before.Control.Request.InventoryId,
            ExpiresAt = before.Control.Request.ExpiresAt
        };
        await SqlRf3Protocol.EqualAsync(before.Control, after.Control with
        {
            Request = sameScope
        });
        await SqlRf3Protocol.EqualAsync(before.Entries, after.Entries);
        await Assert.That(after.OwnerCuts.Length).IsEqualTo(before.OwnerCuts.Length);
        foreach (var current in after.OwnerCuts)
        {
            var prior = before.OwnerCuts.Single(cut => cut.NodeId == current.NodeId);
            await Assert.That(current.StoreIncarnation).IsEqualTo(prior.StoreIncarnation);
            await Assert.That(current.StorePosition).IsEqualTo(prior.StorePosition);
            await Assert.That(current.AppliedIndex).IsEqualTo(prior.AppliedIndex);
            await Assert.That(current.EnrollmentRevision).IsEqualTo(prior.EnrollmentRevision);
            await Assert.That(current.CatalogRevision).IsEqualTo(prior.CatalogRevision);
            await Assert.That(current.OwnerDirectoryRevision).IsEqualTo(prior.OwnerDirectoryRevision);
            await Assert.That(current.PlacementDirectoryRevision).IsEqualTo(prior.PlacementDirectoryRevision);
        }
    }
}
