using KeyLoad.Server.Features.ClusterRouting;
using P = KeyLoad.IntegrationTests.Features.ClusterRouting.ControlledRetireAspireProtocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class ControlledRetireAspireConservation
{
    internal static async Task RequireAsync(IReadOnlyDictionary<string, NativeRetireCutWitness> before,
        IReadOnlyDictionary<string, NativeRetireCutWitness> after)
    {
        await Assert.That(before.Count).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
        await Assert.That(after.Count).IsEqualTo(TwoRf3MembershipProtocol.NodeCount);
        foreach (var node in TwoRf3MembershipProtocol.Nodes)
        {
            var expected = before[node];
            var actual = after[node];
            await Assert.That(actual.Owner).IsEqualTo(expected.Owner);
            await Assert.That(actual.CommittedIndex).IsEqualTo(expected.CommittedIndex);
            await Assert.That((actual.ReceiptEntry is null) == (expected.ReceiptEntry is null)).IsTrue();
            if (actual.ReceiptEntry is { } installed && expected.ReceiptEntry is { } originalEntry)
            {
                await Assert.That(NativeSerialization.Serialize(installed).AsSpan()
                    .SequenceEqual(NativeSerialization.Serialize(originalEntry))).IsTrue();
            }
            await Assert.That(actual.Position).IsEqualTo(expected.Position);
            await Assert.That(actual.ReplicaTail).IsEqualTo(expected.ReplicaTail);
            await Assert.That(JsonDefaults.Serialize(actual.Identity).AsSpan()
                .SequenceEqual(JsonDefaults.Serialize(expected.Identity))).IsTrue();
            await Assert.That(actual.SigningKeySha256).IsEqualTo(expected.SigningKeySha256);
            await Assert.That(actual.Rows.Length).IsEqualTo(expected.Rows.Length);
            for (var index = P.First; index < expected.Rows.Length; index++)
            {
                await Assert.That(actual.Rows[index].Key.Span.SequenceEqual(expected.Rows[index].Key.Span)).IsTrue();
                await Assert.That(actual.Rows[index].Value.Span.SequenceEqual(expected.Rows[index].Value.Span)).IsTrue();
            }
            await Assert.That((actual.OriginalSealedOperation is null) == (expected.OriginalSealedOperation is null)).IsTrue();
            if (actual.OriginalSealedOperation is { } retained && expected.OriginalSealedOperation is { } original)
            {
                await Assert.That(retained.Span.SequenceEqual(original.Span)).IsTrue();
            }
        }
    }
}
