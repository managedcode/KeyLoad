using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Produces stable fixture command identities scoped to the original move, phase and ordinal.</summary>
internal static class ControlledPartitionMovementTargetPhaseIds
{
    private const int GuidBytes = 16;
    private const string Separator = ":";
    private const string StageGrant = "target-stage-grant";
    private const string StageCommand = "target-stage-command";
    private const string StageAcknowledgement = "target-stage-acknowledgement";

    private const string InstallGrantPurpose = "target-install-grant";
    private const string InstallCommandPurpose = "target-install-command";
    private const string InstallAcknowledgementPurpose = "target-install-acknowledgement";

    private const string PublishGrantPurpose = "target-publish-grant";
    private const string PublishCommandPurpose = "target-publish-command";
    private const string PublishAcknowledgementPurpose = "target-publish-acknowledgement";
    private const int PublishOrdinal = 0;

    internal static Guid PublishGrant() => Of(PublishGrantPurpose, PublishOrdinal);
    internal static Guid PublishCommand() => Of(PublishCommandPurpose, PublishOrdinal);
    internal static Guid PublishAcknowledgement() => Of(PublishAcknowledgementPurpose, PublishOrdinal);

    internal static Guid InstallGrant(int ordinal) => Of(InstallGrantPurpose, ordinal);
    internal static Guid InstallCommand(int ordinal) => Of(InstallCommandPurpose, ordinal);
    internal static Guid InstallAcknowledgement(int ordinal) => Of(InstallAcknowledgementPurpose, ordinal);

    internal static Guid Grant(int ordinal) => Of(StageGrant, ordinal);
    internal static Guid Command(int ordinal) => Of(StageCommand, ordinal);
    internal static Guid Acknowledgement(int ordinal) => Of(StageAcknowledgement, ordinal);

    private static Guid Of(string purpose, int ordinal)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ordinal);
        var identity = ControlledPartitionMovementPrepareRequest.MoveId.ToString("D", CultureInfo.InvariantCulture)
            + Separator + purpose + Separator + ordinal.ToString(CultureInfo.InvariantCulture);
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        return new Guid(digest.AsSpan(0, GuidBytes));
    }
}
