using System.Text;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class SampleChunkNativeJobRf3Message
{
    private const string Returned = "Native chunk provider returned ";
    private const string Executing = "Native chunk provider executing ";
    private const string Job = " job ";
    private const string Metadata = " metadata ";
    private const int DigestCharacters = 64;
    private const int TerminatorCharacters = 1;
    private const char Terminator = '.';

    internal static SampleChunkNativeJobRf3Receipt? Read(string actual, Guid commandId, bool executing)
    {
        var prefix = (executing ? Executing : Returned) + commandId.ToString(SampleChunkPendingRf3Protocol.GuidFormat) + Job;
        var start = actual.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
        { return null; }
        if (Encoding.UTF8.GetByteCount(actual) > RequestCqrsProbeFixtureProtocol.MaximumRecordBytes)
        { throw Invalid(); }
        var body = actual.AsSpan(start + prefix.Length);
        var separator = body.LastIndexOf(Metadata.AsSpan(), StringComparison.Ordinal);
        if (separator <= 0)
        { throw Invalid(); }
        var digest = body[(separator + Metadata.Length)..];
        if (digest.Length != DigestCharacters + TerminatorCharacters || digest[^TerminatorCharacters] != Terminator
            || !IsDigest(digest[..DigestCharacters]))
        { throw Invalid(); }
        return new(commandId, body[..separator].ToString(), digest[..DigestCharacters].ToString());
    }

    private static bool IsDigest(ReadOnlySpan<char> digest)
    {
        foreach (var character in digest)
        { if (character is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')) { return false; } }
        return true;
    }

    private static InvalidOperationException Invalid() => new(SampleChunkPendingRf3Protocol.Missing);
}
