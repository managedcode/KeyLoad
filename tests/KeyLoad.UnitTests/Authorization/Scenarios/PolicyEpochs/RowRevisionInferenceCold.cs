using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.Authorization.RowRevisionInferenceProtocol;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class RowRevisionInferenceCold
{
    internal static async Task RunAsync(TestDatabase db,
        List<(Command<DatabaseCommand> Operation, Result<object> Result, byte[] Outcome)> originals, RowRevisionInferenceLifetime lifetime, CancellationToken token)
    {
        var identity = db.Store.Identity;
        var image = TeamRowPolicyAssertions.Image(db.Store);
        var position = db.Store.Position;
        var failures = new List<Exception>();
        lifetime.CloseOriginal(failures);
        for (var cycle = 0; cycle < P.ColdCycles && lifetime.CanOpenCold; cycle++)
        {
            var store = lifetime.OpenCold(identity, failures);
            if (store is null)
            {
                break;
            }
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var database = db.CreateConfiguredDatabase(store);
                await Assert.That(store.Identity.NodeId).IsEqualTo(identity.NodeId);
                await Assert.That(store.Identity.Incarnation).IsEqualTo(identity.Incarnation);
                await Assert.That(store.Identity.ReadGeneration).IsGreaterThanOrEqualTo(identity.ReadGeneration);
                await TeamRowPolicyAssertions.UnchangedAsync(store, image, position);
                foreach (var item in originals)
                {
                    await RowRevisionInferenceAssertions.ReplayAsync(database, store, item.Operation,
                        item.Result, item.Outcome, db.Partition, token);
                }
                await RowRevisionInferenceAssertions.DocumentAsync(database, db.Partition, P.Own,
                    P.OriginalRevision, P.Original, token);
                await TeamRowPolicyAssertions.UnchangedAsync(store, image, position);
            }, failures);
            if (!lifetime.CloseCold(store, failures))
            {
                break;
            }
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
