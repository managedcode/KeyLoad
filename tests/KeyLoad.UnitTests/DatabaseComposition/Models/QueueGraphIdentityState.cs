using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.DatabaseComposition;

internal sealed record QueueGraphIdentityState(
    TestDatabase Fixture,
    EntityRef From,
    EntityRef To,
    DateTimeOffset SeedAt,
    DocumentRecord First,
    DocumentRecord Second,
    MessageInspection Source,
    Command<DatabaseCommand> Seed,
    CommitReceipt SeedReceipt,
    Command<DatabaseCommand> Failed,
    Command<DatabaseCommand> Healthy,
    byte[] EndpointKey,
    byte[] OriginalEndpoint,
    EntityRef BoundFrom,
    EntityRef BoundTo,
    EntityRef MarkerReference)
{
    internal ErrorCode FailureCode { get; init; } = ErrorCode.Corruption;
    internal string FailureDetail { get; init; } = "The typed endpoint record has a different canonical identity.";
    internal MessageInspection? FaultSource
    {
        get; init;
    }

    internal string[] ColdImage { get; set; } = [];
    internal long ColdPosition
    {
        get; set;
    }
    internal byte[] HealthyReceipt { get; set; } = [];

    internal const string Collection = "entities";
    internal const string Graph = "links";
    internal const string Queue = "jobs";
    internal const string Message = "message";
    internal const string Prefix = "derived-";
    internal const string Marker = "marker";
    internal const string Label = "related";
    internal const string FirstJson = "{\"name\":\"first\"}";
    internal const string SecondJson = "{\"name\":\"second\"}";
    internal const string MarkerJson = "{\"state\":\"healthy\"}";
    internal const string AttributesJson = "{\"weight\":7}";
    internal const string HeadersJson = "{\"origin\":\"identity-operation\"}";
    internal const string Root = "root";
    internal const string ForeignPartition = "foreign";
}
