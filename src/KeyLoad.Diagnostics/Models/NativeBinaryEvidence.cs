namespace KeyLoad.Diagnostics.Features.ResourceExecution;

/// <summary>Observed native launch files and the source metadata captured by their actual compilation.</summary>
/// <param name="ExecutablePath">The executable observed on the live operating-system process.</param>
/// <param name="ExecutableSha256">SHA-256 of that executable.</param>
/// <param name="AssemblyPath">The managed entry assembly resolved from the actual native launch.</param>
/// <param name="AssemblySha256">SHA-256 of that managed assembly.</param>
/// <param name="PdbPath">The matching associated symbol file in the observed managed assembly's directory.</param>
/// <param name="PdbSha256">SHA-256 of that symbol file.</param>
/// <param name="SourceCount">The number of source inputs recorded by the compilation identity producer.</param>
/// <param name="SourceCompositionSha256">SHA-256 of the ordered original source, central-input and producer metadata.</param>
public sealed record NativeBinaryEvidence(string ExecutablePath, string ExecutableSha256,
    string AssemblyPath, string AssemblySha256, string PdbPath, string PdbSha256,
    int SourceCount, string SourceCompositionSha256);
