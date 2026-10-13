using System.Globalization;
using KeyLoad.AppHost.Hosting;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.ClusterRouting;

internal static class TwoRf3RemoteReadResources
{
    private const string RegistrationLogEnvironment = "Logging__LogLevel__KeyLoad.Server.Features.ClusterRouting.PhysicalOwnerRegistrationWorker";
    private const string RegistrationLogLevel = "Information";

    internal static void Configure(IOptions<AppHostControlOptions> control,
        IResourceBuilder<ContainerResource>[] resources, bool registerOwners)
    {
        if (registerOwners)
        {
            foreach (var resource in resources)
            { resource.WithEnvironment(TwoRf3ProfileProtocol.RegistrationEnvironment, TwoRf3ProfileProtocol.Enabled); }
            foreach (var resource in resources.Take(TwoRf3ProfileProtocol.MembersPerGroup))
            { resource.WithEnvironment(RegistrationLogEnvironment, RegistrationLogLevel); }
        }
        if (control.Value.RemoteDocumentReads)
        {
            foreach (var resource in resources)
            {
                resource.WithEnvironment(TwoRf3ProfileProtocol.RemoteDocumentEnvironment, TwoRf3ProfileProtocol.Enabled);
            }
        }
        if (control.Value.ProtectedDocumentMovement)
        {
            foreach (var resource in resources)
            { resource.WithEnvironment(TwoRf3ProfileProtocol.MovementEnvironment, TwoRf3ProfileProtocol.Enabled); }
        }
        if (control.Value.TenantCapacityPlacement)
        {
            foreach (var resource in resources)
            { resource.WithEnvironment(TenantCapacityPlacementProfile.Environment, TwoRf3ProfileProtocol.Enabled); }
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
