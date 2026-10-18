using System.Text;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.CodeQuality.Assertions;
using KeyLoad.UnitTests.Features.CodeQuality.Processes;

namespace KeyLoad.UnitTests.Features.CodeQuality.Helpers;

internal static class NativeCompiledDocumentBudgetScenario
{
    internal const string GeneratedDirectory = "obj";
    internal const string GeneratedFile = "native-generated.cs";
    private const string GeneratedText = """
        public static class NativeGeneratedIdentityFixture
        {
            public static int Identity(int value)
            {
                return value;
            }
        }
        """;
    internal static byte[] GeneratedBytes => Encoding.UTF8.GetBytes(GeneratedText);

    internal static async Task RunAsync(CancellationToken token)
    {
        var failures = new List<Exception>();
        NativePathMapFixture? fixture = null;
        ServerFailureObserver.Observe(() => fixture = new NativePathMapFixture(), failures);
        if (fixture is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var directory = Path.Combine(fixture.Root, GeneratedDirectory);
                Directory.CreateDirectory(directory);
                var generated = Path.Combine(directory, GeneratedFile);
                await File.WriteAllBytesAsync(generated, GeneratedBytes, token);
                await NativePathMapCompiler.CompileAdditionalAsync(fixture, generated, token);
                var dll = await File.ReadAllBytesAsync(fixture.DllPath, token);
                var pdb = await File.ReadAllBytesAsync(fixture.PdbPath, token);
                var result = await NativeCompiledDocumentBudgetReader.ReadAsync(fixture, token);
                await NativeCompiledDocumentBudgetAssertions.VerifyAsync(result);
                await NativePathMapAssertions.ImagesUnchangedAsync(fixture, dll, pdb, token);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.Observe(fixture.Dispose, failures);
        }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
