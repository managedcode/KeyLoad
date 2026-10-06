using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeyLoad.AppHost.Features.ClusterReplication;
using Microsoft.Extensions.Options;

internal static class ClusterProfileStore
{
    internal const string ProfileName = "local-profile.json";
    internal const string LegacyBackupName = "local-profile.v1.json.bak";
    private const string StagingSuffix = ".tmp-";
    private const string GuidFormat = "N";
    private const string AdminPrefix = "root.";
    internal const string InvalidProfile = "The private cluster profile is missing required identity or credential fields, or is invalid.";
    private const string UnsafeProfilePath = "The private cluster profile directory and files cannot use reparse paths.";
    internal const int CurrentVersion = 2;
    private const int SecretBytes = 32;
    private const int Base64Characters = 44;
    private const int MinimumAdminCharacters = 32;
    private const int MaximumAdminCharacters = 256;
    private const int RequiredFields = 6;
    private static readonly string[] CurrentFields =
    [
        nameof(LocalProfile.Version), nameof(LocalProfile.PhysicalShardId), nameof(LocalProfile.Incarnation),
        nameof(LocalProfile.SigningKey), nameof(LocalProfile.PeerSecret), nameof(LocalProfile.AdminKey)
    ];
    internal static JsonSerializerOptions CreateJson(IOptions<ClusterProfileExecutionOptions> executionOptions) => new()
    {
        MaxDepth = executionOptions.Value.MaximumJsonDepth,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>Opens or atomically creates the bounded private V2 profile without emitting credentials.</summary>
    internal static LocalProfile Open(string dataRoot, IOptions<ClusterProfileExecutionOptions> executionOptions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var root = ClusterProfileInputBounds.Root(dataRoot, executionOptions);
        PrepareDirectory(root);
        var path = Path.Combine(root, ProfileName);
        RejectLinks(path);
        if (File.Exists(path))
        { return Read(path, executionOptions); }
        var profile = new LocalProfile(CurrentVersion, Guid.NewGuid(), Guid.NewGuid(),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes)),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes)),
            AdminPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes)));
        Validate(profile);
        var staged = StagingPath(path);
        try
        {
            WriteStage(staged, JsonSerializer.SerializeToUtf8Bytes(profile, CreateJson(executionOptions)), ClusterProfilePermissions.NewProfileMode, executionOptions);
            File.Move(staged, path);
            return profile;
        }
        finally { DeleteStage(staged); }
    }

    /// <summary>Explicit offline conversion of one strict four-field legacy profile with a verified backup.</summary>
    internal static LocalProfile UpgradeLegacyOffline(string dataRoot, IOptions<ClusterProfileExecutionOptions> executionOptions)
        => ClusterProfileOfflineUpgrade.Run(dataRoot, executionOptions);

    private static LocalProfile Read(string path, IOptions<ClusterProfileExecutionOptions> executionOptions)
        => DeserializeCurrent(ReadBoundedBytes(path, executionOptions), executionOptions);

    internal static LocalProfile DeserializeCurrent(byte[] bytes, IOptions<ClusterProfileExecutionOptions> executionOptions)
    {
        try
        {
            RequireFields(bytes, CurrentFields, RequiredFields, executionOptions);
            var profile = JsonSerializer.Deserialize<LocalProfile>(bytes, CreateJson(executionOptions))
                ?? throw new InvalidOperationException(InvalidProfile);
            Validate(profile);
            return profile;
        }
        catch (JsonException) { throw new InvalidOperationException(InvalidProfile); }
    }

    internal static void RequireFields(byte[] bytes, string[] allowedFields, int requiredCount, IOptions<ClusterProfileExecutionOptions> executionOptions)
    {
        ClusterProfileInputBounds.Bytes(bytes, executionOptions);
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = executionOptions.Value.MaximumJsonDepth });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        { throw new InvalidOperationException(InvalidProfile); }
        var allowed = new HashSet<string>(allowedFields, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in document.RootElement.EnumerateObject())
        {
            if (!allowed.Contains(field.Name) || !seen.Add(field.Name))
            { throw new InvalidOperationException(InvalidProfile); }
        }
        if (seen.Count != requiredCount)
        { throw new InvalidOperationException(InvalidProfile); }
    }

    internal static void Validate(LocalProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Version != CurrentVersion || profile.PhysicalShardId == Guid.Empty)
        { throw new InvalidOperationException(InvalidProfile); }
        ValidateCredentials(profile.Incarnation, profile.SigningKey, profile.PeerSecret, profile.AdminKey);
    }

    internal static void ValidateCredentials(Guid incarnation, string? signingKey,
        string? peerSecret, string? adminKey)
    {
        if (incarnation == Guid.Empty || !ValidSecret(signingKey) || !ValidSecret(peerSecret)
            || adminKey is null || adminKey.Length is < MinimumAdminCharacters or > MaximumAdminCharacters
            || !adminKey.StartsWith(AdminPrefix, StringComparison.Ordinal))
        { throw new InvalidOperationException(InvalidProfile); }
    }

    private static bool ValidSecret(string? value)
    {
        Span<byte> decoded = stackalloc byte[SecretBytes];
        return value is { Length: Base64Characters } && Convert.TryFromBase64String(value, decoded, out var count)
            && count == SecretBytes;
    }

    internal static void PrepareDirectory(string directory)
    {
        RejectLinks(directory);
        Directory.CreateDirectory(directory);
        RejectLinks(directory);
        ClusterProfilePermissions.PreparePrivateDirectory(directory);
    }

    internal static byte[] ReadBoundedBytes(string path, IOptions<ClusterProfileExecutionOptions> executionOptions)
    {
        const int StructuralValue = 1;
        const int Step = 1;
        const int TotalInitialValue = 0;
        const int EmptyValue = 0;
        const int MissingValue = -1;
        const int StartValue = 0;

        RejectLinks(path);
        ClusterProfilePermissions.RequirePrivate(path);
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            executionOptions.Value.FileBufferBytes);
        var initialLength = input.Length;
        if (initialLength < StructuralValue || initialLength > executionOptions.Value.MaximumProfileBytes)
        { throw new InvalidOperationException(InvalidProfile); }
        var buffer = new byte[executionOptions.Value.MaximumProfileBytes + Step];
        var total = TotalInitialValue;
        while (total < buffer.Length)
        {
            var read = input.Read(buffer.AsSpan(total));
            if (read == EmptyValue)
            { break; }
            total += read;
        }
        if (total < StructuralValue || total > executionOptions.Value.MaximumProfileBytes || initialLength != total || input.Length != total
            || input.ReadByte() != MissingValue || input.Length != total)
        { throw new InvalidOperationException(InvalidProfile); }
        return buffer.AsSpan(StartValue, total).ToArray();
    }

    internal static void VerifyCopy(byte[] original, byte[] copy)
    {
        var originalHash = SHA256.HashData(original);
        var copyHash = SHA256.HashData(copy);
        if (original.Length != copy.Length || !CryptographicOperations.FixedTimeEquals(originalHash, copyHash))
        { throw new InvalidOperationException(InvalidProfile); }
    }

    internal static void WriteStage(string path, byte[] bytes, UnixFileMode? fileMode, IOptions<ClusterProfileExecutionOptions> executionOptions)
    {
        ClusterProfileInputBounds.Bytes(bytes, executionOptions);
        RejectLinks(path);
        using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            executionOptions.Value.FileBufferBytes, FileOptions.WriteThrough);
        ClusterProfilePermissions.ApplyFileMode(path, fileMode);
        output.Write(bytes);
        output.Flush(true);
    }

    internal static string StagingPath(string path) => path + StagingSuffix + Guid.NewGuid().ToString(GuidFormat);

    internal static void DeleteStage(string path)
    {
        if (File.Exists(path))
        { File.Delete(path); }
    }

    internal static void RejectLinks(string path)
    {
        const int EmptyValue = 0;

        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != EmptyValue)
                { throw new InvalidOperationException(UnsafeProfilePath); }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }

}
