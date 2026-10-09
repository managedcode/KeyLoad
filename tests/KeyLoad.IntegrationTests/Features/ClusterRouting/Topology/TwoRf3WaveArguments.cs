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
        RequestCqrsProbeFixture? controls = null, bool protectedDocuments = false, int? movementMaxBatchBytes = null, int? movementMaxFrameBytes = null, MovementFrameObservationFixture? frameObservation = null)
    {
        var args = new List<string>
        {
            TwoRf3MembershipProtocol.DataRootPrefix + root,
            TwoRf3MembershipProtocol.EphemeralArgument,
            TwoRf3MembershipProtocol.ProfileArgument
        };
        if (protectedDocuments)
        { args.Add("--KeyLoadTests:ClusterRouting:ProtectedDocumentMovement=true"); }
        if (movementMaxBatchBytes is { } maxBatchBytes)
        { args.Add("--KeyLoadTests:ClusterRouting:MovementMaxBatchBytes=" + maxBatchBytes.ToString(CultureInfo.InvariantCulture)); }
        if (movementMaxFrameBytes is { } maxFrameBytes)
        { args.Add("--KeyLoadTests:ClusterRouting:MovementMaxFrameBytes=" + maxFrameBytes.ToString(CultureInfo.InvariantCulture)); }
        if (registerPhysicalOwners)
        { args.Add(RegisterArgument); }
        if (remoteDocumentReads)
        { args.Add(RemoteDocumentArgument); }
        if (remotePartitionQueries)
        { args.Add(RemoteQueryArgument); }
        if (controls is not null)
        { args.Add(ProbeEnabled); args.Add(ProbeRoot + controls.Root); args.Add(ProbeSession + controls.SessionId); }
        if (frameObservation is not null)
        {
            args.Add(MovementFrameObservationFixtureProtocol.EnabledArgument);
            args.Add(MovementFrameObservationFixtureProtocol.RootArgument + frameObservation.Root);
            args.Add(MovementFrameObservationFixtureProtocol.SessionArgument + frameObservation.SessionId);
        }
        if (selection is not null)
        {
            args.AddRange(selection.CreateWaveArguments());
        }
        return [.. args];
    }

}
