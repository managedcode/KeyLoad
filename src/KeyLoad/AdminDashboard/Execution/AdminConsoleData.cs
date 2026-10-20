using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using KeyLoad.Client;
using KeyLoad.Query;
using ManagedCode.Communication;
using ManagedCode.Storage.Core.Models;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server;

internal sealed class AdminConsoleData(IOptions<AdminConsoleOptions> execution)
{
    private readonly AdminConsoleOptions options = execution.Value;
    private static readonly CompositeFormat QueueCounts = CompositeFormat.Parse(AdminConsoleText.QueueCounts);
    private const long EmptyCut = 0;
    private const string NextFile = "X-KeyLoad-Next-File";
    internal ImmutableArray<AdminResourceInfo> Resources { get; private set; } = [];
    internal ImmutableArray<AdminResourceInfo> Catalog { get; private set; } = [];
    internal ImmutableArray<QueryRow> Rows { get; private set; } = [];
    internal ImmutableArray<AdminQueueItem> Messages { get; private set; } = [];
    internal BlobMetadata[] Files { get; private set; } = [];
    internal AdminResourceInfo? Selected { get; set; }
    internal string? ResourceAfter { get; private set; }
    internal string? After { get; private set; }
    internal string Description { get; private set; } = AdminConsoleText.Records;
    internal long Cut { get; private set; }

    internal void ClearRows()
    {
        Rows = [];
        Messages = [];
        Files = [];
        Selected = null;
        After = null;
        Cut = EmptyCut;
        Description = AdminConsoleText.Records;
    }

    internal void Clear()
    {
        ClearRows();
        Resources = [];
        Catalog = [];
        ResourceAfter = null;
    }

    internal void SetResources(AdminResourcesPage page, string view)
    {
        Catalog = page.Items;
        ResourceAfter = page.NextAfterName;
        var kind = view switch { AdminConsoleText.QueueView => ResourceKind.WorkQueue, AdminConsoleText.FilesView => ResourceKind.BlobStore, _ => ResourceKind.Collection };
        Resources = [.. page.Items.Where(item => item.Kind == kind)];
    }

    internal async Task LoadAsync(KeyLoadClient client, HttpClient http, PartitionRef partition, string? after, CancellationToken token)
    {
        var resource = Selected ?? throw new InvalidOperationException(AdminConsoleText.NoResource);
        switch (resource.Kind)
        {
            case ResourceKind.Collection:
                var query = new SelectQuery(resource.Name, null, [new(AdminConsoleText.Wildcard, AdminConsoleText.Wildcard)], null, [], options.RowLimit);
                var rows = AdminConsoleSession.Value(await client.QueryAstAsync(new(partition, query, AllowFullScan: true, Cursor: after), token));
                token.ThrowIfCancellationRequested();
                Rows = rows.Rows;
                After = rows.Cursor;
                Cut = rows.CutPosition;
                break;
            case ResourceKind.WorkQueue:
                var queue = AdminConsoleSession.Value(await client.BrowseQueueAsync(new(new(partition, resource.Name), after), token));
                token.ThrowIfCancellationRequested();
                Messages = queue.Items;
                After = queue.NextAfterId;
                Cut = queue.CutPosition;
                Description = string.Format(CultureInfo.InvariantCulture, QueueCounts, queue.Counters.StoredMessages, queue.Counters.InFlightMessages);
                break;
            case ResourceKind.BlobStore:
                await LoadFilesAsync(http, partition, resource.Name, after, token);
                break;
            default:
                Description = AdminConsoleText.Unsupported;
                break;
        }
    }

    private async Task LoadFilesAsync(HttpClient http, PartitionRef partition, string resource, string? after, CancellationToken token)
    {
        var parts = new[] { partition.TenantId, partition.DatabaseId, partition.TransactionDomainId, partition.PartitionKey, resource };
        var path = AdminConsoleText.StorageRoot + string.Join(AdminConsoleText.PathSeparator, parts.Select(Uri.EscapeDataString)) + AdminConsoleText.StorageList;
        if (after is not null)
        {
            path += AdminConsoleText.AfterQuery + Uri.EscapeDataString(after);
        }
        using var response = await http.GetAsync(new Uri(path, UriKind.Relative), token);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<Result<BlobMetadata[]>>(JsonDefaults.Options, token);
        token.ThrowIfCancellationRequested();
        Files = AdminConsoleSession.Value(result);
        After = response.Headers.TryGetValues(NextFile, out var values) ? values.Single() : null;
        Description = AdminConsoleText.Files;
    }
}
