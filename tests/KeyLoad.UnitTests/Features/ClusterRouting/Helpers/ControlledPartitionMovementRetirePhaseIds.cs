using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Produces stable fixture command identities scoped to the original move, phase and ordinal.</summary>
internal static class ControlledPartitionMovementRetirePhaseIds
{
    private const int GuidBytes = 16;
    private const string Separator = ":";
    private const string GrantPurpose = "source-retire-grant";
    private const string CommandPurpose = "source-retire-command";
    private const string AcknowledgementPurpose = "source-retire-acknowledgement";

    internal static Guid Grant(int ordinal) => Of(GrantPurpose, ordinal);
    internal static Guid Command(int ordinal) => Of(CommandPurpose, ordinal);
    internal static Guid Acknowledgement(int ordinal) => Of(AcknowledgementPurpose, ordinal);

    private static Guid Of(string purpose, int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        var identity = ControlledPartitionMovementPrepareRequest.MoveId.ToString("D", CultureInfo.InvariantCulture)
            + Separator + purpose + Separator + ordinal.ToString(CultureInfo.InvariantCulture);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return new Guid(digest.AsSpan(0, GuidBytes));
    }
}
