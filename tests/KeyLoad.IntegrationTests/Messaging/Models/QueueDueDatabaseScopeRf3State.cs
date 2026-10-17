namespace KeyLoad.IntegrationTests.Features.Messaging;

internal sealed class QueueDueDatabaseScopeRf3State(MessagingRf3Identity identity, DateTimeOffset due,
    QueueDueDatabaseScopeRf3Lane allowed, QueueDueDatabaseScopeRf3Lane denied)
{
    internal MessagingRf3Identity Identity { get; set; } = identity;
    internal DateTimeOffset Due { get; } = due;
    internal QueueDueDatabaseScopeRf3Lane Allowed { get; } = allowed;
    internal QueueDueDatabaseScopeRf3Lane Denied { get; } = denied;
    internal QueueDueDatabaseScopeRf3Lane[] Lanes => [Denied, Allowed];
}
