using O = KeyLoad.FunctionalTests.StorageRecovery.PreparationOwnershipOwner;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class PreparationOwnershipAssertions
{
    internal static string[] Image(O owner) => owner.Store.Read(view =>
    {
        var rows = new List<string>();
        var result = view.VisitRange([], view.MaximumRangeRecords, (key, value) =>
        {
            rows.Add(Convert.ToHexString(key) + ":" + Convert.ToHexString(value));
            return true;
        });
        if (result.HasMore)
        {
            throw new InvalidOperationException("The execution preparation fixture image is incomplete.");
        }
        return rows.ToArray();
    });

    internal static async Task ConservedAsync(O owner, long position, byte[] journal, string[] image)
    {
        await Assert.That(owner.Store.Position).IsEqualTo(position);
        await Assert.That(owner.Journal.SequenceEqual(journal)).IsTrue();
        await Assert.That(Image(owner).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
    }

    internal static async Task BytesAsync(byte[] expected, byte[]? actual)
    {
        await Assert.That(actual).IsNotNull();
        await Assert.That(actual is not null && actual.SequenceEqual(expected)).IsTrue();
    }

    internal static void Refuse(Action action, string detail)
    {
        var error = Assert.ThrowsExactly<InvalidOperationException>(action);
        if (error.Message != detail)
        {
            throw new InvalidOperationException("The execution preparation refusal detail changed.", error);
        }
    }

    internal static async Task HealthyAsync(O owner)
    {
        var next = checked(owner.Store.Position + 1);
        var actual = owner.Store.Commit((transaction, position) =>
        {
            transaction.Put(O.ParentKey, O.ParentValue);
            return position;
        });
        await Assert.That(actual).IsEqualTo(next);
        await Assert.That(owner.Store.Position).IsEqualTo(next);
        await BytesAsync(O.ParentValue, owner.Store.Read(view => view.ReadOwnedValue(O.ParentKey)));
        await BytesAsync(O.SeedValue, owner.Store.Read(view => view.ReadOwnedValue(O.SeedKey)));
        await Assert.That(owner.Store.Read(view => view.ReadOwnedValue(O.ChildKey))).IsNull();
    }
}
