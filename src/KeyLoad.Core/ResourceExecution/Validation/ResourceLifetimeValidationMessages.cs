namespace KeyLoad.Core.Features.ResourceExecution;

internal static class ResourceLifetimeValidationMessages
{
    internal const string Invalid = "The resource lifetime coverage is invalid.";
    internal const string Capacity = "The resource lifetime coverage exceeds its byte budget.";
    internal const string MissingAuthority = "The current resource lifetime is unavailable.";
}
