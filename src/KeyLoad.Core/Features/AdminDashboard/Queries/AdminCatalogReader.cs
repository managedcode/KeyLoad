using KeyLoad.Storage;

namespace KeyLoad.Core;

/// <summary>Reads bounded nonsecret catalog metadata from the physical owner's gated store.</summary>
/// <param name="database">Borrowed canonical engine.</param>
public sealed class AdminCatalogReader(DatabaseEngine database)
{
    private const string CatalogSpace = "catalog";

    /// <summary>Reads one metadata page after enforcing current persisted administrator authority.</summary>
    /// <param name="principalId">Persisted principal identifier.</param>
    /// <param name="request">Explicit catalog scope and exclusive continuation.</param>
    /// <param name="cancellationToken">Cancellation of bounded read work.</param>
    /// <returns>Metadata only, with the applied position from the same gated cut.</returns>
    public AdminResourcesPage Read(string principalId, AdminResourcesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        JsonData.Identifier(request.TenantId);
        JsonData.Identifier(request.DatabaseId);
        AdminReadAuthority.ValidatePage(request.Limit, request.AfterName);
        var budget = new ReadExecutionBudget(database.Limits, database.EvaluationClock, cancellationToken);
        return database.Store.Read(view => ReadPage(budget.CreateView(view), budget, principalId, request));
    }

    private AdminResourcesPage ReadPage(IKeyValueView view, ReadExecutionBudget budget, string principalId,
        AdminResourcesRequest request)
    {
        AdminReadAuthority.Require(database, view, principalId);
        var items = new List<AdminResourceInfo>();
        var after = request.AfterName is null ? null : KeySpace.Resource(request.TenantId, request.DatabaseId, request.AfterName);
        var scan = view.VisitRange(KeyCodec.Encode(CatalogSpace, request.TenantId, request.DatabaseId), request.Limit,
            (_, value) =>
            {
                var resource = NativeSerialization.Deserialize<ResourceDefinition>(value);
                items.Add(new(resource.Name, resource.Kind, resource.TransactionDomainId,
                    resource.SchemaVersion, resource.Indexes.Length, resource.Paused));
                return true;
            }, after, cancellationToken: budget.Cancellation);
        var page = new AdminResourcesPage([.. items], scan.HasMore ? items[^1].Name : null, AdminReadAuthority.Position(view));
        budget.CheckResult(page);
        return page;
    }
}
