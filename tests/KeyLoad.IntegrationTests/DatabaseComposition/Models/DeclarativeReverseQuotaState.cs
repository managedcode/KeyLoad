using System.Text.Json;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record DeclarativeReverseQuotaState(RelationalSqlRf3Scenario Scenario,
    DeclarativeCompositionBudgetState Source, ResourceDefinition Target, CommandRequest Seed, CommitReceipt SeedReceipt,
    DocumentResult FirstDocument, DocumentResult SecondDocument, CommandRequest Refused,
    JsonElement Problem, CommandRequest Holder, CommitReceipt HolderReceipt, Guid ConfigureId,
    ReceiveRequest? ClaimRequest = null, ReceiveResult? Claim = null, JsonElement? ClaimImage = null,
    DeliveryCommand? HolderAck = null, CommitReceipt? HolderAckReceipt = null,
    CommandRequest? Healthy = null, CommitReceipt? Receipt = null);
