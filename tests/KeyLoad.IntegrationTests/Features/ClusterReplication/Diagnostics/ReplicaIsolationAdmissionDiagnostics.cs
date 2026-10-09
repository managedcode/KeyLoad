using System.Text.Json;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.Features.ClusterReplication.ReplicaIsolationNativeKeys;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class ReplicaIsolationAdmissionDiagnostics
{
    private const int SchemaVersion = 3;
    private const int SingleNetwork = 1;
    private const int SingleCapability = 1;
    private const string PrefixedNetAdmin = "CAP_NET_ADMIN";
    private const int FirstIndex = 0;
    private const string DiagnosticKind = "OwnedRf3NamespaceAdmission";
    private const string FailureMessage = "The inspected namespace is not the exact admitted non-root fault cohort.";

    internal static void Throw(ReplicaIsolationAdmissionMismatch mismatch, JsonElement value, JsonProperty[] networks)
    {
        var failures = new List<Exception> { new InvalidOperationException(FailureMessage) };
        ServerFailureObserver.Observe(() => Write(mismatch, value, networks), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void Write(ReplicaIsolationAdmissionMismatch mismatch, JsonElement value, JsonProperty[] networks)
    {
        Console.Error.WriteLine(Describe(mismatch, value, networks));
    }

    internal static string Describe(ReplicaIsolationAdmissionMismatch mismatch, JsonElement value, JsonProperty[] networks)
    {
        var capabilities = value.GetProperty(CapAdd).EnumerateArray().Select(item => item.GetString()).ToArray();
        var capability = capabilities.Length == SingleCapability ? capabilities[FirstIndex] : null;
        var mode = value.GetProperty(NetworkMode).GetString();
        var name = networks.Length == SingleNetwork ? networks[FirstIndex].Name : null;
        string? id = null;
        if (networks.Length == SingleNetwork && networks[FirstIndex].Value.TryGetProperty(NetworkId, out var networkId)
            && networkId.ValueKind == JsonValueKind.String)
        { id = networkId.GetString(); }
        return JsonSerializer.Serialize(new
        {
            schemaVersion = SchemaVersion,
            kind = DiagnosticKind,
            firstMismatch = mismatch.ToString(),
            networkModePresent = !string.IsNullOrEmpty(mode),
            attachedNamePresent = !string.IsNullOrEmpty(name),
            attachedIdPresent = !string.IsNullOrEmpty(id),
            modeMatchesAttachedName = !string.IsNullOrEmpty(mode) && string.Equals(mode, name, StringComparison.Ordinal),
            modeMatchesAttachedId = !string.IsNullOrEmpty(mode) && string.Equals(mode, id, StringComparison.Ordinal),
            singleCapability = capabilities.Length == SingleCapability,
            soleCapabilityMatchesNetAdmin = capabilities.Length == SingleCapability && string.Equals(capability, NetAdmin, StringComparison.Ordinal),
            soleCapabilityMatchesPrefixedNetAdmin = capabilities.Length == SingleCapability && string.Equals(capability, PrefixedNetAdmin, StringComparison.Ordinal)
        });
    }
}
