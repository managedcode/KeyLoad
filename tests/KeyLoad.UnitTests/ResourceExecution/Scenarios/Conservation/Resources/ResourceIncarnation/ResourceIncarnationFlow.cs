using ManagedCode.Communication.Commands;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class ResourceIncarnationFlow
{
    internal static async Task RunAsync(TestDatabase fixture, bool sql, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var original = ResourceIncarnationLiteral.Creation(fixture, sql, token);
        var fingerprint = original.Value!.Fingerprint;
        var native = original.Value.Input.ToArray();
        var created = fixture.Database.Apply(original);
        var definition = ResourceIncarnationLiteral.Definition(sql);
        await ResourceIncarnationAssertions.EqualAsync(definition, created.Get<ResourceDefinition>());
        await ResourceIncarnationAssertions.IdentityAsync(fixture.Store, fixture.Partition, original, definition, sql);
        await AgreementAsync(fixture, original, definition, token);
        await PrincipalCreationAsync(fixture, original, definition, token);
        await ResourceIncarnationAssertions.WriteAsync(fixture.Database, fixture.Partition, 1);
        var lifetime = ResourceIncarnationAssertions.Lifetime(fixture.Store, fixture.Partition);
        var bytes = ResourceIncarnationAssertions.Bytes(fixture.Store, ResourceIncarnationLiteral.LifetimeKey(fixture.Partition));
        var creationOutcome = ResourceIncarnationAssertions.Bytes(fixture.Store, KeySpace.GlobalOutcome(original.UserId!, original.CommandId));
        var replacement = definition with
        {
            SchemaVersion = 2,
            FieldPolicies = [new("/secret", "confidential")]
        };
        var updated = ResourceIncarnationLiteral.Configure(fixture.Database, fixture.Partition, replacement, 1);
        var outcome = fixture.Database.Apply(updated);
        await ResourceIncarnationAssertions.EqualAsync(replacement, outcome.Get<ResourceDefinition>());
        var state = new ResourceIncarnationState(original, created, replacement, lifetime, bytes, creationOutcome, updated, outcome,
            [(original, created), (updated, outcome)]);
        await ResourceIncarnationAssertions.PreserveAsync(fixture.Store, fixture.Partition, state);
        await ResourceIncarnationAssertions.WriteAsync(fixture.Database, fixture.Partition, 2);
        await ResourceIncarnationRefusals.RunAsync(fixture, state, token);
        await ResourceIncarnationAssertions.ReplayAsync(fixture.Database, fixture.Store, state);
        await Assert.That(original.Value.Fingerprint).IsEqualTo(fingerprint);
        await Assert.That(original.Value.Input.ToArray()).IsEquivalentTo(native,
            global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await ResourceIncarnationCold.RunAsync(fixture, state, token);
    }

    private static async Task PrincipalCreationAsync(TestDatabase first, Command<DatabaseCommand> original,
        ResourceDefinition definition, CancellationToken token)
        => await ResourceIncarnationOwner.RunAsync(async second =>
        {
            token.ThrowIfCancellationRequested();
            var principal = new PrincipalRecord("identity-admin", second.Partition.TenantId,
                [new("*", "*", Capability.All)], ["*"])
            {
                ClusterAdministrator = true
            };
            second.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal)).Get<PrincipalRecord>();
            var independent = second.Database.CreateCommand(original.Value!.Kind, original.CommandId, principal.Id,
                second.Database.EvaluationClock.GetUtcNow(), ResourceIncarnationLiteral.CreationJson(original));
            await ResourceIncarnationAssertions.EqualAsync(definition, second.Database.ApplyEmbedded(independent, token).Get<ResourceDefinition>());
            var left = ResourceIncarnationAssertions.Lifetime(first.Store, first.Partition);
            var right = ResourceIncarnationAssertions.Lifetime(second.Store, second.Partition);
            await Assert.That(right.CreationFingerprint).IsNotEqualTo(left.CreationFingerprint);
            await Assert.That(right.Incarnation.Value).IsNotEqualTo(left.Incarnation.Value);
            await ResourceIncarnationAssertions.WriteAsync(second.Database, second.Partition, 1);
        });

    private static async Task AgreementAsync(TestDatabase originalOwner, Command<DatabaseCommand> original,
        ResourceDefinition definition, CancellationToken token)
        => await ResourceIncarnationOwner.RunAsync(async replica =>
        {
            token.ThrowIfCancellationRequested();
            await Assert.That(replica.Store.Identity.NodeId).IsNotEqualTo(originalOwner.Store.Identity.NodeId);
            var local = replica.Database.CreateCommand(original.Value!.Kind, original.CommandId, original.UserId!,
                replica.Database.EvaluationClock.GetUtcNow(), ResourceIncarnationLiteral.CreationJson(original));
            await ResourceIncarnationAssertions.EqualAsync(definition, replica.Database.ApplyEmbedded(local, token).Get<ResourceDefinition>());
            await Assert.That(ResourceIncarnationAssertions.Bytes(replica.Store, ResourceIncarnationLiteral.LifetimeKey(replica.Partition)))
                .IsEquivalentTo(ResourceIncarnationAssertions.Bytes(originalOwner.Store, ResourceIncarnationLiteral.LifetimeKey(originalOwner.Partition)),
                    global::TUnit.Assertions.Enums.CollectionOrdering.Matching);
            await ResourceIncarnationAssertions.WriteAsync(replica.Database, replica.Partition, 1);
        });
}
