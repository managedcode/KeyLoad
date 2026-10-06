using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace KeyLoad.Replication;

internal sealed class ReplicaSnapshotFiles
{
    private const int NoFileAttributeFlags = 0;

    private readonly ReplicaConfiguration configuration;
    private readonly ReplicaExecutionOptions execution;
    internal string DirectoryPath { get; }

    internal ReplicaSnapshotFiles(IOptions<ReplicaConfiguration> configurationOptions,
        IOptions<ReplicaExecutionOptions> executionOptions)
    {
        ArgumentNullException.ThrowIfNull(configurationOptions);
        ArgumentNullException.ThrowIfNull(executionOptions);
        execution = executionOptions.Value;
        execution.Validate();
        configuration = configurationOptions.Value;
        configuration.Validate();
        DirectoryPath = PrepareDirectory(configuration);
    }
    internal string ManifestPath => PathFor(ReplicaProtocol.IncomingManifest);
    internal string IncomingPath => PathFor(ReplicaProtocol.IncomingImage);
    internal string ImagePath(ReplicaSnapshot snapshot) => PathFor(snapshot.FileName);
    internal string TemporaryPath(ReplicaSnapshot snapshot) => PathFor(snapshot.FileName + ReplicaProtocol.TemporarySuffix);

    private static string PrepareDirectory(ReplicaConfiguration configuration)
    {
        configuration.Validate();
        var path = Path.GetFullPath(Path.Combine(configuration.Directory, ReplicaProtocol.SnapshotDirectory));
        RejectLinks(path);
        Directory.CreateDirectory(path);
        RejectLinks(path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        return path;
    }

    private string PathFor(string name)
    {
        var path = Path.Combine(DirectoryPath, name);
        RejectLinks(path);
        return path;
    }

    internal static void RejectLinks(string path)
    {
        var current = path;
        while (!string.IsNullOrEmpty(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != NoFileAttributeFlags)
                {
                    throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidSnapshot);
                }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
            current = Path.GetDirectoryName(current);
        }
    }

    internal FileStream OpenPrivate(string path, FileMode mode)
    {
        RejectLinks(path);
        var stream = new FileStream(path, mode, FileAccess.ReadWrite, FileShare.None,
            execution.FileBufferBytes, FileOptions.WriteThrough);
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            return stream;
        }
        catch (Exception)
        {
            stream.Dispose();
            throw;
        }
    }

    internal void WriteManifest(ReplicaSnapshot snapshot)
    {
        var bytes = ReplicaProtocolCodec.Serialize(snapshot);
        if (bytes.Length > execution.MaximumManifestBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaProtocol.InvalidSnapshot);
        }
        var staged = PathFor(ReplicaProtocol.IncomingManifest + ReplicaProtocol.TemporarySuffix);
        using (var file = OpenPrivate(staged, FileMode.Create))
        {
            file.Write(bytes);
            file.Flush(true);
        }
        File.Move(staged, ManifestPath, true);
    }

    internal ReplicaSnapshot? ReadManifest()
    {
        var path = ManifestPath;
        if (!File.Exists(path))
        { return null; }
        using var file = File.OpenRead(path);
        if (file.Length > execution.MaximumManifestBytes)
        {
            throw Errors.Fail(ErrorCode.Corruption, ReplicaProtocol.InvalidSnapshot);
        }
        var bytes = new byte[checked((int)file.Length)];
        file.ReadExactly(bytes);
        var snapshot = ReplicaPersistence.Decode<ReplicaSnapshot>(bytes, configuration.MaxAppendEntries);
        ReplicaPersistence.ValidateSnapshot(snapshot, configuration);
        return snapshot;
    }

    internal string TransferImage(ReplicaSnapshot snapshot)
    {
        var incoming = IncomingPath;
        return File.Exists(incoming) ? incoming : ImagePath(snapshot);
    }

    internal void PublishImage(ReplicaSnapshot snapshot)
    {
        var incoming = IncomingPath;
        var destination = ImagePath(snapshot);
        if (File.Exists(incoming))
        {
            if (File.Exists(destination))
            {
                throw Errors.Fail(ErrorCode.Conflict, ReplicaProtocol.SnapshotUnavailable);
            }
            File.Move(incoming, destination);
        }
    }

    internal void ClearIncoming()
    {
        // Only the two private, fixed transfer files are reclaimed. Immutable published images remain intact.
        var incoming = IncomingPath;
        var manifest = ManifestPath;
        var staged = PathFor(ReplicaProtocol.IncomingManifest + ReplicaProtocol.TemporarySuffix);
        File.Delete(incoming);
        File.Delete(manifest);
        File.Delete(staged);
    }

    internal void DiscardIncoming(ReplicaSnapshot snapshot, ReplicaSnapshot? published)
    {
        if (published?.FileName != snapshot.FileName)
        {
            File.Delete(ImagePath(snapshot));
        }
        ClearIncoming();
    }

    internal ReplicaSnapshot Describe(string path, Guid transferId, long index, long term)
    {
        using var image = File.OpenRead(path);
        if (image.Length > configuration.MaxSnapshotBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, ReplicaProtocol.InvalidSnapshot);
        }
        return new(transferId, configuration.Incarnation, index, term, image.Length,
            Convert.ToHexStringLower(SHA256.HashData(image)), transferId.ToString(ReplicaPersistence.GuidFormat) + ReplicaProtocol.SnapshotExtension);
    }
}
