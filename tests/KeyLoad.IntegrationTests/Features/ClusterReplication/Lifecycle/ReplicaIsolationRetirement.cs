using KeyLoad.AppHost.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.StorageRecovery;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Retires only settled owned namespaces and their actual generated local tags after native locks release.</summary>
internal static class ReplicaIsolationRetirement
{
    private const string List = "ps";
    private const string All = "--all";
    private const string NoTruncation = "--no-trunc";
    private const string Filter = "--filter";
    private const string Format = "--format";
    private const string IdFormat = "{{.ID}}";
    private const string NameFilter = "name=^/";
    private const string EndName = "$";
    private const string IdFilter = "id=";
    private const string AncestorFilter = "ancestor=";
    private const string RunningFormat = "{{json .State.Running}}";
    private const string False = "false";
    private const string ImageCommand = "image";
    private const string ImageList = "ls";
    private const string Quiet = "--quiet";
    private const string Remove = "rm";
    private const string NoPrune = "--no-prune";
    private const string NodeLock = "node.owner.lock";
    private const string DatabaseDirectory = "database";
    private const string ReplicaDirectory = "replica";
    private const string StoreLock = "store.owner.lock";
    private const int NoContainers = 0;
    private const int SingleContainer = 1;
    private const int ContainerIndex = 0;

    internal static async Task VerifyNodesStoppedAsync(IReadOnlyDictionary<string, string> names,
        IReadOnlyList<ReplicaIsolationNode> observed, List<ReplicaIsolationRetirementObservation> evidence, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        foreach (var pair in names)
        {
            await ServerFailureObserver.ObserveAsync(() => VerifyNodeStoppedAsync(pair, observed, evidence, cancellationToken), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task VerifyNodeStoppedAsync(KeyValuePair<string, string> pair,
        IReadOnlyList<ReplicaIsolationNode> observed, List<ReplicaIsolationRetirementObservation> evidence, CancellationToken cancellationToken)
    {
        var original = observed.SingleOrDefault(node => node.Resource == pair.Key);
        var filter = original is null ? NameFilter + pair.Value + EndName : IdFilter + original.Container.Id;
        var result = await ContainerRuntimeDocker.RunAsync([List, All, NoTruncation, Filter, filter, Format, IdFormat], cancellationToken);
        evidence.Add(new(List, pair.Key, result, false));
        ContainerRuntimeDocker.EnsureSuccessful(result, List, pair.Key);
        var ids = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(id => id.Trim()).ToArray();
        if (ids.Length == NoContainers)
        { return; }
        if (ids.Length != SingleContainer || original is not null && ids[ContainerIndex] != original.Container.Id)
        { throw new InvalidOperationException("The original owned namespace has ambiguous stopped identity."); }
        _ = ReplicaIsolationRules.NativeArguments(ids[ContainerIndex], []);
        var state = await ContainerRuntimeDocker.RunAsync([ReplicaIsolationNativeKeys.Inspect, Format, RunningFormat, ids[ContainerIndex]], cancellationToken);
        evidence.Add(new(ReplicaIsolationNativeKeys.Inspect, pair.Key, state, false));
        ContainerRuntimeDocker.EnsureSuccessful(state, ReplicaIsolationNativeKeys.Inspect, pair.Key);
        if (state.StandardOutput.Trim() != False)
        { throw new InvalidOperationException("An original owned namespace is still running; its images and root must remain."); }
    }

    internal static void AssertLocks(string root, List<ReplicaIsolationRetirementObservation> evidence)
    {
        var failures = new List<Exception>();
        foreach (var name in new[] { ClusterFixtureProtocol.NodeName(ReplicaIsolationFlowProtocol.First), ClusterFixtureProtocol.NodeName(ReplicaIsolationFlowProtocol.Second), ClusterFixtureProtocol.NodeName(ReplicaIsolationFlowProtocol.Voters) })
        {
            ServerFailureObserver.Observe(() => AcquireLock(Path.Combine(root, name, NodeLock), evidence), failures);
            ServerFailureObserver.Observe(() => AcquireLock(Path.Combine(root, name, DatabaseDirectory, StoreLock), evidence), failures);
            ServerFailureObserver.Observe(() => AcquireLock(Path.Combine(root, name, ReplicaDirectory, StoreLock), evidence), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void AcquireLock(string path, List<ReplicaIsolationRetirementObservation> evidence)
    {
        NodeEpochRf3OfflineFiles.AssertExclusive(path);
        evidence.Add(new(StoreLock, path, null, true));
    }

    internal static async Task RemoveImagesAsync(ReplicaIsolationBuildPlan plan, List<ReplicaIsolationRetirementObservation> evidence, CancellationToken cancellationToken)
    {
        var failures = new List<Exception>();
        foreach (var target in plan.Targets)
        {
            await ServerFailureObserver.ObserveAsync(() => RemoveImageAsync(plan, target, evidence, cancellationToken), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RemoveImageAsync(ReplicaIsolationBuildPlan plan, ReplicaIsolationBuildTarget target,
        List<ReplicaIsolationRetirementObservation> evidence, CancellationToken cancellationToken)
    {
        var listed = await ContainerRuntimeDocker.RunAsync([ImageCommand, ImageList, NoTruncation, Quiet, target.ImageReference], cancellationToken);
        evidence.Add(new(ImageList, target.ResourceName, listed, false));
        ContainerRuntimeDocker.EnsureSuccessful(listed, ImageList, target.ResourceName);
        var imageId = listed.StandardOutput.Trim();
        if (imageId.Length == NoContainers)
        { return; }
        var actual = await ReplicaIsolationInspectionReader.ImageAsync(target.ImageReference, cancellationToken);
        if (imageId != actual.Id || actual.FaultSource != plan.DockerfileSha256)
        { throw new InvalidOperationException("The actual partial-build tag is not owned by this immutable fault context."); }
        var original = await ReplicaIsolationInspectionReader.ImageAsync(plan.BaseImage, cancellationToken);
        ReplicaIsolationInspectionReader.RequireDerived(original, actual, plan, original.Revision);
        var references = await ContainerRuntimeDocker.RunAsync([List, All, NoTruncation, Filter, AncestorFilter + imageId, Format, IdFormat], cancellationToken);
        evidence.Add(new(List, target.ResourceName, references, false));
        ContainerRuntimeDocker.EnsureSuccessful(references, List, target.ResourceName);
        if (!string.IsNullOrWhiteSpace(references.StandardOutput))
        { throw new InvalidOperationException("A native container still references the fault image; the tag must remain."); }
        var removed = await ContainerRuntimeDocker.RunAsync([ImageCommand, Remove, NoPrune, target.ImageReference], cancellationToken);
        evidence.Add(new(Remove, target.ResourceName, removed, false));
        ContainerRuntimeDocker.EnsureSuccessful(removed, Remove, target.ResourceName);
        var remaining = await ContainerRuntimeDocker.RunAsync([ImageCommand, ImageList, NoTruncation, Quiet, target.ImageReference], cancellationToken);
        evidence.Add(new(ImageList, target.ResourceName, remaining, false));
        ContainerRuntimeDocker.EnsureSuccessful(remaining, ImageList, target.ResourceName);
        if (!string.IsNullOrWhiteSpace(remaining.StandardOutput))
        { throw new InvalidOperationException("An original owned fault tag remains after native removal."); }
    }
}
