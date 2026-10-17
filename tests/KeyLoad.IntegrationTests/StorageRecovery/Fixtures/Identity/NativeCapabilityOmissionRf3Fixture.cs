using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal sealed class NativeCapabilityOmissionRf3Fixture
{
    private readonly RequestCqrsProbeFixture owners;
    private readonly NativeDiscoveryOmissionJson json = new(IntegrationRoutingOptions.ProbeExecution());
    private readonly Dictionary<string, byte[]> originals = new(StringComparer.Ordinal);
    private readonly CancellationToken originalToken;
    private bool disarmed;
    private bool retained;
    private NativeCapabilityOmissionRf3Fixture(RequestCqrsProbeFixture owners, CancellationToken originalToken)
    {
        this.owners = owners;
        this.originalToken = originalToken;
    }
    internal string Root => owners.Root;
    internal string SessionId => owners.SessionId;
    internal static NativeCapabilityOmissionRf3Fixture Create(string dataRoot, CancellationToken originalToken)
        => new(RequestCqrsProbeFixture.Create(dataRoot, Guid.NewGuid()), originalToken);
    internal void Arm(bool official)
    {
        var arm = new NativeDiscoveryOmissionArm(NativeDiscoveryOmissionProtocol.Version, SessionId, Guid.NewGuid(),
            RequestCqrsProbeFixtureProtocol.Node1Origin, RequestCqrsProbeFixtureProtocol.Node2Origin,
            official ? NativeDiscoveryOmissionProtocol.McpRoute : NativeDiscoveryOmissionProtocol.SdkRoute);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(arm, NativeDiscoveryOmissionJsonContext.Default.NativeDiscoveryOmissionArm);
        _ = json.ReadArm(bytes);
        RequestCqrsProbeFileStore.WriteAtomic(Root, NativeDiscoveryOmissionProtocol.ArmFile, bytes);
        originals.Add(NativeDiscoveryOmissionProtocol.ArmFile, bytes);
    }
    internal void ArmPurpose(bool official, Guid commandId)
    {
        var arm = new NativeSignedPurposeArm(NativeDiscoveryOmissionProtocol.Version, SessionId, Guid.NewGuid(),
            RequestCqrsProbeFixtureProtocol.Node1Origin,
            official ? NativeDiscoveryOmissionProtocol.McpRoute : NativeDiscoveryOmissionProtocol.SdkRoute, commandId);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(arm, NativeSignedPurposeJsonContext.Default.NativeSignedPurposeArm);
        _ = new NativeSignedPurposeJson(IntegrationRoutingOptions.ProbeExecution()).Arm(bytes);
        RequestCqrsProbeFileStore.WriteAtomic(Root, NativeSignedPurposeProtocol.ArmFile, bytes);
        originals.Add(NativeSignedPurposeProtocol.ArmFile, bytes);
    }
    internal async Task RequirePurposeCausalRefusalAsync()
    {
        var purposeJson = new NativeSignedPurposeJson(IntegrationRoutingOptions.ProbeExecution());
        var arm = purposeJson.Arm(originals[NativeSignedPurposeProtocol.ArmFile]);
        var issued = ReadPurpose(purposeJson, NativeSignedPurposeProtocol.IssuedFile);
        var refused = ReadPurpose(purposeJson, NativeSignedPurposeProtocol.RejectedFile);
        await Assert.That(issued.Version).IsEqualTo(arm.Version);
        await Assert.That(issued.SessionId).IsEqualTo(arm.SessionId);
        await Assert.That(issued.ArmId).IsEqualTo(arm.ArmId);
        await Assert.That(issued.SourceVoter).IsEqualTo(arm.SourceVoter);
        await Assert.That(issued.Route).IsEqualTo(arm.Route);
        await Assert.That(issued.CommandId).IsEqualTo(arm.CommandId);
        await Assert.That(issued.ConnectionId).IsNotEqualTo(Guid.Empty);
        await Assert.That(issued.RequestId).IsNotEqualTo(Guid.Empty);
        await Assert.That(issued.Incarnation).IsNotEqualTo(Guid.Empty);
        await Assert.That(issued.PrincipalId).IsNotNullOrEmpty();
        await Assert.That(issued.Stage).IsEqualTo(NativeSignedPurposeProtocol.Issued);
        await Assert.That(issued.ReceiverVoter).IsNull();
        await Assert.That(refused.ReceiverVoter is RequestCqrsProbeFixtureProtocol.Node1Origin
            or RequestCqrsProbeFixtureProtocol.Node2Origin or RequestCqrsProbeFixtureProtocol.Node3Origin).IsTrue();
        await Assert.That(refused).IsEqualTo(issued with
        {
            Stage = NativeSignedPurposeProtocol.Rejected,
            ReceiverVoter = refused.ReceiverVoter
        });
    }
    private NativeSignedPurposeWitness ReadPurpose(NativeSignedPurposeJson purposeJson, string name)
    {
        var bytes = RequestCqrsProbeFiles.ReadRecord(Path.Combine(Root, name), IntegrationRoutingOptions.ProbeExecution());
        originals.Add(name, bytes);
        return purposeJson.Witness(bytes);
    }
    internal async Task RequireCausalRefusalAsync()
    {
        var arm = json.ReadArm(originals[NativeDiscoveryOmissionProtocol.ArmFile]);
        var source = Read(NativeDiscoveryOmissionProtocol.RequestFile);
        var omitted = Read(NativeDiscoveryOmissionProtocol.OmittedFile);
        var verified = Read(NativeDiscoveryOmissionProtocol.VerifiedFile);
        await Assert.That(source.Version).IsEqualTo(arm.Version);
        await Assert.That(source.SessionId).IsEqualTo(SessionId);
        await Assert.That(source.ArmId).IsEqualTo(arm.ArmId);
        await Assert.That(source.SourceVoter).IsEqualTo(arm.SourceVoter);
        await Assert.That(source.TargetVoter).IsEqualTo(arm.TargetVoter);
        await Assert.That(source.Route).IsEqualTo(arm.Route);
        await Assert.That(source.CommandId).IsEqualTo(Guid.Empty);
        await Assert.That(source.ConnectionId).IsNotEqualTo(Guid.Empty);
        await Assert.That(source.ChildRequestId).IsNotEqualTo(Guid.Empty);
        await Assert.That(source.Nonce).IsNotEqualTo(Guid.Empty);
        await Assert.That(source.HttpTraceId).IsNotNullOrEmpty();
        await Assert.That(source.SourceSiloAddress).IsNotNullOrEmpty();
        await Assert.That(source.TargetSiloAddress).IsNotNullOrEmpty();
        await Assert.That(SiloAddress.FromParsableString(source.SourceSiloAddress).ToParsableString()).IsEqualTo(source.SourceSiloAddress);
        await Assert.That(SiloAddress.FromParsableString(source.TargetSiloAddress).ToParsableString()).IsEqualTo(source.TargetSiloAddress);
        await Assert.That(source.SourceSiloAddress).IsNotEqualTo(source.TargetSiloAddress);
        await Assert.That(source.Stage).IsEqualTo(NativeDiscoveryOmissionProtocol.SourceStage);
        await Assert.That(source.RuntimeJournalReaderContract).IsNull();
        await Assert.That(source.ProducerVoter).IsNull();
        await Assert.That(omitted).IsEqualTo(source with
        {
            Stage = NativeDiscoveryOmissionProtocol.OmittedStage,
            ProducerVoter = arm.TargetVoter,
            RuntimeJournalReaderContract = KeyLoad.Storage.StoreReaderContract.Unspecified
        });
        await Assert.That(verified).IsEqualTo(source with
        {
            Stage = NativeDiscoveryOmissionProtocol.VerifiedStage,
            ProducerVoter = arm.TargetVoter,
            RuntimeJournalReaderContract = KeyLoad.Storage.StoreReaderContract.Unspecified
        });
    }
    private NativeDiscoveryOmissionWitness Read(string name)
    {
        var bytes = RequestCqrsProbeFiles.ReadRecord(Path.Combine(Root, name), IntegrationRoutingOptions.ProbeExecution());
        originals.Add(name, bytes);
        return json.ReadWitness(bytes);
    }
    internal async Task RepairAfterOwnersJoinedAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        RequireStopped(wave);
        await PreserveEvidenceAsync(token).ConfigureAwait(false);
        foreach (var (name, bytes) in originals)
        {
            RequestCqrsProbeFileStore.DeleteExactFile(Root, name, bytes);
        }
        var publication = Path.Combine(Root, NativeDiscoveryOmissionProtocol.LockFile);
        if (File.Exists(publication))
        {
            NodeEpochRf3OfflineFiles.AssertExclusive(publication);
            if (KeyLoad.Storage.IO.OfflineRegularFile.Inspect(publication).Length != NativeDiscoveryOmissionProtocol.EmptyCount)
            {
                throw new InvalidOperationException(NativeDiscoveryOmissionProtocol.Invalid);
            }
            File.Delete(publication);
        }
        disarmed = true;
    }
    internal async Task DisposeAfterOwnersJoinedAsync(TwoRf3MembershipWave wave)
    {
        RequireStopped(wave);
        if (!disarmed)
        {
            retained = true;
            owners.RetainEvidence();
            wave.RetainRoots();
            await PreserveEvidenceAsync(originalToken).ConfigureAwait(false);
        }
        if (!retained)
        {
            await owners.DisposeAfterResourcesJoinedAsync().ConfigureAwait(false);
        }
    }
    private async Task PreserveEvidenceAsync(CancellationToken token)
    {
        var evidence = Path.Combine(ClusterFixtureDiagnostics.FindRepositoryRoot().FullName,
            "artifacts", "qualification", "native-capability-omission-" + SessionId);
        var before = await NodeEpochRf3Inventory.CaptureAsync(Root, token).ConfigureAwait(false);
        await NodeEpochRf3Inventory.CopyTreeAsync(Root, evidence, token).ConfigureAwait(false);
        var copy = await NodeEpochRf3Inventory.CaptureAsync(evidence, token).ConfigureAwait(false);
        await Assert.That(before.Equivalent(copy)).IsTrue();
    }
    private static void RequireStopped(TwoRf3MembershipWave wave)
    {
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var root = Path.Combine(wave.OwnedDataRoot, node);
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "node.owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "database", "owner.lock"));
            NodeEpochRf3OfflineFiles.AssertExclusive(Path.Combine(root, "replica", "owner.lock"));
        }
    }
}
