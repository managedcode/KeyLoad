namespace KeyLoad.SiteTests.Features.BenchmarkComparisons;

internal static class SiteVendorTokens
{
    public const string FeatureDirectory = SiteAssetTokens.FeatureRelativePath;
    public const string FeatureOutputDirectory = "Features/BenchmarkComparisons";
    public const string BuildEntry = SiteAssetTokens.BuilderRelativePath;
    public const string AggregateScriptDirectory = "scripts/Features/BenchmarkComparisons";
    public static readonly string[] RootAssets =
    ["favicon.ico", "favicon-32x32.png", "favicon-96x96.png", "apple-touch-icon.png", "icon-192.png", "icon-512.png"];
    public const string ManifestPackage = "package";
    public const string ManifestVersion = "version";
    public const string ManifestLicense = "license";
    public const string ManifestSourceCommit = "sourceCommit";
    public const string ManifestIntegrity = "integrity";
    public const string ManifestFiles = SiteAssetTokens.Files;
    public const string ManifestPath = SiteAssetTokens.Path;
    public const string ManifestSha256 = SiteTokens.Sha256;
    public const string ManifestBytes = "bytes";
    public const string ManifestGzipBytes = "gzipBytes";
    public const string VendorError = SiteBuilderTokens.VendorError;
    public const string WrongPackage = "three-other";
    public const string WrongVersion = "0.186.0";
    public const string WrongLicense = "UNLICENSED";
    public const string WrongSourceCommit = "0000000000000000000000000000000000000000";
    public const string WrongIntegrity = "sha512-invalid";
    public const string WrongPath = "foreign-vendor.js";
    public const string WrongSha256 = "0000000000000000000000000000000000000000000000000000000000000000";
    public const string InvalidGzipText = "one";
    public const string Compression = "compression";
    public const string NodeVersion = "nodeVersion";
    public const string ZlibVersion = "zlibVersion";
    public const string Vendor = "vendor";
    public const string Output = "output";
    public const string RecordedGzipBytesProperty = "recordedGzipBytes";
    public const string RuntimeGzipBytes = "runtimeGzipBytes";
    public const string JsonSha256 = SiteTokens.Sha256;
    public const string JavascriptGzipBytes = "javascriptGzipBytes";
    public const string CssGzipBytes = "cssGzipBytes";
    public const string NodeTypeArgument = "--input-type=module";
    public const string NodeEvalArgument = "-e";
    public const string SearchAllEntries = "*";
    public const string NodeOutputExceeded = "The vendor gzip oracle exceeded its bounded process output.";
    public const string OracleDidNotStart = "The independent Node gzip oracle did not start.";
    public const string OracleFailure = "The independent Node gzip oracle failed.";
    public const string ScopeNotInitialized = "The isolated vendor test scope is not initialized.";
    public const int HistoricalGzipSize = 1;
    public const int ZeroGzipBytes = 0;
    public const int NegativeGzipBytes = -1;
    public const double FractionalGzipBytes = 1.5;
    public const long UnsafeGzipBytes = 9_007_199_254_740_992L;
    public const int AuthoredJavaScriptGzipLimit = 40_960;
    public const int AuthoredCssGzipLimit = 20_480;
    public const int FirstByteIndex = SiteTokens.Zero;
    public const int FirstFileIndex = SiteTokens.Zero;
    public const byte ByteChangeMask = 1;
    public const string GzipOracleProgram = """
        import { readFile } from 'node:fs/promises';
        import { basename } from 'node:path';
        import { createHash } from 'node:crypto';
        import { gzipSync } from 'node:zlib';
        const vendor = [];
        for (const path of process.argv.slice(1)) {
          const bytes = await readFile(path);
          vendor.push({
            path: basename(path),
            sha256: createHash('sha256').update(bytes).digest('hex'),
            bytes: bytes.length,
            runtimeGzipBytes: gzipSync(bytes).length
          });
        }
        console.log(JSON.stringify({
          nodeVersion: process.versions.node,
          zlibVersion: process.versions.zlib,
          vendor
        }));
        """;
    public static readonly string[] VendorFiles =
    [
        SiteAssetTokens.WebGpuVendorModule,
        SiteAssetTokens.CoreVendorModule,
        SiteAssetTokens.VendorLicense,
    ];
}
