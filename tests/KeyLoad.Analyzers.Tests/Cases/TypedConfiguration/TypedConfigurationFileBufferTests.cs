namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-034/037: exact native file buffers require configured execution policy.</summary>
internal sealed class TypedConfigurationFileBufferTests
{
    [Test]
    public async Task NativeFileBufferConstsAndReadonlyInitializersAreRejectedAsync()
    {
        const string source = """
            internal static class Subject
            {
                private const int FileBuffer = 65536;
                private static readonly int TextBuffer = 16384;
                internal static void Execute(string path, System.IO.Stream stream, System.IO.FileStreamOptions options)
                {
                    var configured = new System.IO.FileStreamOptions { [|BufferSize = FileBuffer|] };
                    [|options.BufferSize = TextBuffer|];
                    using var file = [|new System.IO.FileStream(path, System.IO.FileMode.Open,
                        System.IO.FileAccess.Read, System.IO.FileShare.Read, FileBuffer, System.IO.FileOptions.SequentialScan)|];
                    using var writer = [|new System.IO.StreamWriter(stream, System.Text.Encoding.UTF8, TextBuffer, leaveOpen: true)|];
                    using var reader = [|new System.IO.StreamReader(stream, System.Text.Encoding.UTF8,
                        detectEncodingFromByteOrderMarks: true, bufferSize: TextBuffer, leaveOpen: true)|];
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ActualOptionsValuesReachNativeFileAndTextBuffersAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int BufferBytes { get; init; } = 65536; }
            internal static class Subject
            {
                internal static void Execute(string path, System.IO.Stream stream,
                    Microsoft.Extensions.Options.IOptions<Policy> configured)
                {
                    var options = new System.IO.FileStreamOptions { BufferSize = configured.Value.BufferBytes };
                    using var file = new System.IO.FileStream(path, System.IO.FileMode.Open,
                        System.IO.FileAccess.Read, System.IO.FileShare.Read, configured.Value.BufferBytes);
                    using var writer = new System.IO.StreamWriter(stream, System.Text.Encoding.UTF8,
                        configured.Value.BufferBytes, leaveOpen: true);
                    using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8,
                        detectEncodingFromByteOrderMarks: true, bufferSize: configured.Value.BufferBytes, leaveOpen: true);
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task OmittedSdkDefaultsDoNotBecomeAuthoredFilePolicyAsync()
    {
        const string source = """
            internal static class Subject
            {
                internal static void Execute(string path, System.IO.Stream stream)
                {
                    using var file = new System.IO.FileStream(path, System.IO.FileMode.Open);
                    using var writer = new System.IO.StreamWriter(stream);
                    using var reader = new System.IO.StreamReader(stream);
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task SourceShadowTypesCannotForgeNativeFilePolicySymbolsAsync()
    {
        const string source = """
            namespace System.IO
            {
                internal sealed class FileStreamOptions { internal int BufferSize { get; set; } }
                internal sealed class FileStream { internal FileStream(int bufferSize) { } }
                internal sealed class StreamWriter { internal StreamWriter(int bufferSize) { } }
                internal sealed class StreamReader { internal StreamReader(int bufferSize) { } }
            }
            internal static class Subject
            {
                private const int ProtocolFieldWidth = 64;
                internal static void Execute()
                {
                    var options = new System.IO.FileStreamOptions { BufferSize = ProtocolFieldWidth };
                    var file = new System.IO.FileStream(ProtocolFieldWidth);
                    var writer = new System.IO.StreamWriter(ProtocolFieldWidth);
                    var reader = new System.IO.StreamReader(ProtocolFieldWidth);
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ProtocolMathAndApplicationBufferNamesRemainOutsideNativeSinksAsync()
    {
        const string source = """
            internal sealed class BufferOptions { internal int BufferSize { get; set; } }
            internal sealed class StreamWriter { internal StreamWriter(int bufferSize) { } }
            internal static class Subject
            {
                private const int ProtocolFieldWidth = 64;
                internal static void Execute()
                {
                    var options = new BufferOptions { BufferSize = ProtocolFieldWidth };
                    var writer = new StreamWriter(ProtocolFieldWidth);
                    var header = new byte[ProtocolFieldWidth];
                    var width = System.Math.Abs(ProtocolFieldWidth);
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ExplicitZeroBufferDoesNotGrantAGeneralPolicyExemptionAsync()
    {
        const string source = """
            internal static class Subject
            {
                private const int NoBuffer = 0;
                internal static void Execute(string path)
                {
                    var options = new System.IO.FileStreamOptions { [|BufferSize = NoBuffer|] };
                    using var file = [|new System.IO.FileStream(path, System.IO.FileMode.Open,
                        System.IO.FileAccess.Read, System.IO.FileShare.Read, NoBuffer)|];
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task NativeBaseConstructorsRejectHiddenBuffersAndAcceptActualOptionsAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int BufferBytes { get; init; } = 65536; }
            internal sealed class FixedFile : System.IO.FileStream
            {
                private const int BufferBytes = 65536;
                internal FixedFile(string path) [|: base(path, System.IO.FileMode.Open,
                    System.IO.FileAccess.Read, System.IO.FileShare.Read, BufferBytes)|] { }
            }
            internal sealed class FixedWriter : System.IO.StreamWriter
            {
                private const int BufferBytes = 16384;
                internal FixedWriter(System.IO.Stream stream) [|: base(stream, System.Text.Encoding.UTF8, BufferBytes)|] { }
            }
            internal sealed class FixedReader : System.IO.StreamReader
            {
                private const int BufferBytes = 16384;
                internal FixedReader(System.IO.Stream stream) [|: base(stream, System.Text.Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true, bufferSize: BufferBytes)|] { }
            }
            internal sealed class ConfiguredFile(string path, Microsoft.Extensions.Options.IOptions<Policy> options)
                : System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read,
                    System.IO.FileShare.Read, options.Value.BufferBytes);
            internal sealed class ConfiguredWriter(System.IO.Stream stream, Microsoft.Extensions.Options.IOptions<Policy> options)
                : System.IO.StreamWriter(stream, System.Text.Encoding.UTF8, options.Value.BufferBytes);
            internal sealed class ConfiguredReader(System.IO.Stream stream, Microsoft.Extensions.Options.IOptions<Policy> options)
                : System.IO.StreamReader(stream, System.Text.Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true, bufferSize: options.Value.BufferBytes);
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }
}
