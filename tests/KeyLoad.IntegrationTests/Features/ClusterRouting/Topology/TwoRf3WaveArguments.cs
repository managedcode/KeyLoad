using System.Globalization;
using KeyLoad.IntegrationTests.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class TwoRf3WaveArguments
{
    private const string RegisterArgument = "--KeyLoadTests:ClusterRouting:RegisterPhysicalOwners=true";
    private const string RemoteDocumentArgument = "--KeyLoadTests:ClusterRouting:RemoteDocumentReads=true";
    private const string RemoteQueryArgument = "--KeyLoadTests:ClusterRouting:RemotePartitionQueries=true";
    private const string ProbeEnabled = "--KeyLoadTests:RequestCqrsProbe:Enabled=true";
    private const string ProbeRoot = "--KeyLoadTests:RequestCqrsProbe:Root=";
    private const string ProbeSession = "--KeyLoadTests:RequestCqrsProbe:SessionId=";
    internal static string[] Create(string root, LocalRf3ImageSelection.Selection? selection,
        bool registerPhysicalOwners, bool remoteDocumentReads, bool remotePartitionQueries,
        RequestCqrsProbeFixture? controls = null, bool protectedDocuments = false, int? movementMaxBatchBytes = null, int? movementMaxFrameBytes = null, MovementFrameObservationFixture? frameObservation = null,
        KeyLoad.IntegrationTests.Features.StorageRecovery.NativeCapabilityOmissionRf3Fixture? nativeDiscoveryOmission = null, bool eventVectorReservedProbe = false, NativeMembershipDiagnosticFixture? nativeMembershipControls = null, bool tenantCapacityPlacement = false)
    {
        NativeMembershipDiagnosticWaveArguments.RequireSelection(nativeMembershipControls, controls,
            remoteDocumentReads, remotePartitionQueries, protectedDocuments, nativeDiscoveryOmission, eventVectorReservedProbe);
        var args = new List<string>
        {
            TwoRf3MembershipProtocol.DataRootPrefix + root,
            TwoRf3MembershipProtocol.EphemeralArgument,
            TwoRf3MembershipProtocol.ProfileArgument
        };
        if (tenantCapacityPlacement)
        {
            if (!protectedDocuments || !registerPhysicalOwners || !remotePartitionQueries)
            {
                throw new InvalidOperationException(TwoRf3MembershipProtocol.MissingState);
            }
            args.Add("--KeyLoadTests:ClusterRouting:TenantCapacityPlacement=true");
        }
        if (protectedDocuments)
        {
            args.Add("--KeyLoadTests:ClusterRouting:ProtectedDocumentMovement=true");
        }
        if (movementMaxBatchBytes is { } maxBatchBytes)
        {
            args.Add("--KeyLoadTests:ClusterRouting:MovementMaxBatchBytes=" + maxBatchBytes.ToString(CultureInfo.InvariantCulture));
        }
        if (movementMaxFrameBytes is { } maxFrameBytes)
        {
            args.Add("--KeyLoadTests:ClusterRouting:MovementMaxFrameBytes=" + maxFrameBytes.ToString(CultureInfo.InvariantCulture));
        }
        if (registerPhysicalOwners)
        {
            args.Add(RegisterArgument);
        }
        if (remoteDocumentReads)
        {
            args.Add(RemoteDocumentArgument);
        }
        if (remotePartitionQueries)
        {
            args.Add(RemoteQueryArgument);
        }
        AddObservationArguments(selection, controls, frameObservation, nativeDiscoveryOmission, eventVectorReservedProbe, nativeMembershipControls, args);
        return [.. args];
    }

    private static void AddObservationArguments(LocalRf3ImageSelection.Selection? selection, RequestCqrsProbeFixture? controls, MovementFrameObservationFixture? frameObservation, StorageRecovery.NativeCapabilityOmissionRf3Fixture? nativeDiscoveryOmission, bool eventVectorReservedProbe, NativeMembershipDiagnosticFixture? nativeMembershipControls, List<string> args)
    {
        if (controls is not null)
        {
            args.Add(ProbeEnabled);
            args.Add(ProbeRoot + controls.Root);
            args.Add(ProbeSession + controls.SessionId);
        }
        if (nativeMembershipControls is not null)
        {
            args.Add(ProbeEnabled);
            args.Add(ProbeRoot + nativeMembershipControls.Root);
            args.Add(ProbeSession + nativeMembershipControls.SessionId);
        }
        if (frameObservation is not null)
        {
            args.Add(MovementFrameObservationFixtureProtocol.EnabledArgument);
            args.Add(MovementFrameObservationFixtureProtocol.RootArgument + frameObservation.Root);
            args.Add(MovementFrameObservationFixtureProtocol.SessionArgument + frameObservation.SessionId);
        }
        if (nativeDiscoveryOmission is not null)
        {
            args.Add(KeyLoad.IntegrationTests.Features.StorageRecovery.NativeCapabilityOmissionRf3Protocol.EnabledArgument);
            args.Add(KeyLoad.IntegrationTests.Features.StorageRecovery.NativeCapabilityOmissionRf3Protocol.RootArgument + nativeDiscoveryOmission.Root);
            args.Add(KeyLoad.IntegrationTests.Features.StorageRecovery.NativeCapabilityOmissionRf3Protocol.SessionArgument + nativeDiscoveryOmission.SessionId);
        }
        if (eventVectorReservedProbe)
        {
            args.Add("--KeyLoadTests:EventVectorReservedProbe=true");
        }
        if (selection is not null)
        {
            args.AddRange(selection.CreateWaveArguments());
        }
    }
}
