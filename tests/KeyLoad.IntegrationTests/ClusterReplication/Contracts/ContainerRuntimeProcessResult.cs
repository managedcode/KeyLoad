namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Retains the original actual CLI exit code and standard streams.</summary>
/// <param name="ExitCode">The actual child exit code.</param>
/// <param name="StandardOutput">The unchanged captured standard output.</param>
/// <param name="StandardError">The unchanged captured standard error.</param>
internal sealed record ContainerRuntimeProcessResult(int ExitCode, string StandardOutput, string StandardError);
