using KeyLoad.IntegrationTests.Features.ClientApi;
namespace KeyLoad.IntegrationTests.Features.QueryExecution;

internal sealed record SqlCreateTableRf3State(McpPersistedIdentity Identity, Guid CreatedId, Guid FailedId,
    ResourceDefinition Definition, CommandRequest Seed, CommitReceipt Receipt, bool EpochFenced = false, Guid? RepairId = null, long Revision = SqlCreateTableRf3Literal.InitialRevision,
    string Selected = SqlCreateTableRf3Literal.Selected, long SeedEpoch = SqlCreateTableRf3Literal.AdministratorEpoch);
