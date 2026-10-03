using Garnet.common;
using Tsavorite.core;
using RawSession = Tsavorite.core.ClientSession<Garnet.common.FixedSpanByteKey, Tsavorite.core.PinnedSpanByte, Tsavorite.core.SpanByteAndMemory, long, Tsavorite.core.SpanByteFunctions<long>, Tsavorite.core.StoreFunctions<Tsavorite.core.SpanByteComparer, Tsavorite.core.DefaultRecordTriggers>, Tsavorite.core.SpanByteAllocator<Tsavorite.core.StoreFunctions<Tsavorite.core.SpanByteComparer, Tsavorite.core.DefaultRecordTriggers>>>;
using RawStore = Tsavorite.core.TsavoriteKV<Tsavorite.core.StoreFunctions<Tsavorite.core.SpanByteComparer, Tsavorite.core.DefaultRecordTriggers>, Tsavorite.core.SpanByteAllocator<Tsavorite.core.StoreFunctions<Tsavorite.core.SpanByteComparer, Tsavorite.core.DefaultRecordTriggers>>>;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal sealed class RawStorageFixtureTsavoriteEngine : IRawStorageFixtureEngine
{
    private const string UnexpectedStatusMessage = "Tsavorite returned a nonterminal or unexpected point-operation status.";
    private const string UnexpectedOutputMessage = "Tsavorite did not return the exact value in the caller-pinned output.";
    private readonly RawStorageCorpus corpus;
    private readonly byte[] readScratch;
    private RawStore? store;
    private RawSession? session;
    private KVSettings? settings;
    private SpanByteAndMemory output;
    private bool outputInitialized;

    internal RawStorageFixtureTsavoriteEngine(RawStorageCorpus corpus, byte[] readScratch)
    {
        this.corpus = corpus;
        this.readScratch = readScratch;
        try
        {
            settings = RawStorageFixtureTsavoriteSettings.Create();
            store = RawStorageFixtureTsavoriteSettings.CreateStore(settings);
            session = store.NewSession<FixedSpanByteKey, PinnedSpanByte, SpanByteAndMemory, long, SpanByteFunctions<long>>(
                new SpanByteFunctions<long>());
            output = SpanByteAndMemory.FromPinnedSpan(readScratch);
            outputInitialized = true;
        }
        catch (Exception primary)
        {
            DisposeAfterFailure(primary);
            throw;
        }
    }

    public string? Directory => null;

    public bool TryRead(int index, out ReadOnlyMemory<byte> value)
    {
        var input = default(PinnedSpanByte);
        Status status;
        try
        {
            status = session!.BasicContext.Read(CreateKey(index), ref input, ref output);
        }
        finally
        {
            GC.KeepAlive(corpus);
            GC.KeepAlive(readScratch);
        }
        EnsureTerminal(status);
        if (status.NotFound)
        {
            value = ReadOnlyMemory<byte>.Empty;
            return false;
        }

        if (!status.Found || !output.IsSpanByte || output.Memory is not null || output.Length != corpus.ValueBytes)
        {
            throw new InvalidOperationException(UnexpectedOutputMessage);
        }

        value = readScratch.AsMemory(0, output.Length);
        return true;
    }

    public void Upsert(int index, bool alternate)
    {
        Status status;
        try
        {
            status = session!.BasicContext.Upsert(CreateKey(index), corpus.Value(index, alternate).Span);
        }
        finally
        {
            GC.KeepAlive(corpus);
        }
        EnsureTerminal(status);
    }

    public bool Delete(int index)
    {
        Status status;
        try
        {
            status = session!.BasicContext.Delete(CreateKey(index));
        }
        finally
        {
            GC.KeepAlive(corpus);
        }
        EnsureTerminal(status);
        if (status.Found)
        {
            return true;
        }

        if (status.NotFound)
        {
            return false;
        }

        throw new InvalidOperationException(UnexpectedStatusMessage);
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        try
        {
            DisposeOwnedResources(failures);
        }
        finally
        {
            GC.KeepAlive(corpus);
            GC.KeepAlive(readScratch);
        }
        RawStorageFixtureFailures.Throw(null, failures);
    }

    private FixedSpanByteKey CreateKey(int index)
        => (FixedSpanByteKey)PinnedSpanByte.FromPinnedSpan(corpus.Key(index).Span);

    private static void EnsureTerminal(Status status)
    {
        if (status.IsPending || status.IsFaulted || status.IsCanceled || !status.IsCompletedSuccessfully)
        {
            throw new InvalidOperationException(UnexpectedStatusMessage);
        }
    }

    private void DisposeAfterFailure(Exception primary)
    {
        var failures = new List<Exception>();
        DisposeOwnedResources(failures);
        RawStorageFixtureFailures.Throw(primary, failures);
    }

    private void DisposeOwnedResources(List<Exception> failures)
    {
        if (!DisposeSession(failures) || !DisposeOutput(failures) || !DisposeStore(failures))
        {
            return;
        }

        DisposeSettings(failures);
    }

    private bool DisposeSession(List<Exception> failures)
    {
        if (session is null)
        {
            return true;
        }

        try
        {
            session.Dispose();
            session = null;
            return true;
        }
        catch (Exception failure) when (RawStorageFixtureFailures.IsNonFatal(failure))
        {
            failures.Add(failure);
            return false;
        }
    }

    private bool DisposeOutput(List<Exception> failures)
    {
        if (!outputInitialized)
        {
            return true;
        }

        if (!RawStorageFixtureFailures.Capture(output.Dispose, failures))
        {
            return false;
        }

        output = default;
        outputInitialized = false;
        return true;
    }

    private bool DisposeStore(List<Exception> failures)
    {
        if (store is null)
        {
            return true;
        }

        try
        {
            store.Dispose();
            store = null;
            return true;
        }
        catch (Exception failure) when (RawStorageFixtureFailures.IsNonFatal(failure))
        {
            failures.Add(failure);
            return false;
        }
    }

    private void DisposeSettings(List<Exception> failures)
    {
        if (settings is null)
        {
            return;
        }

        try
        {
            settings.Dispose();
            settings = null;
        }
        catch (Exception failure) when (RawStorageFixtureFailures.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
    }
}
