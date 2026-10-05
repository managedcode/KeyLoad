namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3WaveArguments
{
    internal static string[] Create(string dataRoot, IReadOnlyDictionary<string, string> images,
        bool configureCohort, string? snapshotThresholdArgument, RequestCqrsProbeFixture? controls)
    {
        var args = new List<string>
        {
            RequestCqrsRf3Protocol.DataRootPrefix + dataRoot,
            RequestCqrsRf3Protocol.Ephemeral
        };
        if (snapshotThresholdArgument is not null)
        { args.Add(snapshotThresholdArgument); }
        if (configureCohort)
        {
            args.Add(RequestCqrsRf3Protocol.CohortEnabled);
            foreach (var node in new[] { RequestCqrsRf3Protocol.Node1, RequestCqrsRf3Protocol.Node2, RequestCqrsRf3Protocol.Node3 })
            { args.Add(RequestCqrsRf3Protocol.VoterPrefix + node + "=" + images[node]); }
        }
        if (controls is not null)
        {
            args.Add("--KeyLoadTests:RequestCqrsProbe:Enabled=true");
            args.Add("--KeyLoadTests:RequestCqrsProbe:Root=" + controls.Root);
            args.Add("--KeyLoadTests:RequestCqrsProbe:SessionId=" + controls.SessionId);
            if (controls.CaptureDiscovery)
            { args.Add("--KeyLoadTests:RequestCqrsProbe:DiscoveryCaptureMode=mixed-interface3-v1"); }
        }
        return [.. args];
    }

}
