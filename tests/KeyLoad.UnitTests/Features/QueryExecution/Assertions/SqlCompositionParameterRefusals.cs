using System.Text.Json;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.Features.QueryExecution;

internal static class SqlCompositionParameterRefusals
{
    internal static async Task RunAsync(SqlCompositionParameterFixture fixture, CommandRequest command,
        bool reverse, CancellationToken token)
    {
        var request = SqlCompositionParameterData.Request(command, reverse);
        var missing = Copy(request);
        missing.Remove(SqlCompositionParameterData.QueueParameter);
        var wrong = Copy(request);
        wrong[SqlCompositionParameterData.GraphParameter] = JsonSerializer.SerializeToElement(new[] { "not-a-scalar" }, JsonDefaults.Options);
        var nullValue = Copy(request);
        nullValue[SqlCompositionParameterData.GraphParameter] = JsonSerializer.SerializeToElement<string?>(null, JsonDefaults.Options);
        var casing = Copy(request);
        casing.Remove(SqlCompositionParameterData.GraphParameter);
        casing.Add(SqlCompositionParameterData.WrongCaseGraphParameter, request.Parameters![SqlCompositionParameterData.GraphParameter]);
        var unused = Copy(request);
        unused.Add(SqlCompositionParameterData.UnusedParameter, JsonSerializer.SerializeToElement("private-unused-parameter", JsonDefaults.Options));
        foreach (var parameters in new[] { missing, wrong, nullValue, casing, unused })
        {
            await OneAsync(fixture, request with
            {
                Parameters = parameters
            }, token);
        }
        if (reverse)
        {
            var entity = Copy(request);
            entity[SqlCompositionParameterData.StartParameter] = JsonSerializer.SerializeToElement("not-an-entity", JsonDefaults.Options);
            await OneAsync(fixture, request with
            {
                Parameters = entity
            }, token);
        }
    }

    private static Dictionary<string, JsonElement> Copy(SqlOperationRequest request)
        => new(request.Parameters!, StringComparer.OrdinalIgnoreCase);

    private static async Task OneAsync(SqlCompositionParameterFixture fixture, SqlOperationRequest request,
        CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var identity = NativeReadCutStoreStateAssertions.SnapshotIdentity(fixture.Store);
        var parameters = JsonDefaults.Serialize(request);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => SqlOperationTestData.Compile(request,
                fixture.Database.Limits, cancellationToken: token));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(failure.Message).IsEqualTo(SqlCompositionParameterData.Invalid);
            await Assert.That(JsonDefaults.Serialize(request).AsSpan().SequenceEqual(parameters)).IsTrue();
            await SqlCompositionParameterAssertions.ModelsAsync(fixture);
            await SqlCompositionParameterAssertions.OutboxAsync(fixture);
        }, failures);
        await ServerFailureObserver.ObserveAsync(
            () => SqlCompositionParameterAssertions.ConserveAsync(fixture, image, position, identity), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
