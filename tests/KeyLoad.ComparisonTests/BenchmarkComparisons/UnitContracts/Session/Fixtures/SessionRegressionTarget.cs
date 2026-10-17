using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

// Small deterministic contract oracle only; this target never produces native performance evidence.
internal class SessionRegressionTarget : IDocumentComparisonTarget
{
    internal ConcurrentDictionary<string, BenchmarkDocument> Documents { get; } = new(StringComparer.Ordinal);
    internal int Initializations
    {
        get; private set;
    }
    internal int Writes => Volatile.Read(ref writes);
    internal bool SynchronousOperations
    {
        get; init;
    }
    internal bool DiscardWrites
    {
        get; init;
    }
    internal bool CorruptReads
    {
        get; set;
    }
    internal bool DiscardUpdates
    {
        get; init;
    }
    internal bool HoldMixedStreams
    {
        get; set;
    }
    private readonly ConcurrentDictionary<string, byte> mixedLanes = new(StringComparer.Ordinal);
    private readonly TaskCompletionSource overlap = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal int Opened;
    internal int Closed;
    private int writes;
    internal async Task ObserveMixedLaneAsync(string lane, CancellationToken token)
    {
        if (!HoldMixedStreams)
        {
            return;
        }
        mixedLanes.TryAdd(lane, 0);
        if (mixedLanes.Count == 5)
        {
            overlap.TrySetResult();
        }
        await overlap.Task.WaitAsync(token);
    }
    public TargetProfile Profile => new("ContractOnly", "test", "Single", "contract", "contract", "contract", "contract", null)
    {
        Cluster = new(1, 1, "test", [])
    };
    public bool Supports(Scenario scenario) => scenario is Scenario.PointRead or Scenario.DocumentWrite or Scenario.DocumentUpdate or Scenario.DocumentDelete;
    public Task InitializeAsync(IComparisonCorpus dataset, CancellationToken cancellationToken)
        => throw new InvalidOperationException("The session must use configure-only document initialization.");
    public Task InitializeDocumentsAsync(DocumentComparisonInitialization initialization, CancellationToken cancellationToken)
    {
        if (initialization.Seed || initialization.Corpus.Documents.Count != 0 || Initializations != 0)
        {
            throw new InvalidOperationException("Unexpected session reset or hidden seeding.");
        }
        Initializations++;
        return Task.CompletedTask;
    }
    public Task<IDocumentComparisonSession> OpenDocumentSessionAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref Opened);
        return Task.FromResult<IDocumentComparisonSession>(new Session(this));
    }
    public async Task<IComparisonSession> OpenSessionAsync(CancellationToken cancellationToken)
        => await OpenDocumentSessionAsync(cancellationToken);
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private void Write(BenchmarkDocument document)
    {
        Interlocked.Increment(ref writes);
        if (!DiscardWrites && !Documents.TryAdd(document.Id, document))
        {
            throw new InvalidOperationException("Duplicate create.");
        }
    }

    private void Update(BenchmarkDocument document)
    {
        if (!Documents.ContainsKey(document.Id))
        {
            throw new InvalidOperationException("Missing update.");
        }
        if (!DiscardUpdates)
        {
            Documents[document.Id] = document;
        }
    }

    private void Delete(BenchmarkDocument document)
    {
        if (!Documents.TryRemove(document.Id, out _))
        {
            throw new InvalidOperationException("Missing delete.");
        }
    }

    private sealed class Session(SessionRegressionTarget target) : IDocumentComparisonSession
    {
        public Guid ClientIdentity { get; } = Guid.NewGuid();
        public bool SingleOperationTiming => true;
        public async Task<global::KeyLoad.Comparisons.OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document, CancellationToken cancellationToken)
        {
            if (!target.SynchronousOperations)
            {
                await Task.Yield();
            }
            cancellationToken.ThrowIfCancellationRequested();
            await target.ObserveMixedLaneAsync(scenario.ToString(), cancellationToken);
            switch (scenario)
            {
                case Scenario.PointRead:
                    return new(Document: await ReadAsync(document, cancellationToken));
                case Scenario.DocumentWrite:
                    target.Write(document);
                    break;
                case Scenario.DocumentUpdate:
                    target.Update(document);
                    break;
                case Scenario.DocumentDelete:
                    target.Delete(document);
                    break;
                default:
                    throw new NotSupportedException();
            }
            return new();
        }
        public Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
            => Task.FromResult(target.Documents.TryGetValue(document.Id, out var found)
                ? new FoundDocument(found.Id, target.CorruptReads ? "{}" : found.Json) : null);
        public async IAsyncEnumerable<FoundDocument> ReadDocumentsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var item in target.Documents.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new(item.Key, item.Value.Json);
                await Task.Yield();
            }
        }
        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref target.Closed);
            return ValueTask.CompletedTask;
        }
    }
}
