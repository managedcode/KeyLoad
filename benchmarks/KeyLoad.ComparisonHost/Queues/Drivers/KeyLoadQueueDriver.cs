using System.Text.Json;
using KeyLoad.ComparisonHost.Deployment;

namespace KeyLoad.ComparisonHost.Queues;

/// <summary>
/// KeyLoad work queue through the KeyLoad SDK: one <see cref="EnqueueMessage"/> commit per publish, a leased
/// <c>ReceiveAsync</c> of one message per receive and a <see cref="DeliveryAction.Ack"/> completion of its delivery token.
/// </summary>
internal sealed class KeyLoadQueueDriver(BenchmarkConnection connection) : IQueueDriver
{
    private const string QueueName = "messages";
    private const string BodyProperty = "body";
    private const string PayloadPrefix = "{\"" + BodyProperty + "\":\"";
    private const string PayloadSuffix = "\"}";
    private const int MaxMessages = 1;
    private const int LeaseSeconds = 30;
    private const int ReceiveSeconds = 60;
    private const string NoDelivery = "No KeyLoad delivery arrived within 60 seconds.";
    private const string MissingBody = "KeyLoad delivered a message without a body: ";
    private const string WrongHandle = "The delivery was not received by the KeyLoad queue driver.";
    private static readonly TimeSpan ReceiveLimit = TimeSpan.FromSeconds(ReceiveSeconds);
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(1);
    private readonly KeyLoadDatabase database = new(connection);

    private QueueLaneRef Lane => new(database.Partition, QueueName);

    public Task PrepareAsync(CancellationToken token)
        => database.ConfigureAsync(database.Resource(QueueName, ResourceKind.WorkQueue), token);

    public Task PublishAsync(QueueMessage message, CancellationToken token)
        => database.CommitAsync([new EnqueueMessage(QueueName, message.Id, Payload(message.Body))], token);

    public async Task<QueueDelivery> ReceiveAsync(CancellationToken token)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (TimeProvider.System.GetElapsedTime(started) < ReceiveLimit)
        {
            var request = new ReceiveRequest(Guid.NewGuid(), Lane, MaxMessages: MaxMessages, LeaseSeconds: LeaseSeconds);
            var received = KeyLoadDatabase.Require(await database.Client.ReceiveAsync(request, token), nameof(ReceiveAsync));
            if (received.Deliveries.Length > 0)
            {
                var delivery = received.Deliveries[0];
                return new(new(delivery.Id, Body(delivery.PayloadJson)), delivery.Token);
            }
            await Task.Delay(PollDelay, TimeProvider.System, token);
        }
        throw new TimeoutException(NoDelivery);
    }

    public async Task AckAsync(QueueDelivery delivery, CancellationToken token)
    {
        if (delivery.Handle is not string deliveryToken)
        {
            throw new ArgumentException(WrongHandle, nameof(delivery));
        }
        var command = new DeliveryCommand(Guid.NewGuid(), Lane, deliveryToken, DeliveryAction.Ack);
        _ = KeyLoadDatabase.Require(await database.Client.CompleteAsync(command, token), nameof(AckAsync));
    }

    public ValueTask DisposeAsync() => database.DisposeAsync();

    private static string Payload(string body) => PayloadPrefix + JsonEncodedText.Encode(body).Value + PayloadSuffix;

    private static string Body(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.GetProperty(BodyProperty).GetString() ?? throw new InvalidDataException(MissingBody + payload);
    }
}
