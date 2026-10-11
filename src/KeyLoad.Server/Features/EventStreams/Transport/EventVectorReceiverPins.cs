using System.Globalization;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorReceiverPins
{
    private const string AddressSeparator = ":";

    internal static ReplicaMembershipAuthorityAddressPins Create(EventVectorRemoteCallValidation authority,
        IOptions<OrleansMembershipOptions> membership)
    {
        var endpoints = authority.ControlOwner.Endpoints.Select(static value =>
            new Uri(value, UriKind.Absolute).DnsSafeHost + AddressSeparator
                + MembershipAuthoritySettingsProtocol.NativeSiloPort.ToString(CultureInfo.InvariantCulture)).ToArray();
        return new(endpoints, membership);
    }
}
