using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

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
            throw Errors.Fail(ErrorCode.FormatUnsupported, RuntimeJournalProtocol.ReaderContractRequired);
        }
        var configured = new RuntimeJournalOperations(this, options);
        if (Interlocked.CompareExchange(ref runtimeJournal, configured, null) is not null)
        {
            throw new InvalidOperationException(RuntimeJournalProtocol.AlreadyConfigured);
        }
    }

    /// <summary>Reads an authorized private journal header at the node-local read cut.</summary>
    /// <param name="principalId">The current protected principal identifier.</param>
    /// <param name="journalName">The exact opaque native journal identifier.</param>
    /// <param name="cancellationToken">Bounds catalog validation and the gated read.</param>
    /// <returns>The current header, or null when the journal does not exist.</returns>
    public RuntimeJournalSnapshot? GetRuntimeJournalHeader(string principalId, string journalName,
        CancellationToken cancellationToken = default)
        => RuntimeJournal.GetHeader(principalId, journalName, cancellationToken);

    /// <summary>Reads one bounded journal byte page pinned to a recovered generation.</summary>
    /// <param name="principalId">The current protected principal identifier.</param>
    /// <param name="request">The captured journal generation and byte offset.</param>
    /// <param name="cancellationToken">Bounds journal validation and byte traversal.</param>
    /// <returns>Owned opaque bytes and the completion marker.</returns>
    public RuntimeJournalPage ReadRuntimeJournal(string principalId, RuntimeJournalReadRequest request,
        CancellationToken cancellationToken = default)
        => RuntimeJournal.Read(principalId, request, cancellationToken);

    /// <summary>Reads the complete bounded native journal catalog.</summary>
    /// <param name="principalId">The current protected principal identifier.</param>
    /// <param name="cancellationToken">Bounds complete catalog validation.</param>
    /// <returns>Every admitted journal header at one authorized read cut.</returns>
    public RuntimeJournalCatalog ReadRuntimeJournalCatalog(string principalId, CancellationToken cancellationToken = default)
        => RuntimeJournal.Catalog(principalId, cancellationToken);

    private RuntimeJournalOperations RuntimeJournal => Volatile.Read(ref runtimeJournal)
        ?? throw Errors.Fail(ErrorCode.RecoveryRequired, RuntimeJournalProtocol.NotConfigured);
}
