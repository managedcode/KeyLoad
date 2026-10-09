using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal sealed class SampleChunkJobRevocationScenario(SampleChunkRf3Scenario window,
    PrincipalRecord creator, string secret)
{
    internal SampleChunkRf3Scenario Window { get; } = window;
    internal PrincipalRecord Creator { get; } = creator;
    internal string Secret { get; } = secret;
    internal CommandRequest? OriginalSeal { get; set; }
    internal CommitReceipt? OriginalReceipt { get; set; }
    internal CommitReceipt? Correction { get; set; }

    internal static async Task<SampleChunkJobRevocationScenario> CreateAsync(KeyLoadClient administrator,
        CancellationToken token)
    {
        var partition = new PartitionRef(SampleChunkJobRevocationProtocol.TenantPrefix + Guid.NewGuid()
            .ToString(SampleChunkJobRevocationProtocol.GuidFormat), SampleChunkJobRevocationProtocol.Database,
            SampleChunkJobRevocationProtocol.Domain, Guid.NewGuid().ToString(SampleChunkJobRevocationProtocol.GuidFormat));
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, new ResourceDefinition(TimeSeriesRf3Scenario.Set,
                ResourceKind.TimeSeries, partition.TransactionDomainId)), token).ConfigureAwait(false));
        var principal = new PrincipalRecord(SampleChunkJobRevocationProtocol.PrincipalPrefix + Guid.NewGuid()
            .ToString(SampleChunkJobRevocationProtocol.GuidFormat), partition.TenantId,
            [new(partition.DatabaseId, TimeSeriesRf3Scenario.Set,
                Capability.SeriesRead | Capability.SeriesAppend | Capability.SeriesManage | Capability.Query)], []);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(
            Guid.NewGuid(), principal, token).ConfigureAwait(false));
        var keyId = SampleChunkJobRevocationProtocol.KeyPrefix + Guid.NewGuid()
            .ToString(SampleChunkJobRevocationProtocol.GuidFormat);
        var secret = keyId + SampleChunkJobRevocationProtocol.Separator + Convert.ToHexStringLower(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(SampleChunkJobRevocationProtocol.SecretBytes));
        var credential = new ApiKeyRecord(keyId, actual.Id, Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret))));
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureApiKeyAsync(
            Guid.NewGuid(), credential, token).ConfigureAwait(false))).IsTrue();
        var from = TimeProvider.System.GetUtcNow();
        var window = new SampleChunkRf3Scenario(new(partition), Guid.NewGuid(), from,
            from.AddHours(SampleChunkRf3Protocol.WindowHours));
        return new(window, actual, secret);
    }

    internal async Task SeedAsync(KeyLoadClient caller, KeyLoadClient administrator, CancellationToken token)
    {
        var window = Window;
        await window.CommitAsync(caller, new OpenSampleChunkWindow(TimeSeriesRf3Scenario.Set,
            TimeSeriesRf3Scenario.Series, window.WindowId, window.From, window.Until), token).ConfigureAwait(false);
        var appended = await window.CommitAsync(caller, new AppendSamples(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
            [window.First, window.Equal], TimeSeriesRf3Scenario.PrivateTags), token).ConfigureAwait(false);
        var original = new CommandRequest(Guid.NewGuid(), window.Native.Partition,
            [new SealSampleChunkWindow(TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series,
                window.WindowId, SampleChunkRf3Protocol.AppendedRevision)]);
        var seal = await McpCallerAssertions.SdkSuccessAsync(await caller.CommitAsync(
            original, token).ConfigureAwait(false));
        var owner = await McpCallerAssertions.SdkSuccessAsync(await administrator.ReadAtomicPartitionPlacementAsync(
            new(SampleChunkRf3Protocol.PlacementVersion, window.Native.Partition), token).ConfigureAwait(false));
        await Assert.That(owner.Partition).IsEqualTo(window.Native.Partition);
        await Assert.That(seal.Token.Position > appended.Token.Position).IsTrue();
        var expected = new CommitReceipt(original.CommandId, new CommitToken(owner.Incarnation,
            window.Native.Partition.AtomicPartitionId, seal.Token.Position, owner.PlacementEpoch),
            [new MutationReceipt(SampleChunkProtocol.SealKind, TimeSeriesRf3Scenario.Set,
                TimeSeriesRf3Scenario.Series, SampleChunkRf3Protocol.SealedRevision)], DurabilityProfile.QuorumProcessDurable);
        await Assert.That(Convert.ToHexString(JsonDefaults.Serialize(seal)))
            .IsEqualTo(Convert.ToHexString(JsonDefaults.Serialize(expected)));
        OriginalSeal = original; OriginalReceipt = seal;
    }
}
