using KeyLoad.ComparisonHost.Deployment;
using KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

namespace KeyLoad.ComparisonHost.Queues;

/// <summary>The only operations a queue implements; thread-safe, shared by all clients.</summary>
internal interface IQueueDriver : IAsyncDisposable
{
    /// <summary>Creates one durable queue with manual acknowledgement.</summary>
    Task PrepareAsync(CancellationToken token);

    /// <summary>Publishes one persistent message; returns after the broker confirmed it.</summary>
    Task PublishAsync(QueueMessage message, CancellationToken token);

    /// <summary>Waits until one message is delivered to this client and returns it unacknowledged.</summary>
    Task<QueueDelivery> ReceiveAsync(CancellationToken token);

    /// <summary>Acknowledges a delivery so it is never redelivered.</summary>
    Task AckAsync(QueueDelivery delivery, CancellationToken token);
}

/// <summary>Creates the queue driver of the deployed database.</summary>
internal static class QueueDrivers
{
    internal static IQueueDriver Create(BenchmarkConnection connection) => connection.Database switch
    {
        BenchmarkDatabases.KeyLoad => new KeyLoadQueueDriver(connection),
        BenchmarkDatabases.Rabbit => new RabbitQueueDriver(connection),
        _ => throw new NotSupportedException(BenchmarkTypes.NoDriver + BenchmarkTypes.Queue + BenchmarkTypes.ForDatabase + connection.Database)
    };
}
