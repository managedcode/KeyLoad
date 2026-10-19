namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>The public canonical request root, independent of any execution schema implementation.</summary>
internal enum McpExpectedBody
{
    /// <summary>The tool accepts no arguments.</summary>
    None,
    /// <summary>The tool requires its actual canonical object request.</summary>
    Object,
    /// <summary>The dispatch control tool requires a boolean request.</summary>
    Boolean
}
