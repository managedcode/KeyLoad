using KeyLoad.Core.Features.ResourceExecution;
using P = KeyLoad.UnitTests.Features.Authorization.SafeProjectorOperationProtocol;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class ResourceLifetimeLedgerNativeAssertions
{
    internal static async Task RetainedAsync(ResourceLifetimeWorkResultRetention ledger,
        params ResourceLifetimeV1[] expected)
    {
        await Assert.That(ledger.RetainedEntries).IsEqualTo(expected.Length);
        var bytes = expected.Sum(lifetime => NativeSerialization.Measure(lifetime));
        await Assert.That(ledger.RetainedEncodedBytes).IsEqualTo(bytes);
    }

    internal static async Task HealthyChildAsync(SafeProjectorOperationDatabase owner,
        ResourceLifetimeLedgerNativeChild.Observation result)
    {
        await Assert.That(result.Error).IsNull();
        await SafeProjectorOperationSeed.EqualAsync(result.Page, ResourceLifetimeLedgerNativeData.Expected(owner));
    }

    internal static async Task RefusedAsync(ResourceLifetimeLedgerNativeChild.Observation actual, ErrorCode code)
    {
        await Assert.That(actual.Page).IsNull();
        await Assert.That(actual.Error).IsTypeOf<KeyLoadException>();
        await Assert.That(((KeyLoadException)actual.Error!).Code).IsEqualTo(code);
        await Assert.That(actual.Error.Message.Contains(P.Canary, StringComparison.Ordinal)).IsFalse();
    }
}
