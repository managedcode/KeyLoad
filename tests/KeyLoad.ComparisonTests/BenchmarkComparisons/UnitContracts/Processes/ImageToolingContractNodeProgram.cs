namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class ImageToolingContractNodeProgram
{
    internal const string Source = """
                                   import { readFileSync } from 'node:fs';
                                   import path from 'node:path';
                                   import { pathToFileURL } from 'node:url';
                                   const moduleRoot = process.env.KEYLOAD_IMAGE_MODULE_ROOT;
                                   const moduleUrl = name => pathToFileURL(path.join(moduleRoot, name)).href;
                                   const inputs = await import(moduleUrl('image-inputs.mjs'));
                                   const manifest = await import(moduleUrl('image-manifest.mjs'));
                                   const evidence = await import(moduleUrl('image-evidence.mjs'));
                                   const request = JSON.parse(readFileSync(0, 'utf8'));
                                   const operation = request.operation;
                                   const environment = request.environment;
                                   try {
                                     let value;
                                     if (operation === 'context') {
                                       const context = inputs.createRunContext(environment, request.platform);
                                       value = { sourceSha: context.sourceSha, runId: context.runId, runAttempt: context.runAttempt, repository: context.repository, ref: context.ref };
                                     } else if (operation === 'source') {
                                       const context = inputs.createRunContext(environment, request.platform);
                                       inputs.assertCleanSourceRevision(context, request.head, request.diffExitCode, request.status);
                                       value = true;
                                     } else if (operation === 'endpoint') {
                                       inputs.assertLocalUnixEndpoint(request.endpoint, request.dockerHost);
                                       value = true;
                                     } else if (operation === 'metadata') {
                                       value = manifest.parseImageMetadata(request.output, request.expectedRevision);
                                     } else if (operation === 'manifest') {
                                       const bytes = Buffer.from(request.bytesBase64, 'base64');
                                       const result = manifest.parseManifestEvidence(bytes, request.digestHeader, request.contentTypeHeader,
                                         request.expectedRevision, request.configId, request.imageName, request.taggedReference);
                                       value = { finalReference: result.finalReference, manifestDigest: result.manifest.manifestSha256,
                                         registryDigest: result.manifest.digestHeader, revisionLabel: result.sourceLabelValue,
                                         configId: result.configImageId, manifestBytesBase64: result.manifest.manifestBytesBase64 };
                                     } else if (operation === 'diagnostics') {
                                       value = evidence.sanitizeNativeOutput(request.output);
                                     } else {
                                       throw new Error('Unsupported image tooling contract operation.');
                                     }
                                     process.stdout.write(JSON.stringify({ ok: true, value }) + '\n');
                                   } catch (error) {
                                     const safeMessage = error instanceof Error ? error.message : 'Image tooling contract input was rejected.';
                                     process.stdout.write(JSON.stringify({ ok: false, message: safeMessage }) + '\n');
                                   }
                                   """;
}
