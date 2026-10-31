using KeyLoad.Core.ResourceExecution;
using P = KeyLoad.FunctionalTests.Authorization.SafeProjectorOperationProtocol;

namespace KeyLoad.FunctionalTests.Authorization;

internal static class ResourceLifetimeLedgerAssertions
{
    internal static async Task RetainedAsync(ResourceLifetimeWorkResultRetention ledger,
        params ResourceLifetime[] expected)
    {
        await Assert.That(ledger.RetainedEntries).IsEqualTo(expected.Length);
    }

    internal static async Task HealthyChildAsync(SafeProjectorOperationDatabase owner,
        ResourceLifetimeLedgerChild.Observation result)
    {
        await Assert.That(result.Error).IsNull();
        await SafeProjectorOperationSeed.EqualAsync(result.Page, ResourceLifetimeLedgerData.Expected(owner));
    }

    internal static async Task RepeatedIdentityAsync(ResourceLifetimeWorkResultRetention ledger, ResourceLifetime lifetime)
    {
        ledger.Reserve([lifetime, lifetime with { }]);
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
