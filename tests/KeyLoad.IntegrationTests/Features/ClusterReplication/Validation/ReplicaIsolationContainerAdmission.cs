using System.Text.Json;
using KeyLoad.AppHost.Features.ClusterReplication;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.ReplicaIsolationNativeKeys;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class ReplicaIsolationContainerAdmission
{
    private const int SingleNetwork = 1;
    private const int SingleCapability = 1;
    private const int FirstIndex = 0;
    private const string NamePrefix = "/";
    private const string IncarnationFormat = "D";

    internal static ReplicaIsolationAdmissionMismatch Observe(JsonElement value, JsonProperty[] networks,
        string?[] capabilities, string name, ReplicaIsolationBuildPlan plan, ReplicaIsolationBuildTarget target)
    {
        if (value.GetProperty(State).GetString() != Running)
        { return ReplicaIsolationAdmissionMismatch.NotRunning; }
        if (value.GetProperty(Privileged).GetBoolean())
        { return ReplicaIsolationAdmissionMismatch.Privileged; }
        if (!string.IsNullOrEmpty(Text(value, PidMode)))
        { return ReplicaIsolationAdmissionMismatch.SharedPidNamespace; }
        if (networks.Length != SingleNetwork)
        { return ReplicaIsolationAdmissionMismatch.NetworkCount; }
        if (value.GetProperty(NetworkMode).GetString() != networks[FirstIndex].Name)
        { return ReplicaIsolationAdmissionMismatch.NetworkMode; }
        if (capabilities.Length != SingleCapability)
        { return ReplicaIsolationAdmissionMismatch.CapabilityCount; }
        if (capabilities[FirstIndex] != NetAdmin)
        { return ReplicaIsolationAdmissionMismatch.Capability; }
        if (Text(value, Name) != NamePrefix + name)
        { return ReplicaIsolationAdmissionMismatch.ContainerName; }
        if (Text(value, ConfigImage) != target.ImageReference)
        { return ReplicaIsolationAdmissionMismatch.ConfiguredImage; }
        if (Text(value, User) != target.ServiceUser)
        { return ReplicaIsolationAdmissionMismatch.RuntimeUser; }
        if (Text(value, FaultSource) != plan.DockerfileSha256)
        { return ReplicaIsolationAdmissionMismatch.BuildSource; }
        if (Guid.ParseExact(Text(value, ReplicaIsolationNativeKeys.Incarnation), IncarnationFormat) != plan.Incarnation)
        { return ReplicaIsolationAdmissionMismatch.Incarnation; }
        return ReplicaIsolationAdmissionMismatch.None;
    }

    private static string Text(JsonElement value, string key) => value.GetProperty(key).GetString()
        ?? throw new InvalidOperationException("A required safe native Docker identity field is absent.");
}
