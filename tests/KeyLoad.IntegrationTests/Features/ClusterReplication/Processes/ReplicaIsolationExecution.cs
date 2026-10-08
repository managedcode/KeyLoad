namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Retains every original foreground process/reader result and distinguishes lost remote completion evidence.</summary>
internal sealed class ReplicaIsolationExecution
{
    private readonly List<ReplicaIsolationMutation> mutations = [];
    internal IReadOnlyList<ReplicaIsolationMutation> Mutations => mutations;
    internal bool RequiresStoppedNamespace => mutations.Any(item => item.RemoteCompletionUnobserved);

    internal async Task ExecuteAsync(string containerId, string[] rule, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var intent = new ReplicaIsolationMutation(containerId, rule.ToArray());
        mutations.Add(intent);
        try
        {
            var original = ContainerRuntimeDocker.RunAsync(ReplicaIsolationRules.NativeArguments(containerId, rule), cancellationToken);
            intent.Result = await original.ConfigureAwait(false);
            ContainerRuntimeDocker.EnsureSuccessful(intent.Result, ReplicaIsolationProtocol.Iptables, containerId);
        }
        catch (Exception)
        {
            intent.RemoteCompletionUnobserved = intent.Result is null;
            throw;
        }
    }

    internal static async Task<ContainerRuntimeProcessResult> ReadAsync(string containerId, string[] arguments,
        CancellationToken cancellationToken)
    {
        var result = await ContainerRuntimeDocker.RunAsync(ReplicaIsolationRules.NativeArguments(containerId, arguments), cancellationToken);
        ContainerRuntimeDocker.EnsureSuccessful(result, ReplicaIsolationProtocol.Iptables, containerId);
        return result;
    }
}
