namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>Frozen caller-visible document values and commands for the real RF3 CRUD parity cases.</summary>
internal sealed record McpDocumentCrudParityScenario(PartitionRef Partition)
{
    internal const long CreatedRevision = 1;
    internal const long ReplacedRevision = 2;
    internal const long PatchedRevision = 3;
    internal const long TombstoneRevision = 4;
    internal const string CreatedJson = "{\"state\":\"created\"}";
    internal const string ReplacedJson = "{\"state\":\"replaced\"}";
    internal const string PatchedJson = "{\"state\":\"patched\"}";
    internal const string StaleJson = "{\"state\":\"stale\"}";

    internal EntityRef Reference => new(Partition, McpDocumentProtocol.Collection, McpDocumentProtocol.Entity);

    internal CommandRequest CreateCommand(Guid commandId)
        => Command(commandId, new PutDocument(McpDocumentProtocol.Collection, McpDocumentProtocol.Entity,
            CreatedJson, ExpectedRevision: 0));

    internal CommandRequest ReplaceCommand(Guid commandId)
        => Command(commandId, new PutDocument(McpDocumentProtocol.Collection, McpDocumentProtocol.Entity,
            ReplacedJson, ExpectedRevision: CreatedRevision, ExplicitReplacement: true));

    internal CommandRequest StaleReplaceCommand(Guid commandId)
        => Command(commandId, new PutDocument(McpDocumentProtocol.Collection, McpDocumentProtocol.Entity,
            StaleJson, ExpectedRevision: CreatedRevision, ExplicitReplacement: true));

    internal CommandRequest PatchCommand(Guid commandId)
        => Command(commandId, new PatchDocument(McpDocumentProtocol.Collection, McpDocumentProtocol.Entity,
            [new("/state", PatchKind.Set, "\"patched\"")], ReplacedRevision));

    internal CommandRequest DeleteCommand(Guid commandId)
        => Command(commandId, new DeleteDocument(McpDocumentProtocol.Collection, McpDocumentProtocol.Entity,
            PatchedRevision));

    private CommandRequest Command(Guid commandId, Mutation mutation)
        => new(commandId, Partition, [mutation]);
}
