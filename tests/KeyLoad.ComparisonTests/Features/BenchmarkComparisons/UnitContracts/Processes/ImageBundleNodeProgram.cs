namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class ImageBundleNodeProgram
{
    internal const string Source = """
        import { mkdir, writeFile, readFile, symlink, truncate, unlink } from 'node:fs/promises';
        import { createHash } from 'node:crypto';
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const [modulePath, root, corruption] = process.argv.slice(1);
        const api = await import(pathToFileURL(modulePath));
        const constants = await import(pathToFileURL(path.join(path.dirname(modulePath), 'image-contracts.mjs')));
        const context = { sourceSha: 'a'.repeat(40), runId: '123', runAttempt: '1', repository: 'managedcode/KeyLoad', ref: 'refs/heads/main' };
        const digest = bytes => createHash('sha256').update(bytes).digest('hex');
        const bundle = { schemaVersion: 1, sourceRevision: context.sourceSha, runId: context.runId, attempt: context.runAttempt,
          repository: context.repository, ref: context.ref, images: {} };
        const receipt = { schemaVersion: 1, sourceRevision: context.sourceSha,
          github: { runId: context.runId, attempt: context.runAttempt, repository: context.repository, ref: context.ref },
          bases: { sdk: constants.baseImage.sdk, runtime: constants.baseImage.aspnet, registry: constants.registry.image }, images: {} };
        for (const kind of ['server', 'comparisons']) {
          const config = 'sha256:' + (kind === 'server' ? 'b' : 'c').repeat(64);
          const manifest = Buffer.from(JSON.stringify({ schemaVersion: 2, mediaType: constants.registryProtocol.dockerManifestMediaType,
            config: { digest: config }, layers: [] }));
          const hash = 'sha256:' + digest(manifest);
          receipt.images[kind] = { reference: `127.0.0.1:5000/keyload/${kind}:${context.sourceSha}-123-1@${hash}`,
            manifestDigest: hash, registryDigest: hash, manifestFile: kind + '-manifest.json', revisionLabel: context.sourceSha, configId: config };
          const archive = Buffer.from('controlled file validation input: ' + kind);
          bundle.images[kind] = { archive: kind + '.tar', bytes: archive.length, sha256: digest(archive) };
          await writeFile(path.join(root, kind + '.tar'), archive);
          await writeFile(path.join(root, kind + '-manifest.json'), manifest);
        }
        let selected = root;
        const server = path.join(root, 'server.tar');
        if (corruption === 'archive-hash') bundle.images.server.sha256 = 'd'.repeat(64);
        if (corruption === 'archive-length') bundle.images.server.bytes++;
        if (corruption === 'archive-name') bundle.images.server.archive = '../server.tar';
        if (corruption === 'archive-empty') await writeFile(server, '');
        if (corruption === 'archive-large') await truncate(server, 4294967297);
        if (corruption === 'archive-symlink') { await unlink(server); await symlink(path.join(root, 'comparisons.tar'), server); }
        if (corruption === 'archive-directory') { await unlink(server); await mkdir(server); }
        if (corruption === 'ancestor-symlink') { selected = root + '-link'; await symlink(root, selected); }
        if (corruption === 'missing') await unlink(server);
        if (corruption === 'extra-field') bundle.ignored = true;
        if (corruption === 'extra-image') bundle.images.foreign = bundle.images.server;
        if (corruption === 'source') bundle.sourceRevision = 'e'.repeat(40);
        if (corruption === 'run') bundle.runId = '124';
        if (corruption === 'attempt') bundle.attempt = '2';
        if (corruption === 'repository') bundle.repository = 'foreign/repository';
        if (corruption === 'ref') bundle.ref = 'refs/heads/foreign';
        if (corruption === 'base') receipt.bases.sdk = constants.baseImage.aspnet;
        if (corruption === 'config') receipt.images.server.configId = 'sha256:' + 'd'.repeat(64);
        if (corruption === 'manifest-bytes') await writeFile(path.join(root, 'server-manifest.json'), '{}');
        if (corruption === 'manifest-digest') receipt.images.server.registryDigest = 'sha256:' + 'd'.repeat(64);
        if (corruption === 'reference') receipt.images.server.reference = receipt.images.comparisons.reference;
        if (corruption === 'revision-label') receipt.images.server.revisionLabel = 'e'.repeat(40);
        let receiptText = JSON.stringify(receipt);
        let bundleText = JSON.stringify(bundle);
        if (corruption === 'duplicate-receipt') receiptText = receiptText.replace('"schemaVersion":1', '"schemaVersion":1,"schemaVersion":1');
        if (corruption === 'duplicate-bundle') bundleText = bundleText.replace('"schemaVersion":1', '"schemaVersion":1,"schemaVersion":1');
        if (corruption === 'duplicate-manifest') {
          const file = path.join(root, 'server-manifest.json');
          const bytes = Buffer.from((await readFile(file, 'utf8')).replace('"schemaVersion":2', '"schemaVersion":2,"schemaVersion":2'));
          await writeFile(file, bytes);
          const hash = 'sha256:' + digest(bytes);
          receipt.images.server.manifestDigest = hash; receipt.images.server.registryDigest = hash;
          receipt.images.server.reference = receipt.images.server.reference.slice(0, -71) + hash;
          receiptText = JSON.stringify(receipt);
        }
        await writeFile(path.join(root, 'image-receipt.json'), receiptText);
        await writeFile(path.join(root, 'image-bundle.json'), bundleText);
        try { await api.readImageBundle(selected, context); process.stdout.write('accepted\n'); }
        catch { process.stdout.write('rejected\n'); process.exitCode = 1; }
        finally { if (selected !== root) await unlink(selected); }
        """;

    internal const string Exclusive = """
        import { writeFile, readFile } from 'node:fs/promises';
        import path from 'node:path';
        import { pathToFileURL } from 'node:url';
        const [modulePath, root] = process.argv.slice(1);
        const api = await import(pathToFileURL(modulePath));
        const file = path.join(root, 'existing.json');
        await writeFile(file, 'original');
        let denied = false;
        try { await api.writeExclusive(file, Buffer.from('replacement')); } catch { denied = true; }
        if (!denied || await readFile(file, 'utf8') !== 'original') throw new Error('output changed');
        process.stdout.write('preserved\n');
        """;
}
