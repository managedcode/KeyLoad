using System.Text.Json;
using KeyLoad.Storage.IO;

namespace KeyLoad.AppHost.Features.ClusterRouting;

/// <summary>Validates the exact private Owner record with bounded native JSON parsing.</summary>
internal static class RequestCqrsProbeOwnerReader
{
    internal const int MaximumOwnerBytes = 8_192;
    internal const UnixFileMode PrivateFileMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private const int MaximumJsonDepth = 1;
    private const int RequiredFieldCount = 4;
    private const int OwnerVersion = 1;
    private const string VersionField = "Version";
    private const string KindField = "Kind";
    private const string SessionField = "SessionId";
    private const string VoterField = "Voter";
    private const string OwnerKind = "Owner";
    private const string InvalidConfiguration = "RequestCqrsProbeConfigurationInvalid";

    internal static RequestCqrsProbeOwnerRecord ReadFile(string path)
    {
        const int StructuralValue = 0;
        const int MissingValue = -1;

        if (OperatingSystem.IsWindows())
        { throw new InvalidOperationException(InvalidConfiguration); }
        var identity = OfflineRegularFile.Inspect(path);
        if (identity.Length is <= StructuralValue or > MaximumOwnerBytes
            || File.GetUnixFileMode(path) != PrivateFileMode)
        { throw new InvalidOperationException(InvalidConfiguration); }
        using var input = OfflineRegularFile.OpenWithIdentity(path, identity, FileAccess.Read,
            FileShare.Read, bufferSize: 4_096);
        if (input.Length != identity.Length || File.GetUnixFileMode(path) != PrivateFileMode)
        { throw new InvalidOperationException(InvalidConfiguration); }
        var bytes = new byte[checked((int)identity.Length)];
        input.ReadExactly(bytes);
        if (input.ReadByte() != MissingValue || input.Length != identity.Length
            || OfflineRegularFile.Inspect(path) != identity)
        { throw new InvalidOperationException(InvalidConfiguration); }
        return Read(bytes);
    }

    internal static RequestCqrsProbeOwnerRecord Read(byte[] bytes)
    {
        const int StructuralValue = 0;
        const int VersionInitialValue = 0;

        if (bytes.Length is <= StructuralValue or > MaximumOwnerBytes)
        { throw new InvalidOperationException(InvalidConfiguration); }
        using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = MaximumJsonDepth });
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        { throw new InvalidOperationException(InvalidConfiguration); }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var version = VersionInitialValue;
        string? kind = null;
        string? session = null;
        string? ownerVoter = null;
        foreach (var field in document.RootElement.EnumerateObject())
        {
            if (!seen.Add(field.Name))
            { throw new InvalidOperationException(InvalidConfiguration); }
            ReadField(field, ref version, ref kind, ref session, ref ownerVoter);
        }
        if (seen.Count != RequiredFieldCount || version != OwnerVersion || kind != OwnerKind
            || session is null || ownerVoter is null)
        { throw new InvalidOperationException(InvalidConfiguration); }
        return new(version, kind, session, ownerVoter);
    }

    private static void ReadField(JsonProperty field, ref int version, ref string? kind,
        ref string? session, ref string? voter)
    {
        switch (field.Name)
        {
            case VersionField when field.Value.ValueKind == JsonValueKind.Number && field.Value.TryGetInt32(out var value):
                version = value;
                break;
            case KindField:
                kind = ReadString(field.Value);
                break;
            case SessionField:
                session = ReadString(field.Value);
                break;
            case VoterField:
                voter = ReadString(field.Value);
                break;
            default:
                throw new InvalidOperationException(InvalidConfiguration);
        }
    }

    private static string ReadString(JsonElement value)
    {
        const int StructuralValue = 0;

        if (value.ValueKind != JsonValueKind.String)
        { throw new InvalidOperationException(InvalidConfiguration); }
        return value.GetString() is { Length: > StructuralValue } text
            ? text : throw new InvalidOperationException(InvalidConfiguration);
    }
}
