using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.DocumentStorage;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.QueryExecution;

/// <summary>Owns independent canonical stores for native query leaf and authority checks.</summary>
internal sealed class QueryNativeFixture(DocumentPatchAtomicityOwner source, DocumentPatchAtomicityOwner destination)
{
    internal const string Root = "root";
    internal const string Reader = "query-native-reader";
    internal const string Tenant = "query-native-tenant";
    private const string DatabaseId = "query-native-db";
    private const string TransactionDomainId = "query-native-domain";
    private const string PartitionKey = "one";
    internal const string Collection = "query-native-documents";
    internal const string DocumentId = "one";
    internal const string Title = "/title";
    internal const string Secret = "/secret";
    internal const string Classification = "private";
    internal const string OriginalJson = "{\"title\":\"destination\",\"secret\":\"private-canary\"}";
    internal const string ProjectedJson = "{\"title\":\"destination\"}";
    internal const int Version = 1;
    internal const long Empty = 0;
    internal const long First = 1;
    internal const long Second = 2;
    private const string ChangedCreation = "The native resource differs from its original creation operation.";
    internal static readonly PartitionRef Partition = new(Tenant, DatabaseId, TransactionDomainId, PartitionKey);
    internal static readonly EntityRef Reference = new(Partition, Collection, DocumentId);
    internal static readonly Guid WriteId = Guid.Parse("102b95e5-2657-40bb-9d1a-efc39b18877d");
    internal DocumentPatchAtomicityOwner Source => source;
    internal DocumentPatchAtomicityOwner Destination => destination;
    internal PhysicalShardRecord SourceOwner => new(source.Store.Identity.NodeId, source.Store.Identity.Incarnation);
    internal PhysicalShardRecord DestinationOwner => new(destination.Store.Identity.NodeId, destination.Store.Identity.Incarnation);
    internal ResourceLifetimeV1 SourceCreation { get; private set; } = null!;
    internal ResourceLifetimeV1 DestinationCreation { get; private set; } = null!;
    internal Command<DatabaseCommand> Write { get; private set; } = null!;
    internal CommitReceipt Receipt { get; private set; } = null!;
    internal long WriteSequence { get; private set; }
    internal EntityRef ExpectedDestinationReference => Reference with { Lifetime = new(DestinationCreation.Incarnation.Value) };
    internal EntityRef ExpectedSourceReference => new(QueryNativeDataset.Local, Collection, DocumentId)
    {
        Lifetime = new(SourceCreation.Incarnation.Value)
    };

    internal static Task RunAsync(Func<QueryNativeFixture, Task> operation)
        => DocumentPatchAtomicityOwner.RunAsync(sourceOwner => DocumentPatchAtomicityOwner.RunAsync(
            destinationOwner => operation(new(sourceOwner, destinationOwner))));

    internal void Seed()
    {
        SourceCreation = Configure();
        DestinationCreation = SourceCreation;
        RequireOriginalResourceBindings();
        Principal(source, new(Reader, Tenant, [new(Partition.DatabaseId, Collection, Capability.DocumentsRead)], [Title]));
        Principal(destination, new(Reader, Tenant, [], [Title]));
        Write = Operation(destination, OperationKind.Batch, WriteId,
            new CommandRequest(WriteId, Partition, [new PutDocument(Collection, DocumentId, OriginalJson)]));
        WriteSequence = checked(destination.ReadCut().AppliedPosition + First);
        Receipt = destination.Submit(Write).Get<CommitReceipt>();
    }

    internal static Command<DatabaseCommand> Operation<T>(DocumentPatchAtomicityOwner owner,
        OperationKind kind, Guid id, T payload)
        => owner.Database.CreateCommand(kind, id, Root, owner.Database.EvaluationClock.GetUtcNow(),
            NativeSerialization.Serialize(payload));

    internal static void Principal(DocumentPatchAtomicityOwner owner, PrincipalRecord principal)
        => _ = owner.Submit(Operation(owner, OperationKind.ConfigurePrincipal, Guid.NewGuid(),
            new ConfigurePrincipalRequest(principal))).Get<PrincipalRecord>();

    private ResourceLifetimeV1 Configure()
    {
        var definition = new ResourceDefinition(Collection, ResourceKind.Collection, Partition.TransactionDomainId)
        {
            FieldPolicies = [new(Secret, Classification)]
        };
        var creation = Operation(source, OperationKind.ConfigureResource, Guid.NewGuid(),
            new ConfigureResourceRequest(Partition.TenantId, Partition.DatabaseId, definition));
        var expected = ResourceLifetimeIdentity.Create(creation, Partition.TenantId, Partition.DatabaseId, definition);
        _ = source.Submit(creation).Get<ResourceDefinition>();
        _ = destination.Submit(destination.Database.OwnCommand(creation)).Get<ResourceDefinition>();
        RequireCreation(source, expected);
        RequireCreation(destination, expected);
        return expected;
    }

    internal void RequireOriginalResourceBindings()
    {
        RequireCreation(source, SourceCreation);
        RequireCreation(destination, DestinationCreation);
    }

    private static void RequireCreation(DocumentPatchAtomicityOwner owner, ResourceLifetimeV1 expected)
    {
        var actual = owner.Store.Read(view =>
        {
            var definition = view.GetRecord<ResourceDefinition>(KeySpace.Resource(Tenant, Partition.DatabaseId, Collection));
            return ResourceLifetimeCatalog.Read(view, Tenant, Partition.DatabaseId, Collection, definition);
        });
        if (actual != expected)
        {
            throw new InvalidOperationException(ChangedCreation);
        }
    }
}
