using System.Security.Cryptography;
using Aspire.Hosting;
using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Owns the actual derived images, inspected native namespaces and all partial rule mutation evidence.</summary>
internal sealed class ReplicaIsolationOwner
{
    private readonly ReplicaIsolationBuildPlan plan;
    private readonly ClusterFixtureSourceImage source;
    private readonly string root;
    private readonly IReadOnlyDictionary<string, string> names;
    private readonly Dictionary<string, ReplicaIsolationNode> nodes = new(StringComparer.Ordinal);
    private readonly ReplicaIsolationExecution execution = new();
    private DistributedApplication? application;
    private ReplicaIsolationRules? rules;
    private string? isolated;
    private bool restored;
    private const string SourceRevision = "GITHUB_SHA";
    private const int SiloPort = 11111;
    private const string ListRules = "-S";
    private const string NativeChain = "-N ";
    private const string RulePrefix = "-A ";

    internal ReplicaIsolationOwner(ReplicaIsolationBuildPlan plan, ClusterFixtureSourceImage source, string root,
        IReadOnlyDictionary<string, string> names)
    { this.plan = plan; this.source = source; this.root = root; this.names = names; }

    internal static async Task<ReplicaIsolationOwner> PrepareAsync(IDistributedApplicationBuilder builder,
        string root, string repository, IReadOnlyDictionary<string, string> names, CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsLinux())
        { throw new PlatformNotSupportedException("The owned namespace fault cohort requires genuine Linux containers and native control."); }
        var source = await ClusterFixtureImageIdentity.ReadVerifiedImageAsync(cancellationToken).ConfigureAwait(false);
        var profile = ClusterFixturePhysicalShardIdentity.ReadProfile(root);
        var plan = await ReplicaIsolationComposition.ApplyAsync(builder, repository, source.Reference,
            profile.Incarnation, cancellationToken).ConfigureAwait(false);
        return new(plan, source, root, names);
    }

    internal async Task StartAsync(DistributedApplication app, CancellationToken cancellationToken)
    {
        await ClusterFixtureImageIdentity.VerifyFaultBaseAsync(app, cancellationToken).ConfigureAwait(false);
        await ReplicaIsolationModelAssertions.VerifyAsync(app, plan, cancellationToken).ConfigureAwait(false);
        await app.StartAsync(cancellationToken).ConfigureAwait(false);
        await KeyLoad.AppHost.Features.TestInfrastructure.AspireStartupReadiness.WaitForHealthyAsync(app,
            plan.Targets.Select(target => target.ResourceName), cancellationToken).ConfigureAwait(false);
    }

    internal async Task VerifyStartedAsync(DistributedApplication app, CancellationToken cancellationToken)
    {
        application = app;
        var original = await ReplicaIsolationInspectionReader.ImageAsync(plan.BaseImage, cancellationToken);
        foreach (var target in plan.Targets)
        {
            var derived = await ReplicaIsolationInspectionReader.ImageAsync(target.ImageReference, cancellationToken);
            ReplicaIsolationInspectionReader.RequireDerived(original, derived, plan,
                Environment.GetEnvironmentVariable(SourceRevision) ?? throw new InvalidOperationException("The authenticated source revision is absent."));
            var current = await ReadNodeAsync(target, cancellationToken);
            if (current.Container.Image != derived.Id)
            { throw new InvalidOperationException("The started container does not use the actual owned derived image."); }
            nodes.Add(target.ResourceName, current);
            var artifacts = await ReplicaIsolationNativeArtifacts.ReadAsync(current.Container.Id, cancellationToken);
            await ReplicaIsolationEvidence.WriteStartedAsync(root, source, plan, current, original, derived,
                artifacts.Tools, artifacts.Modules, cancellationToken);
        }
        if (nodes.Values.Select(node => node.Container.IpAddress).Distinct(StringComparer.Ordinal).Count() != plan.Targets.Count)
        { throw new InvalidOperationException("The owned voter namespaces must have distinct native IPv4 endpoints."); }
    }

    internal IReadOnlyList<string> Resources => plan.Targets.Select(target => target.ResourceName).ToArray();

    internal Task RecordAuthorityAsync(IReadOnlyList<ReplicaIsolationAuthorityObservation> observations,
        CancellationToken cancellationToken) => ReplicaIsolationEvidence.WriteAuthorityAsync(root, observations, cancellationToken);

    internal Task VerifyRestoredNodesAsync(CancellationToken cancellationToken) => VerifyAllAsync(cancellationToken);

    internal async Task VerifyObservedFaultAsync(CancellationToken cancellationToken)
    {
        await VerifyAllAsync(cancellationToken);
        var input = await ReplicaIsolationExecution.ReadAsync(nodes[isolated!].Container.Id,
            [ReplicaIsolationFlowProtocol.ListCounters, rules!.InputChain, ReplicaIsolationFlowProtocol.Verbose,
                ReplicaIsolationFlowProtocol.Numeric, ReplicaIsolationFlowProtocol.ExactCounters], cancellationToken);
        var output = await ReplicaIsolationExecution.ReadAsync(nodes[isolated!].Container.Id,
            [ReplicaIsolationFlowProtocol.ListCounters, rules.OutputChain, ReplicaIsolationFlowProtocol.Verbose,
                ReplicaIsolationFlowProtocol.Numeric, ReplicaIsolationFlowProtocol.ExactCounters], cancellationToken);
        ReplicaIsolationCounters.RequireObserved(input.StandardOutput, output.StandardOutput);
        await ReplicaIsolationEvidence.WriteCountersAsync(root, input, output, cancellationToken);
    }

    internal async Task IsolateAsync(string resource, CancellationToken cancellationToken)
    {
        if (isolated is not null || !nodes.ContainsKey(resource))
        { throw new InvalidOperationException("One inspected owned former-leader namespace may be isolated once."); }
        isolated = resource;
        rules = new(Guid.NewGuid(), nodes.Values.Where(node => node.Resource != resource)
            .Select(node => node.Container.IpAddress).ToArray());
        foreach (var rule in rules.Installation())
        { await MutateAsync(resource, rule, cancellationToken); }
        await VerifyInstalledAsync(cancellationToken);
    }

    internal async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (isolated is null || restored)
        { return; }
        if (execution.RequiresStoppedNamespace)
        { throw new InvalidOperationException("Remote completion is unobserved; the owned namespace must be stopped before retirement."); }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RestoreRulesAsync(cancellationToken), failures);
        await ServerFailureObserver.ObserveAsync(() => ReplicaIsolationEvidence.WriteMutationsAsync(root, execution.Mutations,
            failures, cancellationToken), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        restored = true;
    }

    internal async Task SettleAfterStopAsync(bool stopped, CancellationToken cancellationToken)
    {
        if (!stopped)
        { throw new InvalidOperationException("The actual fault topology did not settle; its root and images remain owned."); }
        var evidence = new List<ReplicaIsolationRetirementObservation>();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => ReplicaIsolationRetirement.VerifyNodesStoppedAsync(
            names, nodes.Values.ToArray(), evidence, cancellationToken), failures);
        if (failures.Count == ReplicaIsolationFlowProtocol.Zero)
        { ServerFailureObserver.Observe(() => ReplicaIsolationRetirement.AssertLocks(root, evidence), failures); }
        if (failures.Count == ReplicaIsolationFlowProtocol.Zero)
        { await ServerFailureObserver.ObserveAsync(() => ReplicaIsolationRetirement.RemoveImagesAsync(plan, evidence, cancellationToken), failures); }
        await ServerFailureObserver.ObserveAsync(() => ReplicaIsolationEvidence.WriteRetirementAsync(root, evidence,
            failures.Count, CancellationToken.None), failures);
        await ServerFailureObserver.ObserveAsync(() => ReplicaIsolationEvidence.WriteMutationsAsync(root, execution.Mutations,
            failures, CancellationToken.None), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task MutateAsync(string resource, string[] rule, CancellationToken cancellationToken)
    {
        await VerifyAllAsync(cancellationToken);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => execution.ExecuteAsync(nodes[resource].Container.Id, rule, cancellationToken), failures);
        await ServerFailureObserver.ObserveAsync(() => ReplicaIsolationEvidence.WriteMutationsAsync(root, execution.Mutations,
            failures, CancellationToken.None), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private async Task VerifyAllAsync(CancellationToken cancellationToken)
    {
        var file = Path.Combine(plan.ContextPath, Path.GetFileName(ReplicaIsolationComposition.Dockerfile));
        if (!Directory.EnumerateFiles(plan.ContextPath, "*", SearchOption.AllDirectories).SequenceEqual([file])
            || Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(file, cancellationToken)
                .ConfigureAwait(false))) != plan.DockerfileSha256)
        { throw new InvalidOperationException("The original native fault build context changed."); }
        foreach (var target in plan.Targets)
        {
            if (await ReadNodeAsync(target, cancellationToken) != nodes[target.ResourceName])
            { throw new InvalidOperationException("An owned container/image/config/namespace or signed silo generation changed."); }
        }
    }

    private async Task<ReplicaIsolationNode> ReadNodeAsync(ReplicaIsolationBuildTarget target, CancellationToken cancellationToken)
    {
        var container = await ReplicaIsolationInspectionReader.ContainerAsync(names[target.ResourceName], plan, target, cancellationToken);
        _ = ReplicaIsolationRules.RequireIpv4(container.IpAddress);
        var discovery = await RequestCqrsRf3SignedDiscovery.ReadForProfileAsync(application
            ?? throw new InvalidOperationException("The native fault application is not started."), target.ResourceName,
            new NodeEpochRf3Profile(ClusterFixturePhysicalShardIdentity.ReadProfile(root)), cancellationToken);
        var endpoint = SiloAddress.FromParsableString(discovery.SiloAddress).Endpoint;
        if (!discovery.TransportReady || endpoint.Port != SiloPort || endpoint.Address.ToString() != container.IpAddress)
        { throw new InvalidOperationException("The signed actual voter silo endpoint does not match its owned namespace."); }
        return new(target.ResourceName, container, discovery.SiloAddress);
    }

    private async Task VerifyInstalledAsync(CancellationToken cancellationToken)
    {
        await VerifyAllAsync(cancellationToken);
        var snapshot = await ReplicaIsolationExecution.ReadAsync(nodes[isolated!].Container.Id, [ListRules], cancellationToken);
        ReplicaIsolationRuleState.RequireInstalled(snapshot.StandardOutput, rules!);
    }

    private async Task RestoreRulesAsync(CancellationToken cancellationToken)
    {
        await VerifyAllAsync(cancellationToken);
        var snapshot = await ReplicaIsolationExecution.ReadAsync(nodes[isolated!].Container.Id, [ListRules], cancellationToken);
        foreach (var rule in ReplicaIsolationRuleState.Removal(snapshot.StandardOutput, rules!))
        { await MutateAsync(isolated!, rule, cancellationToken); }
        var after = await ReplicaIsolationExecution.ReadAsync(nodes[isolated!].Container.Id, [ListRules], cancellationToken);
        if (after.StandardOutput.Split('\n').Any(line => line.StartsWith(NativeChain + rules!.InputChain, StringComparison.Ordinal)
            || line.StartsWith(NativeChain + rules!.OutputChain, StringComparison.Ordinal)
            || line.StartsWith(RulePrefix, StringComparison.Ordinal) && line.Split(' ').Any(word => word == rules!.InputChain || word == rules.OutputChain)))
        { throw new InvalidOperationException("An original owned namespace rule remains after restoration."); }
    }
}
