using KeyLoad.UnitTests.Features.Messaging;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceIncarnationRefusals
{
    internal static async Task RunAsync(TestDatabase fixture, ResourceIncarnationState state, CancellationToken token)
    {
        var stale = ResourceIncarnationLiteral.Configure(fixture.Database, fixture.Partition, state.Definition with
        {
            SchemaVersion = 3
        }, 1);
        var failure = fixture.Database.Apply(stale);
        await Assert.That(Errors.Code(failure.Problem)).IsEqualTo(ErrorCode.RevisionConflict);
        state.Outcomes.Add((stale, failure));
        await ResourceIncarnationAssertions.PreserveAsync(fixture.Store, fixture.Partition, state);
        await ResourceIncarnationAssertions.RowAsync(fixture.Database, fixture.Partition, state.Lifetime, 2);
        var collision = fixture.Database.CreateCommand(state.Original.Value!.Kind, state.Original.CommandId,
            state.Original.UserId!, fixture.Database.EvaluationClock.GetUtcNow(),
            ResourceIncarnationLiteral.CreationJson(state.Original).Replace(
                ResourceIncarnationLiteral.Table, "identity-collision", StringComparison.Ordinal));
        var conflict = fixture.Database.Apply(collision);
        await Assert.That(Errors.Code(conflict.Problem)).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(fixture.Store.Read(view => view.ReadOwnedValue(ResourceIncarnationLiteral.LifetimeKey(
            fixture.Partition, "identity-collision")))).IsNull();
        var duplicate = fixture.Database.CreateCommand(state.Original.Value.Kind, Guid.NewGuid(), state.Original.UserId!,
            fixture.Database.EvaluationClock.GetUtcNow(), ResourceIncarnationLiteral.CreationJson(state.Original));
        var duplicateResult = fixture.Database.ApplyEmbedded(duplicate, token);
        await Assert.That(Errors.Code(duplicateResult.Problem)).IsEqualTo(state.Original.Value.Kind == OperationKind.CreateTable
            ? ErrorCode.Conflict : ErrorCode.UnsupportedCapability);
        state.Outcomes.Add((duplicate, duplicateResult));
        await PrincipalAsync(fixture, state, token);
        await CorruptAsync(fixture, state, null);
        await CorruptAsync(fixture, state, [1, 2, 3]);
        await CorruptAsync(fixture, state, OrleansSerialization.Serialize(state.Lifetime with
        {
            ResourceName = "alien"
        }));
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(token);
        await caller.CancelAsync();
        var cancelled = ResourceIncarnationLiteral.Configure(fixture.Database, fixture.Partition, state.Definition);
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var error = Assert.ThrowsExactly<OperationCanceledException>(() => fixture.Database.ApplyEmbedded(cancelled, caller.Token));
        await Assert.That(error!.CancellationToken).IsEqualTo(caller.Token);
        await ResourceIncarnationAssertions.UnchangedAsync(fixture.Store, image, position);
        var healthy = fixture.Database.Apply(cancelled);
        await ResourceIncarnationAssertions.EqualAsync(state.Definition, healthy.Get<ResourceDefinition>());
        state.Outcomes.Add((cancelled, healthy));
        await ResourceIncarnationAssertions.PreserveAsync(fixture.Store, fixture.Partition, state);
    }

    private static async Task PrincipalAsync(TestDatabase fixture, ResourceIncarnationState state, CancellationToken token)
    {
        var principal = new PrincipalRecord("identity-admin", fixture.Partition.TenantId,
            [new("*", "*", Capability.All)], ["*"])
        {
            ClusterAdministrator = true
        };
        fixture.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
        var original = fixture.Database.CreateCommand(state.Original.Value!.Kind, state.Original.CommandId, principal.Id,
            fixture.Database.EvaluationClock.GetUtcNow(), ResourceIncarnationLiteral.CreationJson(state.Original));
        var result = fixture.Database.ApplyEmbedded(original, token);
        await Assert.That(Errors.Code(result.Problem)).IsEqualTo(state.Original.Value.Kind == OperationKind.CreateTable
            ? ErrorCode.Conflict : ErrorCode.UnsupportedCapability);
        state.Outcomes.Add((original, result));
        await ResourceIncarnationAssertions.PreserveAsync(fixture.Store, fixture.Partition, state);
    }

    private static async Task CorruptAsync(TestDatabase fixture, ResourceIncarnationState state, byte[]? corrupted)
    {
        var operation = ResourceIncarnationLiteral.Configure(fixture.Database, fixture.Partition, state.Definition);
        var key = ResourceIncarnationLiteral.LifetimeKey(fixture.Partition);
        fixture.Store.Commit((tx, _) => { if (corrupted is null) { tx.Delete(key); } else { tx.Put(key, corrupted); } return true; });
        var image = QueueWholeFlowStorage.Bytes(fixture.Store);
        var position = fixture.Store.Position;
        var error = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.Apply(operation));
        await Assert.That(error!.Code).IsEqualTo(ErrorCode.Corruption);
        await ResourceIncarnationAssertions.UnchangedAsync(fixture.Store, image, position);
        fixture.Store.Commit((tx, _) => { tx.Put(key, state.LifetimeBytes); return true; });
        var result = fixture.Database.Apply(operation);
        await ResourceIncarnationAssertions.EqualAsync(state.Definition, result.Get<ResourceDefinition>());
        state.Outcomes.Add((operation, result));
        await ResourceIncarnationAssertions.PreserveAsync(fixture.Store, fixture.Partition, state);
        await ResourceIncarnationAssertions.RowAsync(fixture.Database, fixture.Partition, state.Lifetime, 2);
    }
}
