using System.Security.Cryptography;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Server;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class PartitionMovementBlobWireBorrow : IControlledBlobWireBorrowObserver, IDisposable
{
    private const int FirstVoter = 0;
    private readonly Lock gate = new();
    private readonly HashSet<(ControlledBlobReadPurpose Purpose, OperationKind? Kind)> observed = [];
    private PartitionMovementLateNativeHttp? transport;
    private PartitionMovementLateNativeOwners? owners;

    internal void Attach(PartitionMovementLateNativeOwners value)
    {
        if (owners is not null)
        { throw new InvalidOperationException("The native wire owner was already attached."); }
        transport = PartitionMovementLateNativeHttp.Create(new(value.Settings.Origin(PartitionMovementLateNativeSettings.GroupSize)));
        owners = value;
    }

    public async ValueTask ObserveAsync(RemoteControlledBlobCall call, ReadOnlyMemory<byte> envelope,
        string signature, CancellationToken cancellationToken)
    {
        var identity = (call.Request.Frame.Purpose, call.Request.Frame.Original?.Kind);
        await Assert.That(Supported(identity)).IsTrue();
        lock (gate)
        { if (!observed.Add(identity)) { return; } }
        var actual = owners ?? throw new InvalidOperationException("The native wire owners are not attached.");
        var digest = SHA256.HashData(envelope.Span);
        foreach (var fault in PartitionMovementBlobWireFaults.Create(call))
        {
            var before = actual.Nodes.Select(PartitionMovementBlobWireNativeCut.Read).ToArray();
            var bytes = RemoteDocumentWire.Encode(fault);
            await Assert.That(bytes.AsSpan().SequenceEqual(envelope.Span)).IsFalse();
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(() => PartitionMovementBlobWireRejectedRequest.RequireAsync(
                (transport ?? throw new InvalidOperationException("The actual native HTTP owner is absent.")).Client,
                call, bytes, signature, cancellationToken), failures);
            var after = actual.Nodes.Select(PartitionMovementBlobWireNativeCut.Read).ToArray();
            for (var index = FirstVoter; index < actual.Nodes.Count; index++)
            {
                var selected = index;
                await ServerFailureObserver.ObserveAsync(() => PartitionMovementBlobWireNativeCut.RequireAsync(
                    actual.Nodes[selected], before[selected], after[selected]), failures);
            }
            ServerFailureObserver.ThrowIfAny(failures);
        }
        await Assert.That(Convert.ToHexString(SHA256.HashData(envelope.Span))).IsEqualTo(Convert.ToHexString(digest));
    }

    internal async Task RequireCompleteAsync()
    {
        foreach (var kind in new[] { OperationKind.BeginBlobUpload, OperationKind.WriteBlobPart,
            OperationKind.CompleteBlobUpload, OperationKind.AbortBlobUpload, OperationKind.DeleteBlob, OperationKind.ReclaimBlob })
        { await Assert.That(observed.Contains((ControlledBlobReadPurpose.Outcome, kind))).IsTrue(); }
        foreach (var purpose in new[] { ControlledBlobReadPurpose.Metadata, ControlledBlobReadPurpose.UploadInfo,
            ControlledBlobReadPurpose.Range, ControlledBlobReadPurpose.List })
        { await Assert.That(observed.Contains((purpose, null))).IsTrue(); }
    }

    private static bool Supported((ControlledBlobReadPurpose Purpose, OperationKind? Kind) identity)
        => identity.Purpose == ControlledBlobReadPurpose.Outcome
            ? identity.Kind is OperationKind.BeginBlobUpload or OperationKind.WriteBlobPart or OperationKind.CompleteBlobUpload
                or OperationKind.AbortBlobUpload or OperationKind.DeleteBlob or OperationKind.ReclaimBlob
            : identity.Kind is null && identity.Purpose is ControlledBlobReadPurpose.Metadata
                or ControlledBlobReadPurpose.UploadInfo or ControlledBlobReadPurpose.Range or ControlledBlobReadPurpose.List;

    public void Dispose() => transport?.Dispose();
}
