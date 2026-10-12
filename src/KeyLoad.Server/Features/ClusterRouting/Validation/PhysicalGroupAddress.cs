using System.Text;
using KeyLoad.Orleans;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class PhysicalGroupAddress
{
    private static readonly UTF8Encoding Utf8 = new(false, true);

    internal static bool CanonicalAddress(string? value, IOptions<OrleansMembershipOptions> membershipOptions)
    {
        if (!Bounded(value, membershipOptions))
        { return false; }
        try
        {
            var address = SiloAddress.FromParsableString(value!);
            return string.Equals(address.ToParsableString(), value, StringComparison.Ordinal);
        }
        catch (Exception error) when (error is ArgumentException or FormatException or OverflowException)
        { return false; }
    }

    private static bool Bounded(string? value, IOptions<OrleansMembershipOptions> membershipOptions)
    {
        if (string.IsNullOrWhiteSpace(value))
        { return false; }
        try
        { return Utf8.GetByteCount(value) <= membershipOptions.Value.MaximumAddressBytes; }
        catch (EncoderFallbackException) { return false; }
    }
}
