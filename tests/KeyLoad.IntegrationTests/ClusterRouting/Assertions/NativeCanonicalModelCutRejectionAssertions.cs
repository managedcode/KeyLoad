using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeCanonicalModelCutRejectionAssertions
{
    private const string MissingRealData = "The native cut refusal checks require two actual distinct owners and nonempty genuine canonical data.";
    private const byte ChangedBit = 1;

    internal static async Task RequireAsync(IReadOnlyDictionary<string, NativeCanonicalModelCutWitness> actualSix,
        CancellationToken token)
    {
        if (actualSix.Count != TwoRf3MembershipProtocol.NodeCount)
        {
            throw new InvalidDataException(MissingRealData);
        }
        var original = actualSix.Values.FirstOrDefault(cut => cut.Rows.Any(row => !row.Value.IsEmpty))
            ?? throw new InvalidDataException(MissingRealData);
        var other = actualSix.Values.First(cut => cut.Owner.VoterId != original.Owner.VoterId);
        NativeCanonicalModelCutAssertions.Require(original.Request, original.Owner, original, token);
        var originalBytes = NativeCanonicalModelCutCodec.Encode(original, token);
        var roundTrip = NativeCanonicalModelCutCodec.Decode(originalBytes, token);
        NativeCanonicalModelCutAssertions.Require(original.Request, original.Owner, roundTrip, token);
        NativeCanonicalModelCutAssertions.RequireUnchanged(original, roundTrip, token);

        _ = Assert.ThrowsExactly<InvalidDataException>(() => NativeCanonicalModelCutAssertions.Require(
            original.Request, other.Owner, original, token));
        var changedNonce = original with
        {
            Request = original.Request with
            {
                Nonce = Guid.NewGuid()
            }
        };
        _ = Assert.ThrowsExactly<InvalidDataException>(() => NativeCanonicalModelCutAssertions.Require(
            original.Request, original.Owner, changedNonce, token));
        var rowIndex = Enumerable.Range(0, original.Rows.Length).First(index => !original.Rows[index].Value.IsEmpty);
        var row = original.Rows[rowIndex];
        var changed = row.Value.ToArray();
        changed[0] ^= ChangedBit;
        var changedRows = original.Rows.SetItem(rowIndex, new(row.Key, changed));
        _ = Assert.ThrowsExactly<InvalidDataException>(() => NativeCanonicalModelCutAssertions.RequireUnchanged(
            original, original with
            {
                Rows = changedRows
            }, token));
        _ = Assert.ThrowsExactly<InvalidDataException>(() => NativeCanonicalModelCutAssertions.RequireUnchanged(
            original, original with
            {
                Rows = original.Rows.RemoveAt(rowIndex)
            }, token));
        var duplicated = original with
        {
            Rows = original.Rows.Add(row)
        };
        RequireDuplicateRefusal(() => NativeCanonicalModelCutAssertions.RequireUnchanged(original, duplicated, token));
        await Assert.That(NativeCanonicalModelCutCodec.Encode(original, token).SequenceEqual(originalBytes)).IsTrue();
    }
    private static void RequireDuplicateRefusal(Action operation)
    {
        try
        {
            operation();
        }
        catch (InvalidDataException) { return; }
        catch (KeyLoadException error) when (error.Code == ErrorCode.ResourceExhausted) { return; }
        throw new InvalidOperationException(MissingRealData);
    }
}
