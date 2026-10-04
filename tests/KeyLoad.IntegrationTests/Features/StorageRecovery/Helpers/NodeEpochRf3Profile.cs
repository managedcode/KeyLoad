using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal sealed record NodeEpochRf3Profile(Guid Incarnation, string SigningKey, string PeerSecret, string AdminKey)
{
    private static readonly JsonSerializerOptions Options = new()
    {
        MaxDepth = 4,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true
    };

    public override string ToString() => "NodeEpochRf3Profile(<private>)";

    internal static async Task<(NodeEpochRf3Profile Profile, byte[] Bytes)> CreatePriorAsync(
        string priorRoot, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(priorRoot);
        MakePrivateDirectory(priorRoot);
        var profile = new NodeEpochRf3Profile(Guid.NewGuid(), Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            "root." + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)));
        Validate(profile);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(profile);
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
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 4 });
        var names = json.RootElement.EnumerateObject().Select(property => property.Name).ToArray();
        if (json.RootElement.ValueKind != JsonValueKind.Object || names.Length != 4
            || names.Distinct(StringComparer.Ordinal).Count() != 4
            || !names.ToHashSet(StringComparer.Ordinal).SetEquals(
                [nameof(Incarnation), nameof(SigningKey), nameof(PeerSecret), nameof(AdminKey)]))
        { throw new InvalidDataException("The prior local profile does not have its exact private schema."); }
        var profile = JsonSerializer.Deserialize<NodeEpochRf3Profile>(bytes, Options)
            ?? throw new InvalidDataException("The prior local profile is empty.");
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
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Incarnation == Guid.Empty || profile.SigningKey is not { Length: 44 }
            || profile.PeerSecret is not { Length: 44 } || profile.AdminKey is not { Length: >= 32 and <= 256 }
            || !profile.AdminKey.StartsWith("root.", StringComparison.Ordinal))
        { throw new InvalidDataException("The local profile identity is invalid."); }
        Span<byte> decoded = stackalloc byte[32];
        if (!Convert.TryFromBase64String(profile.SigningKey, decoded, out var signingBytes) || signingBytes != 32
            || !Convert.TryFromBase64String(profile.PeerSecret, decoded, out var peerBytes) || peerBytes != 32)
        { throw new InvalidDataException("The local profile secrets are invalid."); }
    }

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
