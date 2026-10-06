using Microsoft.Extensions.Options;
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

    internal static bool TryRead(HttpRequest request, out ReplicaMembershipAuthorityRequestHeaders headers, IOptions<OrleansMembershipOptions> membershipOptions)
    {
        const int TotalInitialValue = 0;
        const int IndexInitialValue = 0;
        const int EmptySuppliedCount = 1;
        const int IndexEmptyCount = 0;
        const int EmptyValueLength = 0;
        const int ValuesFirstIndex = 0;
        const int ValuesSecondIndex = 1;
        const int ValuesComponentIndex = 2;
        const int TryReadValuesComponentIndex = 3;

        const int CallerIncarnationIndex = 4;
        const int CallerVoterIndex = 5;
        const int CallerSiloIndex = 6;
        const int TimestampIndex = 7;
        const int NonceIndex = 8;
        const int SignatureIndex = 9;

        headers = null!;
        var total = TotalInitialValue;
        var values = new string[Names.Length];
        for (var index = IndexInitialValue; index < Names.Length; index++)
        {
            if (!request.Headers.TryGetValue(Names[index], out var supplied) || supplied.Count != EmptySuppliedCount
                || supplied[IndexEmptyCount] is not { } value || value.Length == EmptyValueLength
                || Encoding.UTF8.GetByteCount(value) > membershipOptions.Value.MaximumHeaderValueBytes)
            { return false; }
            total = checked(total + Encoding.UTF8.GetByteCount(Names[index]) + Encoding.UTF8.GetByteCount(value));
            if (total > membershipOptions.Value.MaximumHeaderBytes)
            { return false; }
            values[index] = value;
        }
        headers = new(values[ValuesFirstIndex], values[ValuesSecondIndex], values[ValuesComponentIndex], values[TryReadValuesComponentIndex], values[CallerIncarnationIndex], values[CallerVoterIndex], values[CallerSiloIndex],
            values[TimestampIndex], values[NonceIndex], values[SignatureIndex]);
        return true;
    }
}
