using KeyLoad.Core;
using KeyLoad.CrashHost.Features.EventStreams;
using KeyLoad.Storage;
using static KeyLoad.CrashHost.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.RecoveryTests.Features.EventStreams;

internal static class SharedDomainRecoveryHistoryAssertions
{
    private const string Stale = "The delivery lease is stale.";

    internal static async Task AllAsync(DatabaseEngine database, IAtomicStore store, IEnumerable<SharedDomainRecoveryHistory> history)
    {
        foreach (var entry in history)
        {
            var rows = SharedDomainStoredImage.Capture(store);
            var position = store.Position;
            var outcome = OutcomeStoreOracle.Read(store, entry.Operation) ?? throw new InvalidOperationException(MissingNative);
            await SharedDomainRecoveryValues.ResultAsync(outcome, entry.Result);
            if (entry.Operation.Kind == OperationKind.Receive)
            {
                var refused = database.ResolveOutcome(entry.Operation);
                await Assert.That(refused.Error).IsEqualTo(ErrorCode.StaleLease);
                await Assert.That(refused.SafeDetail).IsEqualTo(Stale);
                await Assert.That(refused.Json).IsNull();
                await Assert.That(refused.NativeValue).IsNull();
            }
            else
            {
                await SharedDomainRecoveryValues.ResultAsync(database.Apply(entry.Operation), entry.Result);
                await SharedDomainRecoveryValues.ResultAsync(database.ResolveOutcome(entry.Operation), entry.Result);
            }
            await Assert.That(SharedDomainRecoverySeed.Required(store, entry.OutcomeKey).AsSpan().SequenceEqual(entry.OutcomeBytes)).IsTrue();
            await Assert.That(SharedDomainRecoverySeed.Required(store, entry.LocatorKey).AsSpan().SequenceEqual(entry.LocatorBytes)).IsTrue();
            await Assert.That(entry.LocatorBytes.AsSpan().SequenceEqual(entry.OutcomeKey)).IsTrue();
            await SharedDomainRecoveryImage.SameAsync(store, rows, position);
        }
    }
}
