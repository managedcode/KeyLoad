using System.Collections.Immutable;
using KeyLoad.Clustering;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class McpAdminDashboardCatalogEntries
{
    private const string DashboardName = "keyload_admin_dashboard";
    private const string DashboardRoute = "/v1/admin/dashboard";
    private const string ResourcesListName = "keyload_admin_resources_list";
    private const string ResourcesListRoute = "/v1/admin/dashboard/resources";
    private const string QueueBrowseName = "keyload_admin_queue_browse";
    private const string QueueBrowseRoute = "/v1/admin/dashboard/queue";
    internal static ImmutableArray<(string Name, string Route, GrainReadKind? ReadKind, OperationKind? CommandKind)> All
    {
        get;
    } =
    [
        (DashboardName, DashboardRoute, GrainReadKind.AdminDashboard, null),
        (ResourcesListName, ResourcesListRoute, GrainReadKind.AdminResources, null),
        (QueueBrowseName, QueueBrowseRoute, GrainReadKind.AdminQueue, null),
    ];
}
