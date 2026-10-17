using KeyLoad.Server.Features.Search;
using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.Search;

internal static class ClassificationLineageWholeFlow
{
    private const string NativeDirectory = "classification-lineage-native-text";

    internal static async Task RunAsync()
    {
        var fixture = new EventProjectionFixture();
        try
        {
            await RunOwnedAsync(fixture);
        }
        catch (Exception primary)
        {
            try
            {
                fixture.Dispose();
            }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        fixture.Dispose();
    }

    private static async Task RunOwnedAsync(EventProjectionFixture fixture)
    {
        var request = fixture.Request();
        var commandId = Guid.NewGuid();
        var original = fixture.Apply(request, commandId);
        await ClassificationLineageWholeAssertions.ReceiptAsync(fixture, original);
        var vector = fixture.VectorBytes() ?? throw new InvalidOperationException("The admitted vector is missing.");
        var lineage = fixture.Harness.Store.Read(view => view.ReadOwnedValue(fixture.LineageKey()))
            ?? throw new InvalidOperationException("The admitted lineage is missing.");
        await WithProviderAsync(fixture, async projection =>
        {
            await ClassificationLineageWholeAssertions.HealthyAsync(fixture, projection);
            fixture.ConfigureReader([EventProjectionFixture.InputUse, EventProjectionFixture.VectorUse], revoked: true);
            await ClassificationLineageWholeAssertions.RefusedAsync(fixture, projection);
            fixture.ConfigureReader([EventProjectionFixture.InputUse, EventProjectionFixture.VectorUse]);
            await ClassificationLineageWholeAssertions.HealthyAsync(fixture, projection);
        });
        var beforeCold = QueueWholeFlowStorage.Bytes(fixture.Harness.Store);
        var cut = fixture.Harness.Store.Position;
        fixture.Harness.Reopen();
        await ClassificationLineageWholeAssertions.UnchangedAsync(fixture, beforeCold, cut);
        await WithProviderAsync(fixture, projection => ClassificationLineageWholeAssertions.HealthyAsync(fixture, projection));
        await ClassificationLineageWholeAssertions.RetainedAsync(fixture, vector, lineage);
        var beforeReplay = QueueWholeFlowStorage.Bytes(fixture.Harness.Store);
        var replayCut = fixture.Harness.Store.Position;
        await ClassificationLineageWholeAssertions.ReplayReceiptAsync(fixture.Apply(request, commandId), original);
        await ClassificationLineageWholeAssertions.UnchangedAsync(fixture, beforeReplay, replayCut);
        await ClassificationLineageWholeAssertions.RetainedAsync(fixture, vector, lineage);
    }

    private static async Task WithProviderAsync(EventProjectionFixture fixture, Func<NativeTextProjection, Task> action)
    {
        var projection = new NativeTextProjection(Path.Combine(fixture.Harness.DirectoryPath, NativeDirectory),
            UnitExecutionOptions.DatabaseLimits(fixture.Database.Limits), fixture.Harness.Store.Identity.NodeId,
            UnitNativeTextOptions.Execution());
        try
        {
            await action(projection);
        }
        catch (Exception primary)
        {
            try
            {
                projection.Dispose();
            }
            catch (Exception cleanup) { throw new AggregateException(primary, cleanup); }
            throw;
        }
        projection.Dispose();
    }
}
