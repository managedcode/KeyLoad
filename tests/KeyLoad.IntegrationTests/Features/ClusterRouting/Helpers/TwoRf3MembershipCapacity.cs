using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using KeyLoad.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class TwoRf3MembershipCapacity
{
    private const string ParameterPrefix = "Parameters:";
    private const string PublicHttpEndpoint = "http";
    private static readonly string[] CapacityRetainedParameters =
        ["membership-physical-b", "membership-incarnation-b", "membership-peer-b"];
    private readonly Dictionary<string, string> originalParameters = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> originalPorts = new(StringComparer.Ordinal);

    internal async Task ReconfigureMovementCapacityAsync(TwoRf3MembershipWave wave, int maxBatchBytes, CancellationToken cancellationToken)
    {
        new DatabaseLimits { MaxBatchBytes = maxBatchBytes }.Validate();
        if (!wave.protectedDocuments || originalParameters.Count != CapacityRetainedParameters.Length
            || originalPorts.Count != TwoRf3MembershipProtocol.NodeCount)
        { throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState); }
        _ = await wave.StopForDirectoryReadAsync().ConfigureAwait(false);
        wave.movementMaxBatchBytes = maxBatchBytes;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => TwoRf3MembershipWaveStartup.StartCoreAsync(wave, cancellationToken), failures).ConfigureAwait(false);
        if (failures.Count > 0)
        {
            await wave.DisposeApplicationAsync(failures).ConfigureAwait(false);
            if (wave.CanCheckLocks)
            { ServerFailureObserver.Observe(wave.AssertAllNodeLocksReleased, failures); }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    internal void BindOriginalParameters(HostApplicationBuilderSettings settings)
    {
        // Original secret parameter bytes stay in the owning native in-memory provider, never AppHost arguments.
        settings.Configuration ??= new ConfigurationManager();
        settings.Configuration.AddInMemoryCollection(originalParameters.Select(pair =>
            new KeyValuePair<string, string?>(ParameterPrefix + pair.Key, pair.Value)));
    }

    internal async Task RequireOriginalParametersAsync(IEnumerable<ParameterResource> resources,
        CancellationToken cancellationToken)
    {
        foreach (var name in CapacityRetainedParameters)
        {
            var actual = await resources.Single(resource => resource.Name == name).GetValueAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
            if (originalParameters.TryGetValue(name, out var original))
            {
                if (!string.Equals(original, actual, StringComparison.Ordinal))
                { throw new InvalidOperationException(TwoRf3MembershipProtocol.ImageMismatch); }
            }
            else
            { originalParameters.Add(name, actual); }
        }
    }

    internal void CaptureOriginalEndpoints(TwoRf3MembershipWave wave)
    {
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var port = wave.Application.GetEndpoint(node, PublicHttpEndpoint).Port;
            if (originalPorts.TryGetValue(node, out var original))
            {
                if (original != port)
                { throw new InvalidOperationException(TwoRf3MembershipProtocol.ImageMismatch); }
            }
            else
            { originalPorts.Add(node, port); }
        }
    }

    internal void RestoreOriginalEndpoints(IEnumerable<ContainerResource> resources)
    {
        foreach (var resource in resources.Where(resource => originalPorts.ContainsKey(resource.Name)))
        {
            resource.Annotations.OfType<EndpointAnnotation>().Single(endpoint => endpoint.Name == PublicHttpEndpoint).Port
                = originalPorts[resource.Name];
        }
    }
}
