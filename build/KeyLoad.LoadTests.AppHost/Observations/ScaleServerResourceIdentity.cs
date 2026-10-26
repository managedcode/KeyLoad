namespace KeyLoad.LoadTests.AppHost.BenchmarkComparisons;

internal static class ScaleServerResourceIdentity
{
    private const string ContainerId = "container.id", Invalid = "ServerResourceContainerIdentityUnavailable";
    private const int IdentityLength = 64;

    internal static string Read(ResourceNotificationService notifications, ContainerResource resource)
    {
        if (!notifications.TryGetCurrentState(resource.Name, out var current)
            || current.Resource is not ContainerResource || current.Resource.Name != resource.Name
            || current.Snapshot.State?.Text != KnownResourceStates.Running
            || current.Snapshot.Properties.SingleOrDefault(property => property.Name == ContainerId)?.Value is not string identity
            || identity.Length != IdentityLength || identity.Any(character => !char.IsAsciiHexDigitLower(character)))
        {
            throw new InvalidDataException(Invalid);
        }
        return identity;
    }
}
