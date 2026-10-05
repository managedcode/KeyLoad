using System.Security.Cryptography;
using System.Text.Json;
using KeyLoad.AppHost.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal sealed record NodeEpochRf3Profile(LocalProfile Value)
{
    internal int Version => Value.Version;
    internal Guid PhysicalShardId => Value.PhysicalShardId;
    internal Guid Incarnation => Value.Incarnation;
    internal string SigningKey => Value.SigningKey;
    internal string PeerSecret => Value.PeerSecret;
    internal string AdminKey => Value.AdminKey;

    public override string ToString() => "NodeEpochRf3Profile(<private>)";

    internal static async Task<(NodeEpochRf3Profile Profile, byte[] Bytes)> CreatePriorAsync(
        string priorRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(priorRoot);
        MakePrivateDirectory(priorRoot);
        var profile = new NodeEpochRf3Profile(new LocalProfile(ClusterProfileStore.CurrentVersion,
            Guid.NewGuid(), Guid.NewGuid(), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            "root." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32))));
        Validate(profile);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(profile.Value, ClusterProfileStore.Json);
        var path = Path.Combine(priorRoot, NodeEpochRf3Protocol.ProfileFile);
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            NodeEpochRf3Protocol.MaximumProfileBytes, FileOptions.Asynchronous | FileOptions.WriteThrough);
        MakePrivate(path);
        await output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        NodeEpochRf3OfflineFiles.FlushToDisk(output);
        return (profile, bytes);
    }

    internal static async Task<(NodeEpochRf3Profile Profile, byte[] Bytes, string Sha256)> ReadAsync(
        string path, CancellationToken cancellationToken)
    {
        RequirePrivateRegularFile(path);
        var info = new FileInfo(path);
        if (info.Length is < 1 or > NodeEpochRf3Protocol.MaximumProfileBytes)
        { throw new InvalidDataException("The prior local profile exceeds its bounded size."); }
        var bytes = await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false);
        var profile = new NodeEpochRf3Profile(ClusterProfileStore.DeserializeCurrent(bytes));
        Validate(profile);
        return (profile, bytes, Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    internal async Task<string> CopyExactAsync(string destinationRoot, byte[] expectedBytes,
        CancellationToken cancellationToken)
    {
        Validate(this);
        ArgumentNullException.ThrowIfNull(expectedBytes);
        if (expectedBytes.Length is < 1 or > NodeEpochRf3Protocol.MaximumProfileBytes)
        { throw new InvalidDataException("The exact local profile copy exceeds its bounded size."); }
        if (!Directory.Exists(destinationRoot))
        { Directory.CreateDirectory(destinationRoot); }
        MakePrivateDirectory(destinationRoot);
        var path = Path.Combine(destinationRoot, NodeEpochRf3Protocol.ProfileFile);
        await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            NodeEpochRf3Protocol.MaximumProfileBytes, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            MakePrivate(path);
            await output.WriteAsync(expectedBytes, cancellationToken).ConfigureAwait(false);
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            NodeEpochRf3OfflineFiles.FlushToDisk(output);
        }
        var copied = await ReadBoundedAsync(path, cancellationToken).ConfigureAwait(false);
        if (!copied.AsSpan().SequenceEqual(expectedBytes))
        { throw new IOException("The private profile copy changed its source bytes."); }
        return Convert.ToHexStringLower(SHA256.HashData(copied));
    }

    private static void Validate(NodeEpochRf3Profile profile)
        => ClusterProfileStore.Validate(profile.Value);

    private static async Task<byte[]> ReadBoundedAsync(string path, CancellationToken cancellationToken)
    {
        await using var input = NodeEpochRf3OfflineFiles.OpenRead(path);
        if (input.Length is < 1 or > NodeEpochRf3Protocol.MaximumProfileBytes)
        { throw new InvalidDataException("The local profile exceeds its bounded size."); }
        var bytes = new byte[NodeEpochRf3Protocol.MaximumProfileBytes + 1];
        var total = 0;
        while (total < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = await input.ReadAsync(bytes.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            { break; }
            total += count;
        }
        if (total is < 1 or > NodeEpochRf3Protocol.MaximumProfileBytes
            || input.Length != total || await input.ReadAsync(bytes.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) != 0)
        { throw new InvalidDataException("The local profile changed or exceeds its bounded size."); }
        return bytes[..total];
    }

    private static void RequirePrivateRegularFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        NodeEpochRf3OfflineFiles.RequireRegular(path);
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            if ((mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
            { throw new InvalidDataException("The local profile permissions are not private."); }
        }
    }

    private static void MakePrivate(string path)
    {
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    }

    private static void MakePrivateDirectory(string path)
    {
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }
}
