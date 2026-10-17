namespace KeyLoad.Analyzers.Tests.Features.CodeQuality;

/// <summary>AC-CQ-037: const aliases are followed only when their native semantic storage is never reassigned.</summary>
internal sealed class TypedConfigurationFileBufferAliasTests
{
    [Test]
    public async Task NeverReassignedLocalAndReadonlyInstanceAliasesRemainHardcodedPolicyAsync()
    {
        const string source = """
            internal sealed class Subject
            {
                private const int BufferBytes = 65536;
                private readonly int buffer = BufferBytes;
                internal void Execute(string path, System.IO.Stream stream)
                {
                    var localBuffer = BufferBytes;
                    var alias = localBuffer;
                    using var file = [|new System.IO.FileStream(path, System.IO.FileMode.Open,
                        System.IO.FileAccess.Read, System.IO.FileShare.Read, alias)|];
                    using var writer = [|new System.IO.StreamWriter(stream, System.Text.Encoding.UTF8, buffer, leaveOpen: true)|];
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

    [Test]
    public async Task ActualOptionsCapturedAndReassignedScalarBuffersRemainConfiguredAsync()
    {
        const string source = """
            [KeyLoad.ConfigurationOptions]
            internal sealed class Policy { public int BufferBytes { get; init; } = 65536; }
            internal sealed class Subject
            {
                private const int FormerBuffer = 65536;
                private readonly int buffer = FormerBuffer;
                internal Subject(Microsoft.Extensions.Options.IOptions<Policy> configured)
                {
                    buffer = configured.Value.BufferBytes;
                }
                internal void Execute(System.IO.Stream stream, Microsoft.Extensions.Options.IOptions<Policy> configured)
                {
                    var reassigned = FormerBuffer;
                    reassigned = configured.Value.BufferBytes;
                    var captured = configured.Value.BufferBytes;
                    using var writer = new System.IO.StreamWriter(stream, System.Text.Encoding.UTF8, buffer, leaveOpen: true);
                    using var other = new System.IO.StreamWriter(stream, System.Text.Encoding.UTF8, reassigned, leaveOpen: true);
                    using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8,
                        detectEncodingFromByteOrderMarks: true, bufferSize: captured, leaveOpen: true);
                }
            }
            """;

        await MagicRuntimeFixture.AssertConfigurationAsync(source);
    }

}
