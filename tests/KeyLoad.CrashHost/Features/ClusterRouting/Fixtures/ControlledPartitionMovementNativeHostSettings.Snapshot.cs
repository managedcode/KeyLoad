using System.Collections.Immutable;
using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting;

internal sealed partial class ControlledPartitionMovementNativeHostSettings
{
    private const int SnapshotVersion = 1;
    private const int FirstSource = 0;
    private const int NoBatchOverrideArguments = 0;
    private const int SingleBatchOverrideArgument = 1;

    private ControlledPartitionMovementNativeHostSettings(ControlledPartitionMovementNativeHostInput input,
        string originalRoot)
    {
        (origins, silos) = ControlledPartitionMovementNativeHostInputValidation.Read(input, originalRoot);
        maximumBatchBytes = ReadMaximumBatchBytes(input.Arguments[FirstSource]);
        Root = input.Root;
        signing = Read(input, FirstSource, nameof(NodeOptions.SigningKey));
        controlPeer = Read(input, FirstSource, nameof(NodeOptions.PeerSecret));
        destinationPeer = Read(input, FirstDestination, nameof(NodeOptions.PeerSecret));
        cluster = Read(input, FirstSource, nameof(NodeOptions.ClusterId));
        AdministratorKey = Read(input, FirstSource, nameof(NodeOptions.AdminKey));
        Control = Owner(ControlId, ControlIncarnation, FirstSource);
        Destination = Owner(DestinationId, DestinationIncarnation, FirstDestination);
        if (!Same(input.Control, Control) || !Same(input.Destination, Destination))
        {
            throw ControlledPartitionMovementNativeHostInputValidation.Invalid();
        }
        for (var index = FirstSource; index < input.Arguments.Length; index++)
        {
            if (!Arguments(index).SequenceEqual(input.Arguments[index], StringComparer.Ordinal))
            {
                throw ControlledPartitionMovementNativeHostInputValidation.Invalid();
            }
        }
    }

    internal ControlledPartitionMovementNativeHostInput CaptureInput()
        => new(SnapshotVersion,
            Enumerable.Range(FirstSource, ControlledPartitionMovementNativeHostEndpoints.OwnerCount)
                .Select(index => Arguments(index).ToImmutableArray()).ToImmutableArray(),
            silos[FirstSource].Port, Root, Control, Destination);

    internal static ControlledPartitionMovementNativeHostSettings Restore(
        ControlledPartitionMovementNativeHostInput input, string originalRoot)
        => new(input, originalRoot);

    private static string Read(ControlledPartitionMovementNativeHostInput input, int index, string key)
        => ControlledPartitionMovementNativeHostInputValidation.Read(input.Arguments[index], key);

    private static int? ReadMaximumBatchBytes(ImmutableArray<string> arguments)
    {
        var selected = arguments.Where(value => value.StartsWith(BatchBytesArgumentPrefix,
            StringComparison.Ordinal)).ToArray();
        if (selected.Length == NoBatchOverrideArguments)
        {
            return null;
        }
        if (selected.Length != SingleBatchOverrideArgument || !int.TryParse(selected[FirstSource][BatchBytesArgumentPrefix.Length..],
            System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var maximum))
        {
            throw ControlledPartitionMovementNativeHostInputValidation.Invalid();
        }
        new DatabaseLimits { MaxBatchBytes = maximum }.Validate();
        return maximum;
    }

    private static bool Same(RegisteredPhysicalOwnerV1 retained, RegisteredPhysicalOwnerV1 configured)
        => retained is not null && retained.Owner is not null
            && retained.Owner.PhysicalShardId == configured.Owner.PhysicalShardId
            && retained.Owner.Incarnation == configured.Owner.Incarnation
            && retained.Owner.PlacementEpoch == configured.Owner.PlacementEpoch
            && retained.Owner.VoterIds.AsSpan().SequenceEqual(configured.Owner.VoterIds.AsSpan())
            && retained.Endpoints.AsSpan().SequenceEqual(configured.Endpoints.AsSpan());
}
