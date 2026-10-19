using KeyLoad.Core.Features.ResourceExecution;
using P = KeyLoad.UnitTests.Features.Authorization.SafeProjectorOperationProtocol;

namespace KeyLoad.UnitTests.Features.Authorization;

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

    internal static async Task RefusedAsync(ResourceLifetimeLedgerChild.Observation actual, ErrorCode code)
    {
        await Assert.That(actual.Page).IsNull();
        await Assert.That(actual.Error).IsTypeOf<KeyLoadException>();
        await Assert.That(((KeyLoadException)actual.Error!).Code).IsEqualTo(code);
        await Assert.That(actual.Error.Message.Contains(P.Canary, StringComparison.Ordinal)).IsFalse();
    }
}
