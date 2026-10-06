using KeyLoad.Replication;
using Microsoft.Extensions.Options;

namespace KeyLoad.Orleans;

/// <summary>Applies fixed-voter readiness rules over bounded authenticated observations.</summary>
internal sealed class ReplicaCohortAdmission
{
    private readonly ReplicaConfiguration configuration;
    private readonly ReplicaDiscoveryObservationCache observations;
    private readonly Func<string, bool, CancellationToken, Task<ReplicaDiscoveryObservation?>> resolve;

    internal ReplicaCohortAdmission(IOptions<ReplicaConfiguration> configurationOptions,
        ReplicaDiscoveryObservationCache observations,
        Func<string, bool, CancellationToken, Task<ReplicaDiscoveryObservation?>> resolve)
    {
        configuration = configurationOptions.Value;
        this.observations = observations;
        this.resolve = resolve;
    }

    internal bool HasCompatibleCohort
    {
        get
        {
            const int CompatibleInitialValue = 1;

            var local = observations.ReadLocal();
            if (!local.Compatible)
            {
                return false;
            }

            var compatible = CompatibleInitialValue;
            foreach (var voterId in configuration.VoterIds)
            {
                if (voterId == configuration.LocalId || !observations.TryFresh(voterId, out var observation))
                {
                    continue;
                }

                if (!observation!.ProtocolCompatible)
                {
                    return false;
                }

                if (observation.TransportReady)
                {
                    compatible++;
                }
            }

            return compatible >= configuration.Majority;
        }
    }

    internal async Task EnsureCompatibleCohortAsync(CancellationToken cancellationToken)
    {
        const int CompatibleInitialValue = 1;

        cancellationToken.ThrowIfCancellationRequested();
        var local = observations.ReadLocal();
        if (!local.TransportReady)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery);
        }

        if (!local.Compatible)
        {
            throw Incompatible();
        }

        var compatible = CompatibleInitialValue;
        var incompatible = false;
        foreach (var voterId in configuration.VoterIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (voterId == configuration.LocalId)
            {
                continue;
            }

            var observation = await ObserveAvailableAsync(voterId, cancellationToken).ConfigureAwait(false);
            if (observation is null)
            {
                continue;
            }

            incompatible |= !observation.ProtocolCompatible;
            if (observation.ProtocolCompatible && observation.TransportReady)
            {
                compatible++;
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (incompatible)
        {
            throw Incompatible();
        }

        if (compatible < configuration.Majority)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.InvalidDiscovery);
        }
    }

    private async Task<ReplicaDiscoveryObservation?> ObserveAvailableAsync(string voterId,
        CancellationToken cancellationToken)
    {
        try
        {
            return await resolve(voterId, false, cancellationToken).ConfigureAwait(false);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Unauthenticated)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return null;
        }
    }

    private static KeyLoadException Incompatible()
        => Errors.Fail(ErrorCode.OwnershipLost, ReplicaTransportProtocol.IncompatibleCohort);
}
