using System.Text.Json;
using KeyLoad.Server;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace KeyLoad.ClusterTests.ClientApi;

/// <summary>Observes only the pinned official caller's execution remote error code during true CreateAsync.</summary>
internal sealed class McpInitializeLogObservation : ILoggerFactory
{
    private const int MaximumEvents = 8;
    private const int SchemaVersion = 1;
    private const string ClientCategory = "ModelContextProtocol.Client.McpClient";
    private const string FailureEvent = "LogSendingRequestFailed";
    private const string CodeKey = "ErrorCode";
    private const string RemoteErrorCategory = "RemoteRpcError";
    private const string Kind = "McpInitializeSdk";
    private const string UnsupportedProvider = "The execution caller observation cannot register external providers.";
    private readonly Lock gate = new();
    private readonly List<object> events = [];
    private readonly List<Exception> failures = [];
    private bool enabled;
    private bool saturated;

    /// <summary>Starts the passive observation immediately before the original CreateAsync call.</summary>
    internal void Start()
    {
        lock (gate)
        {
            enabled = true;
        }
    }

    /// <summary>Stops recording before any caller operation or subsequent owner cleanup.</summary>
    internal void Stop()
    {
        lock (gate)
        {
            enabled = false;
        }
    }

    /// <summary>Retains any observation failure even if execution initialization otherwise returns successfully.</summary>
    internal void ThrowIfAny()
    {
        lock (gate)
        {
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    /// <summary>Writes only closed execution facts on failure and joins original observation/write faults.</summary>
    /// <param name="ledger">The existing primary-first failure ledger.</param>
    internal void Write(List<Exception> ledger)
    {
        lock (gate)
        {
            foreach (var failure in failures)
            {
                if (!ledger.Any(existing => ReferenceEquals(existing, failure)))
                {
                    ledger.Add(failure);
                }
            }
            ServerFailureObserver.Observe(() => Console.Error.WriteLine(JsonSerializer.Serialize(new
            {
                schemaVersion = SchemaVersion,
                kind = Kind,
                saturated,
                events
            })), ledger);
        }
    }

    /// <summary>Returns the bounded execution logger only for the exact official client category.</summary>
    /// <param name="categoryName">The execution factory's requested category.</param>
    /// <returns>The passive owner or the execution null logger.</returns>
    public ILogger CreateLogger(string categoryName)
        => string.Equals(categoryName, ClientCategory, StringComparison.Ordinal) ? new RuntimeLogger(this) : NullLogger.Instance;

    /// <summary>Refuses external logging providers; this factory has one caller-owned execution observer.</summary>
    /// <param name="provider">The unrelated provider, never retained.</param>
    public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException(UnsupportedProvider);

    /// <summary>Releases the stopped observer after actual session and transport settlement.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            enabled = false;
            events.Clear();
            failures.Clear();
        }
    }

    private bool Enabled(LogLevel level)
    {
        lock (gate)
        {
            return enabled && level == LogLevel.Warning;
        }
    }

    private void Observe<TState>(LogLevel level, EventId eventId, TState state)
    {
        lock (gate)
        {
            if (!enabled || level != LogLevel.Warning || !string.Equals(eventId.Name, FailureEvent, StringComparison.Ordinal))
            {
                return;
            }
            ServerFailureObserver.Observe(() => Record(state), failures);
            if (failures.Count != 0)
            {
                enabled = false;
            }
        }
    }

    private void Record<TState>(TState state)
    {
        if (state is not IReadOnlyList<KeyValuePair<string, object?>> values)
        {
            return;
        }
        foreach (var value in values)
        {
            if (!string.Equals(value.Key, CodeKey, StringComparison.Ordinal) || value.Value is not int code)
            {
                continue;
            }
            if (events.Count == MaximumEvents)
            {
                saturated = true;
                return;
            }
            events.Add(new
            {
                category = RemoteErrorCategory,
                code
            });
            return;
        }
    }

    private sealed class RuntimeLogger(McpInitializeLogObservation owner) : ILogger
    {
        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => owner.Enabled(logLevel);
        /// <inheritdoc/>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => owner.Observe(logLevel, eventId, state);
    }
}
