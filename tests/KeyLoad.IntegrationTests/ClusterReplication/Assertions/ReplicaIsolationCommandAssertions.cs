using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using KeyLoad.AppHost.Features.ClusterReplication;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Refusal evidence runs in the original real namespace/SDK/MCP operation before any installed rule.</summary>
internal static class ReplicaIsolationCommandAssertions
{
    private const int First = 0;
    private const int Second = 1;
    private const string ListRules = "-S";

    internal static async Task RefusedAsync(DistributedApplication app,
        IReadOnlyList<string> resources, CancellationToken token)
    {
        var names = app.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<ContainerResource>()
            .Where(resource => resources.Contains(resource.Name, StringComparer.Ordinal))
            .ToDictionary(resource => resource.Name,
                resource => resource.Annotations.OfType<ContainerNameAnnotation>().Single().Name, StringComparer.Ordinal);
        var members = resources.ToArray();
        var selected = members[First];
        var before = new Dictionary<string, (ContainerRuntimeInspection Identity, ContainerRuntimeProcessResult Rules)>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            var identity = await ContainerRuntimeDocker.InspectAsync(names[member], token);
            before.Add(member, (identity, await ReplicaIsolationExecution.ReadAsync(identity.Id, [ListRules], token)));
        }
        var suffix = Guid.NewGuid().ToString("N")[..ReplicaIsolationCommandProtocol.SuffixLength];
        var rule = new ReplicaIsolationCommandProtocol.Rule(ReplicaIsolationCommandProtocol.Action.CreateChain, true, suffix);
        var selectedId = before[selected].Identity.Id;
        var stale = (selectedId[First] == '0' ? "1" : "0") + selectedId[Second..];
        var attempts = new[]
        {
            ("not-a-container", rule), (stale, rule), (before[members[Second]].Identity.Id, rule),
            (selectedId, rule with { ChainSuffix = "foreign-chain" }),
            (selectedId, rule with { Action = ReplicaIsolationCommandProtocol.Action.AppendDrop, PeerAddress = "127.0.0.1" })
        };
        foreach (var attempt in attempts)
        {
            var result = await AspireReplicaIsolationCommand.InvokeAsync(app, selected, attempt.Item1, attempt.Item2, token);
            await Assert.That(result.Success).IsFalse();
            await Assert.That(result.Data).IsNull();
            foreach (var member in members)
            {
                var actual = await ContainerRuntimeDocker.InspectAsync(names[member], token);
                await Assert.That(actual).IsEqualTo(before[member].Identity);
                var rules = await ReplicaIsolationExecution.ReadAsync(before[member].Identity.Id, [ListRules], token);
                await Assert.That(rules).IsEqualTo(before[member].Rules);
            }
        }
    }
}
