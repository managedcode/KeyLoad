namespace KeyLoad.FunctionalTests.OrleansExecution;

internal static class RequestCqrsFixtureScheduling
{
    // The default data source retains one mutable connection/database/codec per test session.
    internal const string SharedSession = "keyload.unit.request-cqrs-shared-session";
}
