using KeyLoad.Core.ResourceExecution;
using P = KeyLoad.FunctionalTests.Authorization.SafeProjectorOperationProtocol;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class ResourceLifetimeLedgerAssertions
{
    internal static async Task RetainedAsync(ResourceLifetimeWorkResultRetention ledger,
        params ResourceLifetime[] expected)
    {
        await Assert.That(ledger.RetainedEntries).IsEqualTo(expected.Length);
        var bytes = expected.Sum(lifetime => OrleansSerialization.Measure(lifetime));
        await Assert.That(ledger.RetainedEncodedBytes).IsEqualTo(bytes);
    }

    internal static async Task HealthyChildAsync(SafeProjectorOperationDatabase owner,
        ResourceLifetimeLedgerChild.Observation result)
    {
        await Assert.That(result.Error).IsNull();
        await SafeProjectorOperationSeed.EqualAsync(result.Page, ResourceLifetimeLedgerData.Expected(owner));
    }

    internal static async Task RepeatedIdentityAsync(ResourceLifetimeWorkResultRetention ledger, ResourceLifetime lifetime)
    {
        var bytes = checked((int)ledger.RetainedEncodedBytes);
        ledger.Reserve([lifetime, lifetime with { }], bytes);
        await RetainedAsync(ledger, lifetime);
        var capacity = Assert.ThrowsExactly<KeyLoadException>(() => ledger.Reserve([lifetime], bytes - 1));
        await Assert.That(capacity.Code).IsEqualTo(ErrorCode.BudgetExceeded);
        var changed = lifetime with { ResourceName = lifetime.ResourceName + "-altered" };
        var identity = Assert.ThrowsExactly<KeyLoadException>(() => ledger.Reserve([changed]));
        await Assert.That(identity.Code).IsEqualTo(ErrorCode.Corruption);
        var absent = Assert.ThrowsExactly<KeyLoadException>(() => ledger.Reserve([null!]));
        await Assert.That(absent.Code).IsEqualTo(ErrorCode.Corruption);
        await RetainedAsync(ledger, lifetime);
    }

    internal static async Task RefusedAsync(ResourceLifetimeLedgerChild.Observation actual, ErrorCode code)
    {
        await Assert.That(actual.Page).IsNull();
        await Assert.That(actual.Error).IsTypeOf<KeyLoadException>();
        await Assert.That(((KeyLoadException)actual.Error!).Code).IsEqualTo(code);
        await Assert.That(actual.Error.Message.Contains(P.Canary, StringComparison.Ordinal)).IsFalse();
    }
}
