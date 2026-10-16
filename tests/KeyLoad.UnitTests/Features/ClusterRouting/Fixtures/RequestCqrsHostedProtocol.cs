namespace KeyLoad.UnitTests.Features.ClusterRouting;

internal static class RequestCqrsHostedProtocol
{
    internal const string Tenant = "tenant";
    internal const string Database = "database";
    internal const string Domain = "orders";
    internal const string Partition = "customer-1";
    internal const string ProfileRequired = "The hosted CQRS profile requires its own native cohort.";
    internal const string LocalNotInitialized = "The native hosted local silo is not initialized.";
    internal const string CatalogAbsent = "The native hosted catalog owner is absent.";
    internal const string OwnerAbsent = "The native hosted owner is absent.";
    internal const string ClientAbsent = "The native hosted client profile is absent.";
    internal const string LocalOwnerAbsent = "The native hosted local owner is absent.";
    internal const string CoordinatorAbsent = "The native hosted coordinator is absent.";
    internal const string WorkNotJoined = "The native hosted request work has not joined.";
    internal const string CohortWorkNotJoined = "The original hosted CQRS work has not joined.";
    internal const string LeaderEvaluationTime = "The native leader owns the operation evaluation time.";
}
