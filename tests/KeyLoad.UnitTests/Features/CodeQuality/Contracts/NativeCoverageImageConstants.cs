namespace KeyLoad.UnitTests.Features.CodeQuality;

internal static class NativeCoverageImageConstants
{
    internal const string ServerDll = "KeyLoad.Server.dll";
    internal const string ServerPdb = "KeyLoad.Server.pdb";
    internal const string ContextManifest = "identity/context-manifest.json";
    internal const string RuntimeIdentity = "identity/runtime-identity.env";
    internal const string ServerReceipt = "identity/server-source-receipt.json";
    internal const string BaseReceipt = "identity/base-image-receipt.json";
    internal const string ServerReceiptName = "server-source-receipt";
    internal const string BaseReceiptName = "base-image-receipt";
    internal const string Dockerfile = "Dockerfile";
    internal const string Settings = "settings.xml";
    internal const string Wrapper = "server-wrapper.sh";
    internal const string Lifecycle = "server-lifecycle.sh";
    internal const string Target = "server-target.sh";
    internal const string DotnetCoverageDeps = "dotnet-coverage.deps.json";
    internal const string DotnetCoverageRuntime = "dotnet-coverage.runtimeconfig.json";
    internal const string DotnetCoverageToolDirectory = "tools/net8.0/any";
    internal const string DotnetCoverageTargetFramework = ".NETCoreApp,Version=v8.0";
    internal const int MaximumJsonDepth = 64;
    internal const int InitialOutputCapacity = 512;
    internal const int SchemaVersion = 1;
    internal const string LinuxNativeDirectory = "ubuntu/x64";
    internal const string CoveragePackageId = "dotnet-coverage";
    internal const string LinuxInstrumentationEngine = "libInstrumentationEngine.so";
    internal const string LinuxCoverageInstrumentationMethod = "libCoverageInstrumentationMethod.so";
    internal const string LinuxCoverageConfig = "Cov_x64.config";
    internal const string License = "License.txt";
    internal const string ThirdPartyNotices = "ThirdPartyNotices.txt";
    internal const string SentinelName = "sentinel.bin";
    internal const string FailureOutput = "Native coverage image materialization failed.\n";
    internal const string LocalEvidenceKind = "local-test-observed-inputs";
    internal const int RejectedExitCode = 1;
    internal const int PrivateDirectoryMode = 0x1c0;
    internal const int ContextManifestMode = 0x1a4;
    internal static readonly byte[] SentinelBytes = [0x4b, 0x4c, 0x49, 0x4e, 0x45];
}
