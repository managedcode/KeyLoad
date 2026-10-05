using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aspire.Hosting;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TwoRf3MembershipFingerprintAssertions
{
    private const string Domain = "keyload.orleans.membership.active.v1";
    private const int MaximumHealthBytes = 512;

    internal static string Fingerprint(IEnumerable<string> addresses)
    {
        var ordered = addresses.OrderBy(value => value, StringComparer.Ordinal).ToArray();
        if (ordered.Length != TwoRf3MembershipProtocol.NodeCount
            || ordered.Distinct(StringComparer.Ordinal).Count() != TwoRf3MembershipProtocol.NodeCount
            || ordered.Any(value => Encoding.UTF8.GetByteCount(value) is 0 or > 256))
        { throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState); }
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Encoding.ASCII.GetBytes(Domain));
            writer.Write(checked((uint)ordered.Length));
            foreach (var address in ordered)
            {
                var bytes = Encoding.UTF8.GetBytes(address);
                writer.Write(checked((uint)bytes.Length));
                writer.Write(bytes);
            }
        }
        return Convert.ToHexStringLower(SHA256.HashData(stream.ToArray()));
    }

    internal static async Task VerifyHealthAsync(DistributedApplication app, string fingerprint,
        CancellationToken token)
    {
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        { await VerifyNodeAsync(app, node, fingerprint, token).ConfigureAwait(false); }
    }

    private static async Task VerifyNodeAsync(DistributedApplication app, string node, string expected,
        CancellationToken token)
    {
        using var http = McpCallerHttp.Create(app, node);
        using var response = await http.GetAsync(new Uri(http.BaseAddress!, TwoRf3MembershipProtocol.HealthMembership),
            HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
        await Assert.That((int)response.StatusCode).IsEqualTo(200);
        var bytes = await ReadBoundedHealthAsync(response, token).ConfigureAwait(false);
        using var json = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 2 });
        var root = json.RootElement;
        await Assert.That(root.ValueKind).IsEqualTo(JsonValueKind.Object);
        var names = root.EnumerateObject().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        await Assert.That(names.SequenceEqual(new[] { "activeFingerprint", "activeSilos", "membershipRows", "version" },
            StringComparer.Ordinal)).IsTrue();
        await Assert.That(root.GetProperty("version").GetInt32()).IsEqualTo(1);
        await Assert.That(root.GetProperty("activeSilos").GetInt32()).IsEqualTo(6);
        var rows = root.GetProperty("membershipRows").GetInt32();
        await Assert.That(rows is >= 6 and <= 48).IsTrue();
        var actual = root.GetProperty("activeFingerprint").GetString();
        await Assert.That(IsLowerHex(actual)).IsTrue();
        await Assert.That(actual).IsEqualTo(expected);
    }

    private static async Task<byte[]> ReadBoundedHealthAsync(HttpResponseMessage response, CancellationToken token)
    {
        if (response.Content.Headers.ContentLength is > MaximumHealthBytes)
        { throw new InvalidDataException(TwoRf3MembershipProtocol.MissingState); }
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var buffer = new byte[MaximumHealthBytes + 1];
        var count = 0;
        while (count < buffer.Length)
        {
            token.ThrowIfCancellationRequested();
            var read = await stream.ReadAsync(buffer.AsMemory(count), token).ConfigureAwait(false);
            if (read == 0) { break; }
            count += read;
        }
        if (count > MaximumHealthBytes || response.Content.Headers.ContentLength is { } length && length != count)
        { throw new InvalidDataException(TwoRf3MembershipProtocol.MissingState); }
        return buffer.AsSpan(0, count).ToArray();
    }

    private static bool IsLowerHex(string? value)
    {
        if (value is null || value.Length != 64) { return false; }
        foreach (var character in value)
        { if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) { return false; } }
        return true;
    }
}
