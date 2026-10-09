using System.Text.Json;
using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>AC-MCP-001/006/007: actual immutable catalog matches the frozen public contract.</summary>
internal sealed class McpCatalogTests
{
    private const string TypeKey = "type";
    private const string ObjectType = "object";
    private const string AdditionalPropertiesKey = "additionalProperties";
    private const string ChangedName = "caller-mutated-name";

    /// <summary>Checks every frozen tool identity, route, schema root and exclusive operation capability.</summary>
    [Test]
    public async Task AcMcp001EveryFrozenOperationHasItsExactRouteAndOneCapability()
    {
        await Assert.That(McpOperationCatalog.Entries.Length).IsEqualTo(McpCatalogExpectations.Count);
        await Assert.That(McpOperationCatalog.Entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(McpCatalogExpectations.Count);
        await Assert.That(McpCatalogExpectations.Entries.Length).IsEqualTo(McpCatalogExpectations.Count);
        await Assert.That(McpCatalogExpectations.Entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count())
            .IsEqualTo(McpCatalogExpectations.Count);
        foreach (var expected in McpCatalogExpectations.Entries)
        {
            await Assert.That(McpOperationCatalog.TryGetTool(expected.Name, out var actual)).IsTrue();
            await Assert.That(actual!.Route).IsEqualTo(expected.Route);
            await Assert.That(actual.ReadKind).IsEqualTo(expected.ReadKind);
            await Assert.That(actual.CommandKind).IsEqualTo(expected.CommandKind);
            if (expected.Name == SqlOperationProtocol.ToolName)
            {
                await Assert.That(actual.IsAdapter).IsTrue();
                await Assert.That(actual.ReadOnly || actual.Idempotent).IsFalse();
                await Assert.That(actual.Destructive).IsTrue();
                await Assert.That(McpOperationCatalog.TryGet(expected.Name, out _)).IsFalse();
            }
            else
            {
                await Assert.That(actual.IsAdapter).IsFalse();
                await Assert.That(actual.ReadKind.HasValue ^ actual.CommandKind.HasValue).IsTrue();
            }
            await McpCatalogHintExpectations.RequireAsync(actual, expected.ReadKind, expected.CommandKind);
            await Assert.That(string.IsNullOrWhiteSpace(actual.Description)).IsFalse();
            await Assert.That(actual.InputSchema.GetProperty(TypeKey).GetString()).IsEqualTo(ObjectType);
            await Assert.That(actual.InputSchema.GetProperty(AdditionalPropertiesKey).ValueKind).IsEqualTo(JsonValueKind.False);
            await Assert.That(actual.OutputSchema.GetProperty(TypeKey).GetString()).IsEqualTo(ObjectType);
        }
        await McpMovementCatalogSchema.RequireAsync(Find(McpMovementCatalogProtocol.Name));
        await McpMovementCatalogDecode.RequireAsync(Find(McpMovementCatalogProtocol.Name));
    }

    /// <summary>Rejects case-changed names and prevents internal authentication or membership discovery.</summary>
    [Test]
    public async Task AcMcp001LookupIsOrdinalAndInternalCapabilitiesAreAbsent()
    {
        await Assert.That(McpOperationCatalog.TryGet(McpCatalogExpectations.DocumentsGet.ToUpperInvariant(), out _)).IsFalse();
        await Assert.That(McpOperationCatalog.Entries.Any(entry => entry.ReadKind == GrainReadKind.Authenticate)).IsFalse();
        await Assert.That(McpOperationCatalog.Entries.Any(entry => entry.CommandKind == OperationKind.Membership)).IsFalse();
    }

    /// <summary>Distinguishes physical backup retries from deduplicated destructive delivery completion.</summary>
    [Test]
    public async Task AcMcp006BackupHasExplicitPhysicalSideEffectsAndRetryHints()
    {
        var backup = Find(McpCatalogExpectations.AdminBackup);
        var delivery = Find(McpCatalogExpectations.MessagesComplete);
        await Assert.That(backup.ReadOnly).IsFalse();
        await Assert.That(backup.Idempotent).IsFalse();
        await Assert.That(backup.Destructive).IsFalse();
        await Assert.That(delivery.ReadOnly).IsFalse();
        await Assert.That(delivery.Idempotent).IsTrue();
        await Assert.That(delivery.Destructive).IsTrue();
    }

    /// <summary>AC-MCP-006: additive resource creation is not advertised as a destructive update.</summary>
    [Test]
    public async Task AcMcp006AdditiveResourceConfigurationIsNotDestructive()
    {
        var resource = Find(McpCatalogExpectations.ResourcesConfigure);
        await Assert.That(resource.ReadOnly).IsFalse();
        await Assert.That(resource.Idempotent).IsTrue();
        await Assert.That(resource.Destructive).IsFalse();
    }

    /// <summary>Caller changes to one native Tool and its annotations cannot mutate catalog metadata.</summary>
    [Test]
    public async Task AcMcp007DiscoveryCreatesFreshNativeToolsAndAnnotations()
    {
        var descriptor = Find(McpCatalogExpectations.DocumentsGet);
        var first = descriptor.CreateTool();
        var second = descriptor.CreateTool();
        first.Name = ChangedName;
        first.Annotations!.ReadOnlyHint = false;
        await Assert.That(second.Name).IsEqualTo(McpCatalogExpectations.DocumentsGet);
        await Assert.That(second.Annotations!.ReadOnlyHint).IsTrue();
        await Assert.That(second.InputSchema.GetRawText()).IsEqualTo(descriptor.InputSchema.GetRawText());
        await Assert.That(second.OutputSchema!.Value.GetRawText()).IsEqualTo(descriptor.OutputSchema.GetRawText());
        await Assert.That(descriptor.Name).IsEqualTo(McpCatalogExpectations.DocumentsGet);
        await Assert.That(descriptor.ReadOnly).IsTrue();
    }

    private static McpOperationDescriptor Find(string name)
    {
        if (!McpOperationCatalog.TryGet(name, out var descriptor))
        {
            throw new InvalidOperationException(name);
        }
        return descriptor!;
    }

    [Test]
    public async Task AcMcp006MessagingInspectionAndGraphSearchAdvertiseReadOnlyNativeTools()
    {
        string[] names = [McpCatalogExpectations.QueueTransferInspect, McpCatalogExpectations.QueueTransferReceipt,
            McpCatalogExpectations.ScheduleInspect, McpCatalogExpectations.SagaInspect,
            McpCatalogExpectations.SearchGraph, McpCatalogExpectations.QuerySearch];
        foreach (var name in names)
        {
            var operation = Find(name);
            await Assert.That(operation.ReadOnly).IsTrue();
            await Assert.That(operation.Idempotent).IsTrue();
            await Assert.That(operation.Destructive).IsFalse();
            var tool = operation.CreateTool();
            await Assert.That(tool.Annotations!.ReadOnlyHint).IsTrue();
            await Assert.That(tool.Annotations.IdempotentHint).IsTrue();
            await Assert.That(tool.Annotations.DestructiveHint).IsFalse();
        }
    }
}
