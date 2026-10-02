using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeyLoad.AppHost.Features.ClusterReplication;

internal static class ClusterProfileStore
{
    private const string ProfileName = "local-profile.json";
    private const string StagingSuffix = ".tmp-";
    private const string GuidFormat = "N";
    private const string AdminPrefix = "root.";
    private const string InvalidProfile = "The private cluster profile is missing required identity or credential fields, or is invalid.";
    private const string UnsafeProfilePath = "The private cluster profile directory and files cannot use reparse paths.";
    private const int MaximumProfileBytes = 8192;
    private const int MaximumDepth = 8;
    private const int SecretBytes = 32;
    private const int Base64Characters = 44;
    private const int MinimumAdminCharacters = 32;
    private const int MaximumAdminCharacters = 256;
    private const int RequiredFields = 4;
    private static readonly JsonSerializerOptions Json = new()
    {
        MaxDepth = MaximumDepth,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    /// <summary>Opens or atomically creates the bounded private Pascal-case local profile without emitting credentials.</summary>
    internal static LocalProfile Open(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var root = Path.GetFullPath(dataRoot);
        PrepareDirectory(root);
        var path = Path.Combine(root, ProfileName);
        RejectLinks(path);
        if (File.Exists(path))
        { return Read(path); }
        var profile = new LocalProfile(Guid.NewGuid(), Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes)),
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(SecretBytes)),
            AdminPrefix + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(SecretBytes)));
        Validate(profile);
        var staged = path + StagingSuffix + Guid.NewGuid().ToString(GuidFormat);
        try
        {
            using (var output = new FileStream(staged, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                MaximumProfileBytes, FileOptions.WriteThrough))
            {
                PrivateFile(staged);
                output.Write(JsonSerializer.SerializeToUtf8Bytes(profile, Json));
                output.Flush(true);
            }
            File.Move(staged, path);
            return profile;
        }
        finally { File.Delete(staged); }
    }

    private static LocalProfile Read(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length is < 1 or > MaximumProfileBytes)
        { throw new InvalidOperationException(InvalidProfile); }
        PrivateFile(path);
        var bytes = new byte[checked((int)input.Length)];
        input.ReadExactly(bytes);
        try
        {
            RequireFields(bytes);
            var profile = JsonSerializer.Deserialize<LocalProfile>(bytes, Json)
                ?? throw new InvalidOperationException(InvalidProfile);
            Validate(profile);
            return profile;
        }
        catch (JsonException) { throw new InvalidOperationException(InvalidProfile); }
    }

    private static void RequireFields(byte[] bytes)
    {
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = MaximumDepth });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        { throw new InvalidOperationException(InvalidProfile); }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in document.RootElement.EnumerateObject())
        {
            if (field.Name is not (nameof(LocalProfile.Incarnation) or nameof(LocalProfile.SigningKey)
                or nameof(LocalProfile.PeerSecret) or nameof(LocalProfile.AdminKey)) || !seen.Add(field.Name))
            { throw new InvalidOperationException(InvalidProfile); }
        }
        if (seen.Count != RequiredFields)
        { throw new InvalidOperationException(InvalidProfile); }
    }

    internal static void Validate(LocalProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (profile.Incarnation == Guid.Empty || !ValidSecret(profile.SigningKey) || !ValidSecret(profile.PeerSecret)
            || profile.AdminKey is null || profile.AdminKey.Length is < MinimumAdminCharacters or > MaximumAdminCharacters
            || !profile.AdminKey.StartsWith(AdminPrefix, StringComparison.Ordinal))
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
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute); }
    }

    private static void PrivateFile(string path)
    {
        RejectLinks(path);
        if (!OperatingSystem.IsWindows())
        { File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
    }

    private static void RejectLinks(string path)
    {
        for (var current = path; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
        {
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                { throw new InvalidOperationException(UnsafeProfilePath); }
            }
            catch (FileNotFoundException) { }
            catch (DirectoryNotFoundException) { }
        }
    }
}
