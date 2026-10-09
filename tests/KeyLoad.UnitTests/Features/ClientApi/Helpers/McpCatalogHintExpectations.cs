using KeyLoad.Orleans;
using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.ClientApi;

/// <summary>Independent effects for the complete frozen public operation inventory and fresh official tools.</summary>
internal static class McpCatalogHintExpectations
{
    internal static async Task RequireAsync(McpOperationDescriptor actual, GrainReadKind? read, OperationKind? command)
    {
        var readOnly = read.HasValue && read != GrainReadKind.Backup;
        var idempotent = read != GrainReadKind.Backup && (read.HasValue || command.HasValue);
        var destructive = !read.HasValue;
        if (command is OperationKind.MaintainAnnIndex or OperationKind.MaintainTextIndex or OperationKind.ReceiveAcrossLanes)
        { idempotent = false; }
        if (command is OperationKind.ConfigureResource or OperationKind.BindAtomicPartitionPlacement
            or OperationKind.BeginBlobUpload or OperationKind.WriteBlobPart or OperationKind.AbortBlobUpload)
        { destructive = false; }
        await Assert.That(actual.ReadOnly).IsEqualTo(readOnly);
        await Assert.That(actual.Idempotent).IsEqualTo(idempotent);
        await Assert.That(actual.Destructive).IsEqualTo(destructive);
        var annotations = actual.CreateTool().Annotations!;
        await Assert.That(annotations.ReadOnlyHint).IsEqualTo(readOnly);
        await Assert.That(annotations.IdempotentHint).IsEqualTo(idempotent);
        await Assert.That(annotations.DestructiveHint).IsEqualTo(destructive);
    }
}
