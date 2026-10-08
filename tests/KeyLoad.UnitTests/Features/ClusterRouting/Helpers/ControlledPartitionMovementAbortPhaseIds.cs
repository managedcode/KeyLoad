using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using KeyLoad.Core.Features.ClusterRouting.Contracts;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

/// <summary>Scopes stable original abort phase identities to the immutable move, role and bounded ordinal.</summary>
internal static class ControlledPartitionMovementAbortPhaseIds
{
    private const int GuidBytes = 16;
    private const string GuidFormat = "N";
    private const string Separator = ":";
    private const string GrantPurpose = "abort-grant";
    private const string CommandPurpose = "abort-command";
    private const string AcknowledgementPurpose = "abort-acknowledgement";

    internal static Guid Grant(PartitionMovePeerStage stage, PartitionMoveCleanupRole role, int ordinal)
        => Create(GrantPurpose, stage, role, ordinal);
    internal static Guid Command(PartitionMovePeerStage stage, PartitionMoveCleanupRole role, int ordinal)
        => Create(CommandPurpose, stage, role, ordinal);
    internal static Guid Acknowledgement(PartitionMovePeerStage stage, PartitionMoveCleanupRole role, int ordinal)
        => Create(AcknowledgementPurpose, stage, role, ordinal);

    private static Guid Create(string purpose, PartitionMovePeerStage stage,
        PartitionMoveCleanupRole role, int ordinal)
    {
        var identity = ControlledPartitionMovementPrepareRequest.MoveId.ToString(GuidFormat) + Separator + purpose
            + Separator + ((int)stage).ToString(CultureInfo.InvariantCulture)
            + Separator + ((int)role).ToString(CultureInfo.InvariantCulture)
            + Separator + ordinal.ToString(CultureInfo.InvariantCulture);
        return new Guid(SHA256.HashData(Encoding.UTF8.GetBytes(identity)).AsSpan(0, GuidBytes));
    }
}
