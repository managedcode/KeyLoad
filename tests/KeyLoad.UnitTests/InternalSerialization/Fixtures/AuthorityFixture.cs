using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.InternalSerialization;

internal static class AuthorityFixture
{
    internal const string Root = "root";
    internal const string OtherPrincipal = "other";
    internal const string Collection = "execution-command-input";
    internal const string Entity = "entry";
    internal const string Json = "{\"value\":\"界λ😀\",\"number\":1.00}";
    internal const string InvalidJson = "{";

    internal static Command<DatabaseCommand> Create(TestDatabase database)
        => database.Database.CreateCommand(OperationKind.SetDispatch, Guid.NewGuid(), Root,
            database.Database.EvaluationClock.GetUtcNow(), OrleansSerialization.Serialize(true));
}
