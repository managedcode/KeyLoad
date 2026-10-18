namespace KeyLoad.ComparisonTests.Features.ClusterRouting;

internal static class RequestCqrsImageJournalNodeProgram
{
    internal const string Source = """
                                   import { mkdtemp, readFile, rm } from 'node:fs/promises';
                                   import os from 'node:os';
                                   import path from 'node:path';
                                   import { pathToFileURL } from 'node:url';
                                   const processApi = await import(pathToFileURL(process.argv[1]).href);
                                   const evidenceApi = await import(pathToFileURL(process.argv[2]).href);
                                   const contracts = await import(pathToFileURL(process.argv[3]).href);
                                   const mode = process.argv[4];
                                   const root = await mkdtemp(path.join(os.tmpdir(), 'keyload-c1-image-journal-'));
                                   const context = { runnerTemp: root, evidenceDirectory: path.join(root, contracts.directoryName.evidence) };
                                   let primary;
                                   try {
                                     await evidenceApi.ensureEvidenceDirectory(context);
                                     let summary;
                                     if (mode === 'sequence41') summary = await recordSequence(context, processApi, evidenceApi, 41);
                                     else if (mode === 'boundary64') summary = await verifyBoundary(context, processApi, evidenceApi, contracts);
                                     else if (mode === 'output-overflow') summary = await verifyOverflow(context, processApi, evidenceApi, contracts);
                                     else throw new Error('Unsupported private image-journal test mode.');
                                     process.stdout.write(JSON.stringify(summary) + '\n');
                                   } catch (error) {
                                     primary = error;
                                     throw error;
                                   } finally {
                                     try { await rm(root, { recursive: true, force: true }); }
                                     catch (cleanup) {
                                       if (primary) throw new AggregateError([primary, cleanup], 'Image-journal test cleanup failed.');
                                       throw cleanup;
                                     }
                                   }

                                   async function recordSequence(context, processApi, evidenceApi, count) {
                                     let actualSuccesses = 0;
                                     for (let index = 1; index <= count; index++) {
                                       const operation = `sequence-step-${String(index).padStart(2, '0')}`;
                                       const result = await actualCommand(processApi, context.runnerTemp);
                                       if (!result.success) throw new Error('A native sequence command did not succeed.');
                                       await evidenceApi.recordNativeCommand(context, operation, result);
                                       actualSuccesses++;
                                     }
                                     const records = await readJournal(context, contracts);
                                     const ordered = records.length === count && records.every((record, index) =>
                                       record.operation === `sequence-step-${String(index + 1).padStart(2, '0')}`
                                         && record.exitCode === 0 && record.signal === null && !record.timedOut
                                         && !record.outputLimitExceeded && !record.spawnFailed);
                                     return { count: records.length, ordered, actualSuccesses };
                                   }

                                   async function verifyBoundary(context, processApi, evidenceApi, contracts) {
                                     if (contracts.processLimit.maxNativeCommandRecords !== 64) throw new Error('The native command journal cap changed.');
                                     const prior = await recordSequence(context, processApi, evidenceApi, 64);
                                     if (!prior.ordered || prior.actualSuccesses !== 64) throw new Error('The exact-cap native sequence was incomplete.');
                                     const journalPath = path.join(context.evidenceDirectory, contracts.fileName.nativeCommands);
                                     const before = await readFile(journalPath);
                                     const command65 = await actualCommand(processApi, context.runnerTemp);
                                     if (!command65.success) throw new Error('The native sixty-fifth command did not complete successfully.');
                                     let rejected65 = false;
                                     try { await evidenceApi.recordNativeCommand(context, 'sequence-step-65', command65); }
                                     catch (error) { rejected65 = error instanceof Error && error.message === contracts.message.commandOutputLimit; }
                                     const after = await readFile(journalPath);
                                     const records = await readJournal(context, contracts);
                                     return { count: records.length, rejected65, journalUnchanged: before.equals(after), command65ExitCode: command65.code };
                                   }

                                   async function verifyOverflow(context, processApi, evidenceApi, contracts) {
                                     if (contracts.processLimit.maxOutputBytes !== 262144) throw new Error('The native output bound changed.');
                                     const program = `process.stdout.write(Buffer.alloc(${contracts.processLimit.maxOutputBytes + 1}, 0x61));`;
                                     const result = await processApi.runBounded(process.execPath, ['-e', program], { cwd: context.runnerTemp });
                                     const childSettled = !result.spawnFailed && !result.timedOut && (result.code !== null || result.signal !== null);
                                     await evidenceApi.recordNativeCommand(context, 'native-output-overflow', result);
                                     const records = await readJournal(context, contracts);
                                     const record = records[0];
                                     const failureRecordMatchesChild = record?.outputLimitExceeded === true
                                       && record.exitCode === result.code && record.signal === result.signal
                                       && record.spawnFailed === result.spawnFailed && record.timedOut === result.timedOut;
                                     return { overflowRecorded: result.outputLimitExceeded, childSettled,
                                       failureRecordMatchesChild, capturedBytes: Buffer.byteLength(result.stdout, 'utf8') };
                                   }

                                   async function actualCommand(processApi, cwd) {
                                     return processApi.runBounded(process.execPath, ['-e', "process.stdout.write('native-command-ok\\n')"], { cwd });
                                   }

                                   async function readJournal(context, contracts) {
                                     const bytes = await readFile(path.join(context.evidenceDirectory, contracts.fileName.nativeCommands));
                                     const text = bytes.toString('utf8');
                                     if (!text.endsWith('\n')) throw new Error('The native command journal is incomplete.');
                                     return text.trimEnd().split('\n').map(line => JSON.parse(line));
                                   }
                                   """;
}
