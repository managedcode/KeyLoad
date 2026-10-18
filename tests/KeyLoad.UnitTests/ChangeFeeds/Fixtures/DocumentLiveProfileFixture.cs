using ManagedCode.Communication.Commands;
using KeyLoad.Core;
using KeyLoad.Query;

namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed class DocumentLiveProfileFixture : IDisposable
{
    internal const string Collection = "live-profile-documents";
    internal const string Reader = "live-profile-reader";
    internal const string Secret = "PRIVATE-LIVE-PROFILE";
    internal const string Unsupported = "The live-query profile requires an unordered complete scalar result set.";
    internal TestDatabase Harness { get; } = new();
    internal List<(Command<DatabaseCommand> Operation, CommitReceipt Receipt, byte[] Outcome)> Commands { get; } = [];
    internal AstQueryRequest Query => new(Harness.Partition,
        new(Collection, null, [new("*", "*")], new Comparison(new FieldOperand("/value"), ">", ValueOperand.Create(0)), [], 10), AllowFullScan: true);
    internal AstQueryRequest Model(ModelQuerySourceKind kind) => Query with
    {
        Query = Query.Query with
        {
            ModelSource = new(kind, kind == ModelQuerySourceKind.Events ? "stream" : Collection, 1L)
        }
    };
    internal EntityRef Reference(string id) => new(Harness.Partition, Collection, id);
    internal static string Json(int value) => "{\"value\":" + value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        + ",\"secret\":\"" + Secret + "\"}";
    internal static QueryRow Row(string id, long revision, int value)
        => new(id, revision, "{\"value\":" + value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}", true, ["/secret"]);
    internal async Task InitializeAsync(CancellationToken token)
    {
        Harness.Configure(Collection, ResourceKind.Collection, fields: [new("/secret", "private", "private.read", "private.use")]);
        await PersistAsync(1L, true, false);
        await CommitAsync([new PutDocument(Collection, "a", Json(1), 0L, Access: new("live-owner")),
            new PutDocument(Collection, "hidden", Json(99), 0L, Access: new("other-owner"))],
            [new("putDocument", Collection, "a", 1L), new("putDocument", Collection, "hidden", 1L)], token);
    }
    internal async Task PersistAsync(long epoch, bool feed, bool revoked)
    {
        var expected = new PrincipalRecord(Reader, Harness.Partition.TenantId,
            [new(Harness.Partition.DatabaseId, Collection, Capability.DocumentsRead | Capability.Query | (feed ? Capability.ChangesRead : Capability.None))], [])
        {
            PolicyEpoch = epoch,
            Revoked = revoked,
            RestrictRows = true,
            OwnerId = "live-owner"
        };
        await DocumentLiveProfileReads.EqualAsync(Harness.Submit(OperationKind.ConfigurePrincipal,
            new ConfigurePrincipalRequest(expected)).Get<PrincipalRecord>(), expected);
    }
    internal async Task CommitAsync(Mutation[] mutations, MutationReceipt[] effects, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), Harness.Partition, [.. mutations]);
        var operation = Harness.Database.CreateCommand(OperationKind.Batch, command.CommandId, "root", default, System.Text.Json.JsonSerializer.Serialize(command, JsonDefaults.Options));
        var before = Harness.Store.Position;
        var receipt = Harness.Database.ApplyEmbedded(operation, token).Get<CommitReceipt>();
        var expected = new CommitReceipt(command.CommandId, new(Harness.Store.Identity.Incarnation, Harness.Partition.AtomicPartitionId, before + 1L), [.. effects], DurabilityProfile.ProcessDurable, 1);
        await DocumentLiveProfileReads.EqualAsync(receipt, expected);
        await Assert.That(Harness.Store.Position).IsEqualTo(before + 1L);
        foreach (var effect in receipt.Mutations)
        {
            await Assert.That(effect.CompositionReferences).IsEmpty();
        }
        var outcome = Harness.Store.Read(view => view.ReadOwnedValue(KeySpace.PartitionOutcome(Harness.Partition, "root", command.CommandId)))
            ?? throw new InvalidOperationException("The genuine live profile producer outcome is missing.");
        Commands.Add((operation, expected, outcome));
    }
    public void Dispose() => Harness.Dispose();
}
