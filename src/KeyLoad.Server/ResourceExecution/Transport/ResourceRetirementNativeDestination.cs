using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Resolves the original retirement RPC destination through the existing native owner authority.</summary>
internal static class ResourceRetirementNativeDestination
{
    private const int SelectedVoter = 0;

    internal static async Task<(string Voter, Orleans.Runtime.SiloAddress Address)> ResolveAsync(
        ResourceOwnerInventoryNativeAuthority authority, IOptions<NodeOptions> options, PartitionHost partition, RegisteredPhysicalOwnerV1 destination,
        CancellationToken token)
    {
        if (!PhysicalOwnerEntryValidation.Valid(destination))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
        }
        return await ResolveAsync(authority, options, partition, destination, destination.Owner.VoterIds[SelectedVoter], token).ConfigureAwait(false);
    }

    internal static async Task<(string Voter, Orleans.Runtime.SiloAddress Address)> ResolveAsync(
        ResourceOwnerInventoryNativeAuthority authority, IOptions<NodeOptions> options, PartitionHost partition, RegisteredPhysicalOwnerV1 destination,
        string voter, CancellationToken token)
    {
        if (!PhysicalOwnerEntryValidation.Valid(destination)
            || !destination.Owner.VoterIds.Contains(voter, StringComparer.Ordinal))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
        }
        var control = PhysicalOwnerConfiguredTuples.Control(options.Value, partition.Configuration);
        var address = await authority.ResolveAsync(control, destination, voter, token).ConfigureAwait(false);
        return (voter, address);
    }
}
