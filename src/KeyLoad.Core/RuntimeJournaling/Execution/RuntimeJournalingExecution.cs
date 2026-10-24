using KeyLoad.Core.RuntimeJournaling.Contracts;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;
/// <summary>Executes RuntimeJournalingExecution operations against the caller's database context.</summary>
public static class RuntimeJournalingExecution
{
    /// <summary>Configures the private journal before physical recovery and request admission.</summary>
    /// <param name = "options">The centrally validated immutable journal limits.</param>

    /// <param name = "engine">The shared database execution context.</param>
    public static void ConfigureRuntimeJournal(this DatabaseEngine engine, IOptions<RuntimeJournalOptions> options)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (engine.Store.Identity.MinimumReaderContract != StoreReaderContract.RuntimeJournal)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, RuntimeJournalProtocol.ReaderContractRequired);
        }

        var configured = new RuntimeJournalOperations(engine, options);
        if (Interlocked.CompareExchange(ref engine.configuredRuntimeJournal, configured, null) is not null)
        {
            throw new InvalidOperationException(RuntimeJournalProtocol.AlreadyConfigured);
        }
    }

    /// <summary>Reads an authorized private journal header at the node-local read cut.</summary>
    /// <param name = "principalId">The current protected principal identifier.</param>
    /// <param name = "journalName">The exact opaque execution journal identifier.</param>
    /// <param name = "cancellationToken">Bounds catalog validation and the gated read.</param>
    /// <returns>The current header, or null when the journal does not exist.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static RuntimeJournalSnapshot? GetRuntimeJournalHeader(this DatabaseEngine engine, string principalId, string journalName, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.RuntimeJournal.GetHeader(principalId, journalName, cancellationToken);
    }

    /// <summary>Reads one bounded journal byte page pinned to a recovered generation.</summary>
    /// <param name = "principalId">The current protected principal identifier.</param>
    /// <param name = "request">The captured journal generation and byte offset.</param>
    /// <param name = "cancellationToken">Bounds journal validation and byte traversal.</param>
    /// <returns>Owned opaque bytes and the completion marker.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static RuntimeJournalPage ReadRuntimeJournal(this DatabaseEngine engine, string principalId, RuntimeJournalReadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.RuntimeJournal.Read(principalId, request, cancellationToken);
    }

    /// <summary>Reads the complete bounded execution journal catalog.</summary>
    /// <param name = "principalId">The current protected principal identifier.</param>
    /// <param name = "cancellationToken">Bounds complete catalog validation.</param>
    /// <returns>Every admitted journal header at one authorized read cut.</returns>

    /// <param name = "engine">The shared database execution context.</param>
    public static RuntimeJournalCatalog ReadRuntimeJournalCatalog(this DatabaseEngine engine, string principalId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);
        return engine.RuntimeJournal.Catalog(principalId, cancellationToken);
    }
}
