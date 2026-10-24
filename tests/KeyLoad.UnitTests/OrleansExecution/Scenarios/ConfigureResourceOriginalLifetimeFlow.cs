using KeyLoad.Clustering;
using KeyLoad.Core.ResourceExecution;
using ManagedCode.Communication.Commands;
using A = KeyLoad.UnitTests.OrleansExecution.ConfigureResourceOriginalLifetimeAssertions;
using L = KeyLoad.UnitTests.RelationalStorage.SqlCreateTableLiteral;
using O = KeyLoad.UnitTests.OrleansExecution.ConfigureResourceOriginalLifetimeOwner;

namespace KeyLoad.UnitTests.OrleansExecution;

internal static class ConfigureResourceOriginalLifetimeFlow
{
    private const string Subject = "configure-execution-admin";
    private const string Resource = "configure-execution-lifetime";

    internal static Task RunAsync(bool table, CancellationToken token) => O.RunAsync(async (fixture, owner) =>
    {
        var administrator = new PrincipalRecord(Subject, fixture.Partition.TenantId, [], [])
        {
            ClusterAdministrator = true,
            PolicyEpoch = 1
        };
        await A.EqualAsync(administrator, fixture.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(administrator)).Get<PrincipalRecord>());
        var initial = table ? L.Definition(fixture.Partition)
            : new ResourceDefinition(Resource, ResourceKind.Collection, fixture.Partition.TransactionDomainId);
        var created = await ConfigureAsync(fixture, owner, initial, null, token, createTable: table);
        var originalBytes = A.Outcome(fixture, created.Operation);
        await ReplayAsync(fixture, owner, created.Operation, initial, created.Lifetime, token);
        var changed = initial with
        {
            SchemaVersion = initial.SchemaVersion + 1,
            FieldPolicies = [new(table ? "/title" : "/secret", "private", "secret-read", "secret-use")]
        };
        var reconfigured = await ConfigureAsync(fixture, owner, changed, initial, token);
        await A.EqualAsync(created.Lifetime, reconfigured.Lifetime);
        await ReplayAsync(fixture, owner, reconfigured.Operation, changed, created.Lifetime, token);
        await ReplayAsync(fixture, owner, created.Operation, initial, created.Lifetime, token);
        foreach (var repaired in new[] { false, true })
        {
            var current = administrator with
            {
                ClusterAdministrator = repaired,
                PolicyEpoch = repaired ? 3 : 2
            };
            await A.EqualAsync(current, fixture.Submit(OperationKind.ConfigurePrincipal,
                new ConfigurePrincipalRequest(current)).Get<PrincipalRecord>());
            await RefusedAsync(fixture, owner, created.Operation, token);
            await RefusedAsync(fixture, owner, reconfigured.Operation, token);
            await Assert.That(A.Outcome(fixture, created.Operation).SequenceEqual(originalBytes)).IsTrue();
            await A.EqualAsync(created.Lifetime, A.Lifetime(fixture, changed));
        }
        var healthyDefinition = changed with
        {
            SchemaVersion = changed.SchemaVersion + 1,
            FieldPolicies = []
        };
        var healthy = await ConfigureAsync(fixture, owner, healthyDefinition, changed, token);
        await A.EqualAsync(created.Lifetime, healthy.Lifetime);
    }, token);

    private static async Task<(Command<DatabaseCommand> Operation, ResourceLifetime Lifetime)> ConfigureAsync(
        TestDatabase fixture, RequestWorkOwner owner, ResourceDefinition expected, ResourceDefinition? previous, CancellationToken token, bool createTable = false)
    {
        Command<DatabaseCommand>? original = null;
        ResourceLifetime? lifetime = null;
        await O.LeaseAsync(fixture, owner, Guid.NewGuid(), async (lease, work) =>
        {
            var request = new ConfigureResourceRequest(fixture.Partition.TenantId, fixture.Partition.DatabaseId, expected)
            {
                ExpectedSchemaVersion = previous?.SchemaVersion
            };
            var payload = createTable
                ? OrleansSerialization.Serialize(new CreateTableRequest(request.TenantId, request.DatabaseId, expected))
                : OrleansSerialization.Serialize(request);
            original = fixture.Database.CreateCommand(createTable ? OperationKind.CreateTable : OperationKind.ConfigureResource,
                Guid.NewGuid(), Subject, fixture.Database.EvaluationClock.GetUtcNow(), payload, work);
            var admitted = ResourceEffectAdmissionValidation.Read(original, fixture.Database.OperationLimitsOptions.Value);
            await Assert.That(admitted.Bindings.Length).IsEqualTo(previous is null ? 0 : 1);
            fixture.Database.ValidateResourceEffectSubmission(original, new DateTimeOffset(original.Timestamp), lease, work);
            await Assert.That(lease.Dependencies.Count).IsEqualTo(previous is null ? 0 : 1);
            await A.EqualAsync(expected, fixture.SubmitIssued(original).Get<ResourceDefinition>());
            lifetime = A.Lifetime(fixture, expected);
            var spent = work.ReadBytes;
            await A.UnchangedAsync(fixture, async () =>
            {
                await A.EqualAsync(expected, fixture.Database.ResolveSubmissionOutcome(original, work).Get<ResourceDefinition>());
                await Assert.That(work.ReadBytes).IsGreaterThan(spent);
                await A.BoundAsync(lease, lifetime);
            });
        }, token);
        return (original ?? throw new InvalidOperationException("The execution configure operation was not issued."),
            lifetime ?? throw new InvalidOperationException("The execution configure lifetime was not created."));
    }

    private static Task ReplayAsync(TestDatabase fixture, RequestWorkOwner owner, Command<DatabaseCommand> original,
        ResourceDefinition expected, ResourceLifetime lifetime, CancellationToken token)
        => O.LeaseAsync(fixture, owner, Guid.NewGuid(), async (lease, work) =>
        {
            await Assert.That(lease.Dependencies.IsEmpty).IsTrue();
            await A.UnchangedAsync(fixture, async () =>
            {
                await A.EqualAsync(expected, fixture.Database.ResolveSubmissionOutcome(original, work).Get<ResourceDefinition>());
                await A.BoundAsync(lease, lifetime);
            });
        }, token);

    private static Task RefusedAsync(TestDatabase fixture, RequestWorkOwner owner,
        Command<DatabaseCommand> original, CancellationToken token)
        => O.LeaseAsync(fixture, owner, Guid.NewGuid(), async (lease, work) =>
        {
            await A.UnchangedAsync(fixture, async () =>
            {
                ResourceDefinition? published = null;
                var refusal = Assert.ThrowsExactly<KeyLoadException>(() =>
                    published = fixture.Database.ResolveSubmissionOutcome(original, work).Get<ResourceDefinition>());
                await Assert.That(refusal.Code).IsEqualTo(ErrorCode.PermissionDenied);
                await Assert.That(published).IsNull();
                await Assert.That(lease.Dependencies.IsEmpty).IsTrue();
            });
        }, token);
}
