using KeyLoad.AppHost.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class ClusterFixtureProfile
{
    internal static LocalProfile ReadProfile(string root)
    {
        var path = Path.Combine(root, ClusterFixtureProtocol.ProfileFileName);
        var execution = IntegrationProfileOptions.Execution();
        return global::ClusterProfileStore.DeserializeCurrent(global::ClusterProfileStore.ReadBoundedBytes(path, execution), execution);
    }

}
