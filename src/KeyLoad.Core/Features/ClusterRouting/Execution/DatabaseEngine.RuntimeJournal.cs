using KeyLoad.Core.Features.ClusterRouting;
using Microsoft.Extensions.Options;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private RuntimeJournalOperations? runtimeJournal;

    /// <summary>Configures the private journal before physical recovery and request admission.</summary>
    /// <param name="options">The centrally validated immutable journal limits.</param>
    public void ConfigureRuntimeJournal(IOptions<RuntimeJournalOptions> options)
    {
        if (Store.Identity.MinimumReaderContract != StoreReaderContract.RuntimeJournal)
        {
            throw Errors.Fail(ErrorCode.FormatUnsupported, "Runtime journal ownership requires its persisted reader contract.");
        }
        var configured = new RuntimeJournalOperations(this, options);
        if (Interlocked.CompareExchange(ref runtimeJournal, configured, null) is not null)
        {
            throw new InvalidOperationException("Runtime journal ownership has already been configured.");
        }
    }

    /// <summary>Reads an authorized private journal header at the node-local read cut.</summary>
    /// <param name="principalId">The current protected principal identifier.</param>
    /// <param name="journalName">The exact opaque native journal identifier.</param>
    /// <returns>The current header, or null when the journal does not exist.</returns>
    public RuntimeJournalSnapshot? GetRuntimeJournalHeader(string principalId, string journalName)
        => RuntimeJournal.GetHeader(principalId, journalName);

    /// <summary>Reads one bounded journal byte page pinned to a recovered generation.</summary>
    /// <param name="principalId">The current protected principal identifier.</param>
    /// <param name="request">The captured journal generation and byte offset.</param>
    /// <returns>Owned opaque bytes and the completion marker.</returns>
    public RuntimeJournalPage ReadRuntimeJournal(string principalId, RuntimeJournalReadRequest request)
        => RuntimeJournal.Read(principalId, request);

    /// <summary>Reads the complete bounded native journal catalog.</summary>
    /// <param name="principalId">The current protected principal identifier.</param>
    /// <returns>Every admitted journal header at one authorized read cut.</returns>
    public RuntimeJournalCatalog ReadRuntimeJournalCatalog(string principalId)
        => RuntimeJournal.Catalog(principalId);

    private RuntimeJournalOperations RuntimeJournal => Volatile.Read(ref runtimeJournal)
        ?? throw Errors.Fail(ErrorCode.RecoveryRequired, "Runtime journal ownership is not configured.");
}
