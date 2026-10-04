using System.Collections.Immutable;
using KeyLoad.Orleans;

namespace KeyLoad.Server;

/// <summary>Additive explicit official MCP/agent bindings for the administration slice.</summary>
internal static class AdminDashboardMcpCatalog
{
    private const string SnapshotDescription = "Observe the actual physical node as a persisted administrator. "
        + "Physical file bytes and process HTTP counters are bounded observations, not unique cluster totals.";
    private const string ResourcesDescription = "List bounded nonsecret resource metadata in an explicit tenant/database scope "
        + "as a persisted administrator; continue using nextAfterName.";
    private const string QueueDescription = "Browse persisted queue counters and bounded lifecycle metadata as a persisted "
        + "administrator without receiving, sweeping, claiming or acknowledging messages; continue using nextAfterId.";

    internal static ImmutableArray<McpOperationDescriptor> Entries { get; } =
    [
        McpOperationFactory.Read<AdminNodeSnapshot>(AdminDashboardProtocol.SnapshotTool,
            AdminDashboardProtocol.SnapshotPath, GrainReadKind.AdminDashboard),
        McpOperationFactory.Read<AdminResourcesRequest, AdminResourcesPage>(AdminDashboardProtocol.ResourcesTool,
            AdminDashboardProtocol.ResourcesPath, GrainReadKind.AdminResources),
        McpOperationFactory.Read<AdminQueueRequest, AdminQueuePage>(AdminDashboardProtocol.QueueTool,
            AdminDashboardProtocol.QueuePath, GrainReadKind.AdminQueue)
    ];

    internal static string Description(string name) => name switch
    {
        AdminDashboardProtocol.SnapshotTool => SnapshotDescription,
        AdminDashboardProtocol.ResourcesTool => ResourcesDescription,
        AdminDashboardProtocol.QueueTool => QueueDescription,
        _ => throw new ArgumentOutOfRangeException(nameof(name), McpCatalogProtocol.InvalidOperation)
    };
}
