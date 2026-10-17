using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal sealed class RequestCqrsNativeModelInventoryRefusalTests
{
    private const string UnexpectedAuthorityKey = "UnexpectedNativeAuthority";

    [Test]
    public void OriginalObservedMarkerBindsSessionArmRequestCommandPhaseAndSender()
    {
        using var fixture = new RequestCqrsNativeModelWitnessCase();
        var original = fixture.Create();
        var marker = RequestCqrsNativeModelWitnessCase.Marker(original);
        var changed = new[]
        {
            marker with { SessionId = Guid.NewGuid().ToString("N") }, marker with { ArmId = Guid.NewGuid() },
            marker with { RequestId = Guid.NewGuid() }, marker with { CommandId = Guid.NewGuid() },
            marker with { Phase = RequestCqrsProbePhase.NativeModelBeforeDispatch },
            marker with { Voter = marker.Voter + "-changed" }, marker with { SiloAddress = marker.SiloAddress + "-changed" },
            marker with { Outcome = RequestCqrsProbeOutcome.Released }
        };
        foreach (var candidate in changed)
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => RequestCqrsNativeModelInventory.RequireMarkers([original], [candidate]));
        }
        Assert.ThrowsExactly<InvalidOperationException>(() => RequestCqrsNativeModelInventory.RequireMarkers([original], []));
        RequestCqrsNativeModelInventory.RequireMarkers([original], [marker]);
    }

    [Test]
    public void EachRequiredFieldMalformedAndUnknownJsonRefusesThenHealthyDecodes()
    {
        using var fixture = new RequestCqrsNativeModelWitnessCase();
        var original = RequestCqrsNativeModelInventory.Write(fixture.Create());
        var node = JsonNode.Parse(original)!.AsObject();
        foreach (var key in node.Select(item => item.Key).ToArray())
        {
            var changed = node.DeepClone().AsObject();
            changed.Remove(key);
            var bytes = RequestCqrsNativeModelWitnessCase.Json(changed.ToJsonString());
            Assert.ThrowsExactly<JsonException>(() => RequestCqrsNativeModelInventory.Read(bytes));
        }
        var unknown = node.DeepClone().AsObject();
        unknown.Add(UnexpectedAuthorityKey, true);
        Assert.ThrowsExactly<JsonException>(() => RequestCqrsNativeModelInventory.Read(RequestCqrsNativeModelWitnessCase.Json(unknown.ToJsonString())));
        Assert.ThrowsExactly<JsonException>(() => RequestCqrsNativeModelInventory.Read(RequestCqrsNativeModelWitnessCase.Json("{")));
        _ = RequestCqrsNativeModelInventory.Read(original);
    }

    [Test]
    public async Task ExactOriginalRecordLimitAndCanonicalWitnessNamesAreEnforced()
    {
        using var fixture = new RequestCqrsNativeModelWitnessCase();
        var witness = fixture.Create();
        var bytes = RequestCqrsNativeModelInventory.Write(witness);
        var maximum = new RequestProbeExecutionOptions().MaximumRecordBytes;
        await Assert.That(maximum).IsEqualTo(8192);
        var exact = Enumerable.Repeat((byte)' ', maximum).ToArray();
        bytes.CopyTo(exact, 0);
        _ = RequestCqrsNativeModelInventory.Read(exact);
        Assert.ThrowsExactly<InvalidOperationException>(() => RequestCqrsNativeModelInventory.Read(new byte[maximum + 1]));
        Assert.ThrowsExactly<InvalidOperationException>(() => RequestCqrsNativeModelInventory.Read([]));
        var name = RequestCqrsNativeModelInventory.Name(witness.ArmId, witness.RequestId, witness.Phase);
        await Assert.That(RequestCqrsNativeModelInventory.IsName(name)).IsTrue();
        foreach (var bad in new[] { "../" + name, name + ".tmp", name.Replace(witness.ArmId.ToString("N"), Guid.Empty.ToString("N"), StringComparison.Ordinal),
            name.Replace(witness.Phase.ToString(), nameof(RequestCqrsProbePhase.RequestStarted), StringComparison.Ordinal), name.Replace(witness.RequestId.ToString("N"), witness.RequestId.ToString("D"), StringComparison.Ordinal) })
        {
            await Assert.That(RequestCqrsNativeModelInventory.IsName(bad)).IsFalse();
        }
    }
}
