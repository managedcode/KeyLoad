using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.Authorization;

internal sealed class InitialPrincipalEpochFixture : IDisposable
{
    internal const string Reader = "epoch-reader";
    internal const string Collection = "epoch-documents";
    internal const string KeyId = "epoch-key";
    internal const string OriginalSecret = "epoch-key.original-secret-at-least-32";
    internal const string ReplacementSecret = "epoch-key.replacement-secret-at-least-32";
    internal const string InvalidDetail = "An initial principal policy epoch must be positive.";
    internal TestDatabase Harness { get; } = new();
    internal Command<DatabaseCommand> Seed { get; private set; } = null!;
    internal CommitReceipt SeedReceipt { get; private set; } = null!;
    internal byte[] SeedOutcome { get; private set; } = null!;
    internal async Task SeedAsync(CancellationToken token)
    {
        Harness.Configure(Collection, ResourceKind.Collection);
        var command = new CommandRequest(Guid.NewGuid(), Harness.Partition, [new PutDocument(Collection, "record", "{\"value\":0}", 0L)]);
        Seed = Operation(OperationKind.Batch, command, id: command.CommandId);
        var before = Harness.Store.Position;
        var expectedToken = Harness.NextCommitToken();
        SeedReceipt = Harness.Database.ApplyEmbedded(Seed, token).Get<CommitReceipt>();
        var expected = new CommitReceipt(command.CommandId, expectedToken, [new("putDocument", Collection, "record", 1L)], DurabilityProfile.ProcessDurable, 1);
        await InitialPrincipalEpochAssertions.EqualAsync(SeedReceipt, expected);
        await Assert.That(SeedReceipt.Mutations.Single().CompositionReferences).IsEmpty();
        await Assert.That(Harness.Store.Position).IsEqualTo(before + 1L);
        SeedOutcome = Harness.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(Harness.Partition, "root", command.CommandId)))
            ?? throw new InvalidOperationException("The original acknowledged principal lifecycle seed outcome is absent.");
    }
    internal PrincipalRecord Principal(long epoch, bool granted = true, bool revoked = false)
        => new(Reader, Harness.Partition.TenantId,
            granted ? [new(Harness.Partition.DatabaseId, Collection, Capability.DocumentsRead | Capability.DocumentsWrite)] : [], [])
        {
            PolicyEpoch = epoch,
            Revoked = revoked
        };
    internal static ApiKeyRecord Credential(bool replacement = false, bool expired = false)
        => AuthorizationExecution.Credential(KeyId, Reader, replacement ? ReplacementSecret : OriginalSecret,
            expired ? DateTimeOffset.UnixEpoch : null);
    internal Command<DatabaseCommand> Configuration(long epoch)
        => Operation(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(Principal(epoch)));
    internal Command<DatabaseCommand> Operation<T>(OperationKind kind, T payload, string principal = "root", Guid? id = null) where T : notnull
        => Harness.Database.CreateCommand(kind, id ?? Guid.NewGuid(), principal,
            Harness.Database.EvaluationClock.GetUtcNow(), payload);
    internal EntityRef Reference => new(Harness.Partition, Collection, "record");
    internal static byte[] Outcome(Command<DatabaseCommand> operation)
        => KeySpace.GlobalOutcome(operation.UserId!, operation.CommandId);
    public void Dispose() => Harness.Dispose();
}
