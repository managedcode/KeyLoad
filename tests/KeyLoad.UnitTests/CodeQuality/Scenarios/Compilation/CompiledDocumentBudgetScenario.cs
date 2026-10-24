using System.Text;
using KeyLoad.Server;
using KeyLoad.UnitTests.CodeQuality.Assertions;
using KeyLoad.UnitTests.CodeQuality.Processes;

namespace KeyLoad.UnitTests.CodeQuality.Helpers;

internal static class CompiledDocumentBudgetScenario
{
    internal const string GeneratedDirectory = "obj";
    internal const string GeneratedFile = "execution-generated.cs";
    private const string GeneratedText = """
        public static class GeneratedIdentityFixture
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
        PathMapFixture? fixture = null;
        ServerFailureObserver.Observe(() => fixture = new PathMapFixture(), failures);
        if (fixture is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var directory = Path.Combine(fixture.Root, GeneratedDirectory);
                Directory.CreateDirectory(directory);
                var generated = Path.Combine(directory, GeneratedFile);
                await File.WriteAllBytesAsync(generated, GeneratedBytes, token);
                await PathMapCompiler.CompileAdditionalAsync(fixture, generated, token);
                var dll = await File.ReadAllBytesAsync(fixture.DllPath, token);
                var pdb = await File.ReadAllBytesAsync(fixture.PdbPath, token);
                var result = await CompiledDocumentBudgetReader.ReadAsync(fixture, token);
                await CompiledDocumentBudgetAssertions.VerifyAsync(result);
                await PathMapAssertions.ImagesUnchangedAsync(fixture, dll, pdb, token);
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.Observe(fixture.Dispose, failures);
        }
        global::KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }
}
