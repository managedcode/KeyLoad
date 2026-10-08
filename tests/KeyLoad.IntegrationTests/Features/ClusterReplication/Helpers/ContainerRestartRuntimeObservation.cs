namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Observes native fixed-name replacement creation without treating absence as Docker failure.</summary>
internal static class ContainerRestartRuntimeObservation
{
    private const string ContainerCommand = "container";
    private const string ListCommand = "ls";
    private const string AllArgument = "--all";
    private const string FullIdArgument = "--no-trunc";
    private const string FilterArgument = "--filter";
    private const string NameFilterPrefix = "name=^/";
    private const string NameFilterSuffix = "$";
    private const string ListFormat = "{{.ID}}|{{.Names}}";
    private const int ObservationFieldCount = 2;
    private const string InvalidObservation = "The native replacement observation does not identify the exact owned container.";

    internal static async Task<ContainerRuntimeInspection?> ReadAsync(string containerName,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        if (containerName.Any(character => !char.IsAsciiLetterOrDigit(character)
            && character is not '-' and not '_'))
        { throw new InvalidOperationException(InvalidObservation); }
        var listed = await ContainerRuntimeDocker.RunAsync([ContainerCommand, ListCommand,
            AllArgument, FullIdArgument, FilterArgument, NameFilterPrefix + containerName + NameFilterSuffix,
            ContainerRuntimeProtocol.FormatArgument, ListFormat], cancellationToken);
        ContainerRuntimeDocker.EnsureSuccessful(listed, ListCommand, containerName);
        var value = listed.StandardOutput.Trim();
        if (value.Length == 0)
        { return null; }
        var fields = value.Split(ContainerRuntimeProtocol.InspectSeparator);
        if (fields.Length != ObservationFieldCount || fields[0].Length != ContainerRuntimeProtocol.FullContainerIdCharacters
            || !fields[0].All(Uri.IsHexDigit) || fields[1] != containerName)
        { throw new InvalidOperationException(InvalidObservation); }
        return await ContainerRuntimeDocker.InspectAsync(fields[0], cancellationToken);
    }
}
