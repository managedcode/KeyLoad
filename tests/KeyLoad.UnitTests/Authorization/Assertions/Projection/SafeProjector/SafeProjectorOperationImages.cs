using KeyLoad.Server;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class SafeProjectorOperationImages
{
    internal static async Task ObserveAsync(SafeProjectorOperationDatabase owner, Func<Task> operation)
    {
        var rows = TeamRowPolicyAssertions.Image(owner.Store);
        var cut = owner.Store.Position;
        var applied = owner.Database.LastApplied;
        var journal = owner.ReadCanonicalCut();
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(operation, failures);
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await TeamRowPolicyAssertions.UnchangedAsync(owner.Store, rows, cut);
            await Assert.That(owner.Database.LastApplied).IsEqualTo(applied);
            await Assert.That(owner.ReadCanonicalCut()).IsEqualTo(journal);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static string[] Without(SafeProjectorOperationDatabase owner, params byte[][] excluded)
    {
        const string KeyValueSeparator = ":";
        var keys = excluded.Select(key => Convert.ToHexString(key) + KeyValueSeparator).ToArray();
        return TeamRowPolicyAssertions.Image(owner.Store).Where(row =>
            !keys.Any(key => row.StartsWith(key, StringComparison.Ordinal))).ToArray();
    }
}
