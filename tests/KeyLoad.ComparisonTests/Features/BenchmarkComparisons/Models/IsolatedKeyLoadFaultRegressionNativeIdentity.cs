using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Safe selected native fields; the owned mount path is hashed and never exported.</summary>
internal sealed record IsolatedKeyLoadFaultRegressionNativeIdentity(string Id, string Image, string ImageId,
    string State, DateTimeOffset StartedAt, string MountSha256)
{
    internal const string InspectFormat = "{\"id\":{{json .Id}},\"image\":{{json .Config.Image}},\"imageId\":{{json .Image}},"
        + "\"state\":{{json .State.Status}},\"startedAt\":{{json .State.StartedAt}},\"mounts\":{{json .Mounts}}}";
    private const string Digest = "sha256:";
    private const string DestinationField = "Destination";
    private const string RwField = "RW";
    private const string SourceField = "Source";
    private const string TypeField = "Type";
    private const string IdField = "id";
    private const string ImageField = "image";
    private const string ImageIdField = "imageId";
    private const string MountsField = "mounts";
    private const string StartedAtField = "startedAt";
    private const string StateField = "state";

    internal static IsolatedKeyLoadFaultRegressionNativeIdentity Parse(string text, string image, string source)
    {
        using var json = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 8 });
        var value = json.RootElement;
        var id = value.GetProperty(IdField).GetString()!;
        var configured = value.GetProperty(ImageField).GetString()!;
        var imageId = value.GetProperty(ImageIdField).GetString()!;
        var state = value.GetProperty(StateField).GetString()!;
        var started = value.GetProperty(StartedAtField).GetString();
        IsolatedKeyLoadFaultRegressionProtocol.Require(Hex(id, 64) && configured == image
            && imageId.StartsWith(Digest, StringComparison.Ordinal) && Hex(imageId[Digest.Length..], 64)
            && state is IsolatedKeyLoadFaultRegressionProtocol.Running or IsolatedKeyLoadFaultRegressionProtocol.Exited
                or "created" or "restarting" or "paused" or "removing" or "dead");
        IsolatedKeyLoadFaultRegressionProtocol.Require(DateTimeOffset.TryParse(started, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var timestamp) && timestamp.Offset == TimeSpan.Zero);
        var mounts = value.GetProperty(MountsField).EnumerateArray()
            .Where(item => item.GetProperty(DestinationField).GetString() == IsolatedKeyLoadFaultRegressionProtocol.DataMount).ToArray();
        IsolatedKeyLoadFaultRegressionProtocol.Require(mounts.Length == 1);
        var mount = mounts[0];
        IsolatedKeyLoadFaultRegressionProtocol.Require(mount.GetProperty(TypeField).GetString() == "bind"
            && mount.GetProperty(SourceField).GetString() == source && mount.GetProperty(RwField).GetBoolean());
        var authority = "bind\n" + source + "\n" + IsolatedKeyLoadFaultRegressionProtocol.DataMount + "\nread-write";
        return new(id, configured, imageId, state, timestamp, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(authority))));
    }

    internal static bool Hex(string value, int length)
        => value.Length == length && value.All(static item => item is >= '0' and <= '9' or >= 'a' and <= 'f');
}
