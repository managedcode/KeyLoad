using System.Text;
using KeyLoad.Orleans;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed record ReplicaMembershipAuthorityRequestHeaders(string Cluster, string AuthorityPhysical,
    string AuthorityIncarnation, string CallerPhysical, string CallerIncarnation, string CallerVoter,
    string CallerSilo, string Timestamp, string Nonce, string Signature);

internal static class ReplicaMembershipAuthorityHttpHeaders
{
    private static readonly string[] Names =
    [
        ReplicaMembershipAuthorityProtocol.ClusterHeader, ReplicaMembershipAuthorityProtocol.AuthorityPhysicalHeader,
        ReplicaMembershipAuthorityProtocol.AuthorityIncarnationHeader, ReplicaMembershipAuthorityProtocol.CallerPhysicalHeader,
        ReplicaMembershipAuthorityProtocol.CallerIncarnationHeader, ReplicaMembershipAuthorityProtocol.CallerVoterHeader,
        ReplicaMembershipAuthorityProtocol.CallerSiloHeader, ReplicaMembershipAuthorityProtocol.TimestampHeader,
        ReplicaMembershipAuthorityProtocol.NonceHeader, ReplicaMembershipAuthorityProtocol.SignatureHeader
    ];

    internal static bool TryRead(HttpRequest request, out ReplicaMembershipAuthorityRequestHeaders headers)
    {
        headers = null!;
        var total = 0;
        var values = new string[Names.Length];
        for (var index = 0; index < Names.Length; index++)
        {
            if (!request.Headers.TryGetValue(Names[index], out var supplied) || supplied.Count != 1
                || supplied[0] is not { } value || value.Length == 0
                || Encoding.UTF8.GetByteCount(value) > ReplicaMembershipAuthorityProtocol.MaximumHeaderValueBytes)
            { return false; }
            total = checked(total + Encoding.UTF8.GetByteCount(Names[index]) + Encoding.UTF8.GetByteCount(value));
            if (total > ReplicaMembershipAuthorityProtocol.MaximumHeaderBytes) { return false; }
            values[index] = value;
        }
        headers = new(values[0], values[1], values[2], values[3], values[4], values[5], values[6],
            values[7], values[8], values[9]);
        return true;
    }
}
