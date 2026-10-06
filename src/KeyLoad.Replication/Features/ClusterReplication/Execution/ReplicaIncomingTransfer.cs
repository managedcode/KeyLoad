using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Replication;

internal sealed class ReplicaIncomingTransfer
{
    private const int BeforeFirstLogPosition = 0;
    private const int BeforeFirstTransferByte = 0;

    private readonly ReplicaSnapshotFiles files;
    private readonly Action<ReplicaCrashBoundary>? faultObserver;
    private readonly ReplicaConfiguration configuration;

    internal ReplicaIncomingTransfer(ReplicaSnapshotFiles files, IOptions<ReplicaConfiguration> configurationOptions,
        Action<ReplicaCrashBoundary>? faultObserver = null)
    {
        ArgumentNullException.ThrowIfNull(configurationOptions);
        configuration = configurationOptions.Value;
        configuration.Validate();
        this.files = files;
        this.faultObserver = faultObserver;
    }
    internal ReplicaSnapshot? Descriptor => files.ReadManifest();

    internal void Recover(IAtomicStore canonical, ReplicaSnapshot? published, Func<Guid, ReplicaSnapshot> complete)
    {
        ReplicaSnapshot? pending;
        try
        { pending = Descriptor; }
        catch (KeyLoadException error) when (error.Code is ErrorCode.Corruption or ErrorCode.Validation)
        {
            files.ClearIncoming();
            return;
        }
        if (pending is null)
        { return; }
        if (pending.Index < ReplicaPersistence.AppliedPosition(canonical) || pending.Index < (published?.Index ?? BeforeFirstLogPosition))
        {
            files.DiscardIncoming(pending, published);
            return;
        }
        try
        {
            if (Length(pending) < pending.Length)
            { return; }
            ReplicaPersistence.VerifyImage(canonical, files.TransferImage(pending), pending);
        }
        catch (KeyLoadException error) when (error.Code is ErrorCode.Corruption or ErrorCode.FormatUnsupported or ErrorCode.Validation)
        {
            faultObserver?.Invoke(ReplicaCrashBoundary.SnapshotRejected);
            files.DiscardIncoming(pending, published);
            return;
        }
        _ = complete(pending.TransferId);
    }

    internal long Begin(ReplicaSnapshot snapshot)
    {
        ReplicaPersistence.ValidateSnapshot(snapshot, configuration);
        var pending = Descriptor;
        if (pending is not null)
        {
            if (pending != snapshot)
            { throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.SnapshotUnavailable); }
            var path = files.TransferImage(snapshot);
            if (File.Exists(path))
            {
                using var existingImage = files.OpenPrivate(path, FileMode.Open);
                existingImage.Flush(true);
            }
            return Length(snapshot);
        }
        if (File.Exists(files.ImagePath(snapshot)))
        {
            throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.SnapshotUnavailable);
        }
        // An image without a durable descriptor was never acknowledged; restarting can safely discard it.
        File.Delete(files.IncomingPath);
        files.WriteManifest(snapshot);
        using var image = files.OpenPrivate(files.IncomingPath, FileMode.CreateNew);
        image.Flush(true);
        return BeforeFirstTransferByte;
    }

    internal long Length(ReplicaSnapshot snapshot)
    {
        var path = files.TransferImage(snapshot);
        if (!File.Exists(path))
        { return BeforeFirstTransferByte; }
        var length = new FileInfo(path).Length;
        if (length > snapshot.Length)
        { throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidSnapshot); }
        return length;
    }

    internal long Append(Guid transferId, long offset, ReadOnlySpan<byte> bytes)
    {
        var snapshot = Required(transferId);
        if (offset < BeforeFirstTransferByte || bytes.IsEmpty || bytes.Length > configuration.SnapshotChunkBytes
            || offset > snapshot.Length || bytes.Length > snapshot.Length - offset)
        {
            throw Errors.Fail(ErrorCode.Validation, ReplicaProtocol.InvalidSnapshot);
        }
        using var image = files.OpenPrivate(files.IncomingPath, FileMode.OpenOrCreate);
        if (offset > image.Length)
        { throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.SnapshotUnavailable); }
        if (offset < image.Length)
        {
            VerifyReplay(image, offset, bytes);
            image.Flush(true);
            return image.Length;
        }
        image.Position = offset;
        image.Write(bytes);
        image.Flush(true);
        return image.Length;
    }

    private static void VerifyReplay(FileStream image, long offset, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > image.Length - offset)
        {
            throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.SnapshotUnavailable);
        }
        var existing = new byte[bytes.Length];
        image.Position = offset;
        image.ReadExactly(existing);
        if (!existing.AsSpan().SequenceEqual(bytes))
        {
            throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.SnapshotUnavailable);
        }
    }

    internal ReplicaSnapshot Required(Guid transferId)
    {
        var snapshot = Descriptor;
        if (snapshot is null || snapshot.TransferId != transferId)
        {
            throw Errors.Fail(ErrorCode.NotFound, ReplicaProtocol.SnapshotUnavailable);
        }
        return snapshot;
    }
}
