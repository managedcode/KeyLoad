namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Captures actual installed native tools and inherited compiled module/PDB bytes from the owned image.</summary>
internal static class ReplicaIsolationNativeArtifacts
{
    private const string Cat = "/usr/bin/cat";
    private const string Sha256 = "/usr/bin/sha256sum";
    private const string Packages = "/app/keyload-fault-tools.txt";
    private const string IptablesVersion = "/app/keyload-fault-iptables.txt";
    private const string ServerDependencies = "/app/KeyLoad.Server.deps.json";
    private const string ApplicationDirectory = "/app/";
    private const string Dll = ".dll";
    private const string Pdb = ".pdb";
    private static readonly string[] Modules = ["KeyLoad.Abstractions", "KeyLoad.Core", "KeyLoad.Orleans",
        "KeyLoad.Replication", "KeyLoad.Security", "KeyLoad.Query", "KeyLoad.Storage.ZoneTree", "KeyLoad.Storage.IO",
        "KeyLoad.Server", "KeyLoad.Diagnostics", "KeyLoad.ServiceDefaults"];

    internal static async Task<(ContainerRuntimeProcessResult Tools, ContainerRuntimeProcessResult Modules)> ReadAsync(
        string containerId, CancellationToken cancellationToken)
    {
        _ = ReplicaIsolationRules.NativeArguments(containerId, []);
        var tools = await ReadAsync(containerId, Cat, [Packages, IptablesVersion], cancellationToken);
        var paths = Modules.SelectMany(module => new[] { ApplicationDirectory + module + Dll,
            ApplicationDirectory + module + Pdb }).Append(ServerDependencies).ToArray();
        var modules = await ReadAsync(containerId, Sha256, paths, cancellationToken);
        return (tools, modules);
    }

    private static async Task<ContainerRuntimeProcessResult> ReadAsync(string containerId, string executable,
        string[] arguments, CancellationToken cancellationToken)
    {
        var result = await ContainerRuntimeDocker.RunAsync([ReplicaIsolationProtocol.Exec, ReplicaIsolationProtocol.User,
            ReplicaIsolationProtocol.RootUser, containerId, ReplicaIsolationProtocol.Timeout, ReplicaIsolationProtocol.TermSignal,
            ReplicaIsolationProtocol.KillBound, ReplicaIsolationProtocol.ExecutionBound, executable, .. arguments], cancellationToken);
        ContainerRuntimeDocker.EnsureSuccessful(result, executable, containerId);
        return result;
    }
}
