using ManagedCode.Communication;

namespace KeyLoad.FunctionalTests.DocumentStorage;

internal static class DocumentCommandPrincipalColdReplayContract
{
    internal const string Collection = "orders";
    internal const string FirstPrincipal = "scope-writer-one";
    internal const string SecondPrincipal = "scope-writer-two";
    internal const string FirstDocument = "scope-one";
    internal const string SecondDocument = "scope-two";
    internal const string FirstJson = "{\"owner\":\"one\"}";
    internal const string SecondJson = "{\"owner\":\"two\"}";
    internal const string ChangedJson = "{\"owner\":\"healthy-one\"}";
    internal sealed record Retained(StoredOutcome Entry, Result<object> Result);
}
