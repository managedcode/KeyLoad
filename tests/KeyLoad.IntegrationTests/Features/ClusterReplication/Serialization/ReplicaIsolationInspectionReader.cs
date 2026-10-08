using System.Text.Json;
using KeyLoad.AppHost.Features.ClusterReplication;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.ReplicaIsolationNativeKeys;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Decodes bounded safe native Docker output and rejects ambiguous or privileged namespaces.</summary>
internal static class ReplicaIsolationInspectionReader
{
    private const int FirstIndex = 0;

    internal static async Task<ReplicaIsolationInspection> ContainerAsync(string name,
        ReplicaIsolationBuildPlan plan, ReplicaIsolationBuildTarget target, CancellationToken cancellationToken)
    {
        var result = await ContainerRuntimeDocker.RunAsync([Inspect, Format, ContainerFormat, name], cancellationToken);
        ContainerRuntimeDocker.EnsureSuccessful(result, Inspect, name);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var value = document.RootElement;
        var networks = value.GetProperty(Networks).EnumerateObject().ToArray();
        var capabilities = value.GetProperty(CapAdd).EnumerateArray().Select(item => item.GetString()).ToArray();
        var mismatch = ReplicaIsolationContainerAdmission.Observe(value, networks, capabilities, name, plan, target);
        if (mismatch != ReplicaIsolationAdmissionMismatch.None)
        { ReplicaIsolationAdmissionDiagnostics.Throw(mismatch); }
        var id = Text(value, Id);
        _ = ReplicaIsolationRules.NativeArguments(id, []);
        return new(id, Text(value, Name), Text(value, Image), Text(value, ConfigImage), Text(value, User),
            Text(value, StartedAt), networks[FirstIndex].Value.GetProperty(IpAddress).GetString()
                ?? throw new InvalidOperationException("The owned namespace has no IPv4 endpoint."),
            plan.Incarnation, plan.DockerfileSha256);
    }

    internal static async Task<ReplicaIsolationImage> ImageAsync(string reference, CancellationToken cancellationToken)
    {
        var result = await ContainerRuntimeDocker.RunAsync([ImageInspect, Inspect, Format, ImageFormat, reference], cancellationToken);
        ContainerRuntimeDocker.EnsureSuccessful(result, ImageInspect, reference);
        using var document = JsonDocument.Parse(result.StandardOutput);
        var value = document.RootElement;
        return new(Text(value, Id), Text(value, User), Text(value, Revision), value.GetProperty(FaultSource).GetString(),
            Text(value, Os), Text(value, Architecture), value.GetProperty(Layers).EnumerateArray()
                .Select(item => item.GetString() ?? throw new InvalidOperationException("An OCI rootfs layer identity is absent.")).ToArray());
    }

    internal static void RequireDerived(ReplicaIsolationImage original, ReplicaIsolationImage derived,
        ReplicaIsolationBuildPlan plan, string expectedRevision)
    {
        if (original.Os != Linux || derived.Os != Linux || derived.Architecture != original.Architecture
            || original.Revision != expectedRevision || derived.Revision != expectedRevision
            || derived.User != original.User || derived.FaultSource != plan.DockerfileSha256
            || original.Layers.Length == FirstIndex || derived.Layers.Length <= original.Layers.Length
            || !derived.Layers.Take(original.Layers.Length).SequenceEqual(original.Layers, StringComparer.Ordinal))
        { throw new InvalidOperationException("The actual fault image does not inherit the authenticated native server image."); }
    }

    private static string Text(JsonElement value, string key) => value.GetProperty(key).GetString()
        ?? throw new InvalidOperationException("A required safe native Docker identity field is absent.");
}
