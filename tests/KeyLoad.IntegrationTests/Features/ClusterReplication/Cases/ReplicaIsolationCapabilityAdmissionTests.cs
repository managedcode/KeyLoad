using System.Text.Json;
using KeyLoad.AppHost.Features.ClusterReplication;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Supporting native-field admission control; does not create or qualify Docker resources.</summary>
internal sealed class ReplicaIsolationCapabilityAdmissionTests
{
    private const string Node = "node1";
    private const string Image = "fixture-image";
    private const string ServiceUser = "fixture-user";
    private const string Source = "fixture-source";
    private const string Network = "fixture-network";
    private static readonly Guid Incarnation = Guid.Parse("45c96938-9c08-4474-ab8e-186bfedffafc");
    private const string RowPrefix = "{\"schemaVersion\":3,\"kind\":\"OwnedRf3NamespaceAdmission\",\"firstMismatch\":\"";
    private const string RowNetwork = "\",\"networkModePresent\":true,\"attachedNamePresent\":true,\"attachedIdPresent\":true,\"modeMatchesAttachedName\":false,\"modeMatchesAttachedId\":true,\"singleCapability\":";

    [Test]
    public async Task WrongPrefixedAbsentAndMultipleCapabilitiesRejectBeforeExactHealthyAdmission()
    {
        await VerifyAsync(["SYS_ADMIN"], ReplicaIsolationAdmissionMismatch.Capability, "true", "false", "false");
        await VerifyAsync(["CAP_NET_ADMIN"], ReplicaIsolationAdmissionMismatch.Capability, "true", "false", "true");
        await VerifyAsync([], ReplicaIsolationAdmissionMismatch.CapabilityCount, "false", "false", "false");
        await VerifyAsync(["NET_ADMIN", "SYS_ADMIN"], ReplicaIsolationAdmissionMismatch.CapabilityCount, "false", "false", "false");
        await VerifyAsync(["NET_ADMIN"], ReplicaIsolationAdmissionMismatch.None, "true", "true", "false");
    }

    private static async Task VerifyAsync(string[] capabilities, ReplicaIsolationAdmissionMismatch expected,
        string single, string exact, string prefixed)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            State = "running",
            Privileged = false,
            PidMode = "",
            NetworkMode = "fixture-network-id",
            Networks = new Dictionary<string, object> { [Network] = new { NetworkID = "fixture-network-id" } },
            CapAdd = capabilities,
            Name = "/node1",
            ConfigImage = Image,
            User = ServiceUser,
            FaultSource = Source,
            Incarnation = Incarnation.ToString("D")
        }));
        var value = document.RootElement;
        var networks = value.GetProperty(ReplicaIsolationNativeKeys.Networks).EnumerateObject().ToArray();
        var target = new ReplicaIsolationBuildTarget(Node, Image, ServiceUser);
        var plan = new ReplicaIsolationBuildPlan(Image, Image, Source, "fixture-context", Incarnation, [target]);
        var actual = ReplicaIsolationContainerAdmission.Observe(value, networks, capabilities, Node, plan, target);
        await Assert.That(actual).IsEqualTo(expected);
        var expectedRow = RowPrefix + expected + RowNetwork + single
            + ",\"soleCapabilityMatchesNetAdmin\":" + exact
            + ",\"soleCapabilityMatchesPrefixedNetAdmin\":" + prefixed + "}";
        await Assert.That(ReplicaIsolationAdmissionDiagnostics.Describe(actual, value, networks)).IsEqualTo(expectedRow);
    }
}
