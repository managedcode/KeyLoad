using System.Globalization;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.ClusterRouting;

internal static class TwoRf3RemoteReadResources
{
    private const string DataReadyHealth = "/health/ready";
    private const string Http = "http";

    internal static void Configure(IOptions<AppHostControlOptions> control,
        IResourceBuilder<ContainerResource>[] resources, bool registerOwners)
    {
        if (registerOwners)
        {
            foreach (var resource in resources)
            { resource.WithEnvironment(TwoRf3ProfileProtocol.RegistrationEnvironment, TwoRf3ProfileProtocol.Enabled); }
        }
        if (control.Value.RemoteDocumentReads)
        {
            foreach (var resource in resources)
            {
                resource.WithEnvironment(TwoRf3ProfileProtocol.RemoteDocumentEnvironment, TwoRf3ProfileProtocol.Enabled);
            }
            foreach (var resource in resources.Skip(TwoRf3ProfileProtocol.MembersPerGroup))
            { resource.WithHttpHealthCheck(DataReadyHealth, endpointName: Http); }
        }
        if (control.Value.ProtectedDocumentMovement)
        {
            foreach (var resource in resources)
            { resource.WithEnvironment(TwoRf3ProfileProtocol.MovementEnvironment, TwoRf3ProfileProtocol.Enabled); }
        }
        if (control.Value.MovementMaxBatchBytes is { } maxBatchBytes)
        {
            foreach (var resource in resources)
            {
                resource.WithEnvironment(TwoRf3ProfileProtocol.MovementMaxBatchBytesEnvironment,
                    maxBatchBytes.ToString(CultureInfo.InvariantCulture));
            }
        }
        if (control.Value.MovementMaxFrameBytes is { } maxFrameBytes)
        {
            foreach (var resource in resources)
            {
                resource.WithEnvironment(TwoRf3ProfileProtocol.MovementMaxFrameBytesEnvironment,
                    maxFrameBytes.ToString(CultureInfo.InvariantCulture));
            }
        }
        if (control.Value.RemotePartitionQueries)
        {
            foreach (var resource in resources)
            { resource.WithEnvironment(TwoRf3ProfileProtocol.RemoteQueryEnvironment, TwoRf3ProfileProtocol.Enabled); }
        }
    }
}
