namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>Defines bounded execution of analyzer coverage child processes.</summary>
[KeyLoad.ConfigurationOptions]
internal sealed class CoverageProcessOptions
{
    /// <summary>Gets or sets the maximum duration of one coverage child process.</summary>
    public TimeSpan ProcessTimeout { get; set; } = TimeSpan.FromSeconds(30);
    /// <summary>Gets or sets the deadline for joined child-process cleanup.</summary>
    public TimeSpan CleanupTimeout { get; set; } = TimeSpan.FromSeconds(3);
    /// <summary>Gets or sets the number of admitted coverage child processes.</summary>
    public int MaximumConcurrentProcesses { get; set; } = 2;
}
