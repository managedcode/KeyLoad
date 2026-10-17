namespace KeyLoad.UnitTests.Features.Messaging;

internal static class SubscriptionFilterProtocol
{
    internal const string Root = "root";
    internal const string Topic = "topic";
    internal const string Group = "filter-group";
    internal const string Independent = "independent";
    internal const string OldType = "Created";
    internal const string NewType = "Changed";
    internal const string Payload = "{\"value\":1}";
    internal const string Window = "subscription-window";
    internal const string Completion = "subscription-completion";
    internal const string State = "subscription";
    internal const int WindowSize = 3;
    internal const long FirstGeneration = 1;
    internal const long NextGeneration = 2;
    internal const long GapCheckpoint = 1;
    internal const string FirstEvent = "one";
    internal const string SecondEvent = "two";
    internal const string ThirdEvent = "three";
    internal const string FourthEvent = "four";
    internal const string MissingGroup = "missing";
    internal const string InvalidCursor = "unused";
    internal const string HealthyEvent = "healthy";
    internal const string TopicRecords = "topic-event";
    internal const string TopicHead = "topic-head";
    internal const string TopicIdentities = "topic-event-id";
}
