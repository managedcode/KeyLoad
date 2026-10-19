// Controlled contract fixtures only; these numbers never enter published benchmark artifacts.
import { pathToFileURL } from 'node:url';
const base = pathToFileURL(process.cwd() + '/scripts/Features/BenchmarkComparisons/');
const contract = await import(new URL('session-contract.mjs', base));
const planner = await import(new URL('session-plan.mjs', base));
const reports = await import(new URL('session-report-validation.mjs', base));
const selection = await import(new URL('session-github-selection.mjs', base));
const scenario = process.argv[2];
let accepted = false, rejections = 0, expectedRejections = 0;
const reject = callback => {
  expectedRejections++;
  try { callback(); } catch { rejections++; }
};
const clone = value => structuredClone(value);
const binary = { executablePath: '/usr/share/dotnet/dotnet', executableSha256: '1'.repeat(64),
  assemblyPath: '/workspace/runtime.dll', assemblySha256: '2'.repeat(64), pdbPath: '/workspace/runtime.pdb',
  pdbSha256: '3'.repeat(64), sourceCount: 12, sourceCompositionSha256: '4'.repeat(64) };
const processes = (target, count) => ({ client: { name: 'comparison-client', processId: 500,
  startedAtUtc: '2026-10-09T10:02:00Z', aspireStartedAtUtc: null,
  aspireProcessId: null, aspireProcessStartedAtUtc: null, binary },
  servers: target === 'KeyLoad' ? Array.from({length: count}, (_, index) => ({name: 'node' + (index + 1),
    processId: 100 + index, startedAtUtc: '2026-10-09T10:01:00Z', aspireStartedAtUtc: '2026-10-09T10:00:59Z',
    aspireProcessId: 200 + index, aspireProcessStartedAtUtc: '2026-10-09T10:00:59Z', binary })) : [] });
const pendingResources = { id: 'matched-execution-process-resources', status: 'notExecuted', failureType: 'MatchedProcessResourceQualificationOpen' };
const cohort = { sourceRevision: 'a'.repeat(40), runId: 12345, attempt: 2,
  repository: contract.SESSION.repository, ref: contract.SESSION.ref,
  workflow: contract.SESSION.workflow, profile: contract.SESSION.profile };
if (scenario === 'plan') {
  const plan = planner.createSessionPlan();
  planner.validateSessionPlan(plan);
  const matrices = planner.sessionMatrices();
  accepted = plan.workers.length === 22 && Object.keys(matrices).length === 11
    && Object.values(matrices).every(matrix => matrix.include.length === 2
      && matrix.include[0].nodeCount === 1 && matrix.include[1].nodeCount === 3);
  for (const change of [value => value.workers.pop(), value => value.workers.push(value.workers[0]),
    value => value.workers[0].nodeCount = 2, value => value.caseIds.pop(),
    value => value.warmup = 1, value => value.initialRecords = 100000]) {
    const altered = clone(plan); change(altered); reject(() => planner.validateSessionPlan(altered));
  }
} else if (scenario === 'reports') {
  const count = contract.SESSION.operations;
  const samples = Array.from({length: count}, (_, operation) => ({operation, worker: operation % 16,
    startedMs: 0, completedMs: 1, success: true, error: null}));
  const measurement = {attempts:count,successes:count,failures:0,elapsedSeconds:1,
    usefulOperationsPerSecond:count,latency:{p50Ms:1,p95Ms:1,p99Ms:1},uniqueCompletedMessages:0,
    enqueue:null,receive:null,ack:null,clientResources:null};
  const report = {caseId:'sequential-read',status:'measured',detail:null,initialRecords:1000000,
    expectedRecords:1000000,verifiedRecords:0,lanes:[{id:'read',measurement,firstStartedMs:0,
      lastCompletedMs:1000,samples}],elapsedSeconds:1};
  reports.validateSessionCase(report, report.caseId); accepted = true;
  for (const change of [value => value.lanes[0].samples[1].operation = 0,
    value => value.lanes[0].measurement.latency.p95Ms = 2,
    value => value.lanes[0].measurement.successes--,
    value => value.lanes[0].measurement.usefulOperationsPerSecond++,
    value => value.lanes[0].samples[0].completedMs = -1,
    value => value.lanes[0].samples.pop(), value => value.caseId = 'unknown']) {
    const altered = clone(report); change(altered); reject(() => reports.validateSessionCase(altered, report.caseId));
  }
  const execution = '<TestRun>' + contract.SESSION.caseIds.map(id => ({'load-million':'LoadMillion',index:'BuildIndexes',
    'sequential-read':'SequentialRead','random-read':'RandomRead',search:'Search',mixed:'Mixed','verify-final':'VerifyFinalState'})[id])
    .map(name => `<UnitTestResult testName="${name}" outcome="Passed"/>`).join('') + '</TestRun>';
  const cases = contract.SESSION.caseIds.map(caseId => ({caseId,status:'measured'}));
  reports.validateSessionResults(execution,cases);
  reject(() => reports.validateSessionResults(execution.replace('LoadMillion','Other'),cases));
  reject(() => reports.validateSessionResults(execution.replace('outcome="Passed"','outcome="Failed"'),cases));
} else if (scenario === 'profile-accounting') {
  const hash='1'.repeat(64),server='sha256:'+'2'.repeat(64);
  const methods={'load-million':'LoadMillion',index:'BuildIndexes','sequential-read':'SequentialRead',
    'random-read':'RandomRead',search:'Search',mixed:'Mixed','verify-final':'VerifyFinalState'};
  const originalReports=contract.SESSION.caseIds.map(caseId=>({caseId,status:'unavailable',
    detail:"Outside this target's shared contract.",initialRecords:caseId==='load-million'?0:contract.SESSION.records,
    expectedRecords:contract.SESSION.records,verifiedRecords:0,lanes:[],elapsedSeconds:0}));
  const runtimeXml=outcome=>'<TestRun>'+contract.SESSION.caseIds.map(id=>
    `<UnitTestResult testName="${methods[id]}" outcome="${outcome}"/>`).join('')+'</TestRun>';
  const selected=contract.sessionRows().filter(row=>['Qdrant','RabbitMQ','KurrentDB'].includes(row.target));
  let capabilitySession;
  for(const row of selected) {
    const session={schemaVersion:contract.SESSION.schema,sessionId:row.id,target:row.target,nodeCount:row.nodeCount,
      ...cohort,jobId:10,images:{server},processes:processes(row.target,row.nodeCount),targetProfile:null,
      runtime:{operatingSystem:'Linux',architecture:'X64',logicalProcessors:4,framework:'.NET 10',storage:'execution bound mount'},
      assemblies:[{path:'ComparisonHost.dll',sha256:hash,pdbSha256:hash},{path:'Comparisons.dll',sha256:hash,pdbSha256:hash}],
      cases:contract.SESSION.caseIds.map(id=>({id,path:`reports/${id}.json`,sha256:hash})),
      runtimeReport:{path:'RuntimeTestResults/original.trx',sha256:hash},outerRuntime:{path:'runtime-import.json',sha256:hash},
      qualificationReport:{path:'session-qualification.json',sha256:hash},
      resourceReport:{path:'session-resource-evidence.json',sha256:hash},
      outcome:'unavailable',reason:contract.SESSION.capabilitiesUnavailable};
    delete session.profile;
    capabilitySession=clone(session);
    const qualification={schemaVersion:1,sessionId:row.id,target:row.target,nodeCount:row.nodeCount,runtimeExitCode:0,
      status:'passed',reason:null,steps:[{id:'server-resource-cancellation',status:row.nodeCount===1?'passed':'notApplicable',failureType:null},
        {id:'execution-correctness',status:row.target==='KurrentDB'?'passed':'notApplicable',failureType:null}]};
    const execution=reports.validateSessionResults(runtimeXml('Passed'),originalReports);
    originalReports.forEach(report=>reports.validateSessionCase(report,report.caseId));
    reports.validateSessionEnvelope(session,row,cohort,10);
    reports.validateSessionQualification(qualification,session,execution);
    reports.validateSessionProfileAccounting(session,originalReports,execution,qualification);accepted=true;
    reject(()=>reports.validateSessionProfileAccounting(session,originalReports.slice(1),execution,qualification));
    reject(()=>reports.validateSessionProfileAccounting(session,originalReports,
      execution.map((test,index)=>index===0?{...test,outcome:'Failed'}:test),qualification));
    for(const change of [v=>v.images.server=null,v=>v.resourceReport=null,
      v=>v.reason=contract.SESSION.topologyUnavailable,v=>v.targetProfile={name:row.target,version:'unverified',
        topology:'unverified execution topology',writeAcknowledgement:'execution',readContract:'execution',transport:'execution',
        authorization:'execution',image:null,cluster:null}]) {
      const altered=clone(session);change(altered);reject(()=>reports.validateSessionEnvelope(altered,row,cohort,10));
    }
    const measured=clone(originalReports);measured[1]={...measured[1],status:'measured',detail:null,elapsedSeconds:1};
    reports.validateSessionCase(measured[1],'index');
    reject(()=>reports.validateSessionProfileAccounting(session,measured,execution,qualification));
    const altered=clone(originalReports);altered[0].detail='AdapterSessionQueryLifecycleUnavailable';
    reject(()=>reports.validateSessionProfileAccounting(session,altered,execution,qualification));
  }
  for(const [target,detail] of [
    ['Neo4j',"Neo4j Community does not provide execution clustering; Enterprise licensing is excluded."],
    ['SurrealDB',"SurrealDB Community's experimental TiKV clustering is outside the qualified execution topology; only the persistent standalone server is measured."],
    ['HelixDB',"The self-hosted HelixDB server is standalone; managed cloud high availability is outside this execution comparison."]]) {
    const row=contract.sessionRows().find(row=>row.target===target&&row.nodeCount===3);
    const session={...capabilitySession,sessionId:row.id,target,nodeCount:3,images:{server:null},
      resourceReport:null,reason:contract.SESSION.topologyUnavailable};
    const cases=originalReports.map(report=>({...report,detail}));
    const execution=reports.validateSessionResults(runtimeXml('Passed'),cases);
    const qualification={schemaVersion:1,sessionId:row.id,target,nodeCount:3,runtimeExitCode:0,
      status:'notApplicable',reason:contract.SESSION.topologyUnavailable,steps:[]};
    cases.forEach(report=>reports.validateSessionCase(report,report.caseId));
    reports.validateSessionEnvelope(session,row,cohort,10);
    reports.validateSessionQualification(qualification,session,execution);
    reports.validateSessionProfileAccounting(session,cases,execution,qualification);
    reject(()=>reports.validateSessionEnvelope({...session,images:{server}},row,cohort,10));
    const cleanupFailure={...session,outcome:'failure',reason:'SessionTestFailure'};
    const failedExit={...qualification,runtimeExitCode:1,status:'notExecuted',reason:'ClientFailed'};
    reports.validateSessionEnvelope(cleanupFailure,row,cohort,10);
    reports.validateSessionQualification(failedExit,cleanupFailure,execution);
    reports.validateSessionProfileAccounting(cleanupFailure,cases,execution,failedExit);
    const wrongRow=contract.sessionRows().find(item=>item.target==='KeyLoad'&&item.nodeCount===3);
    reject(()=>reports.validateSessionEnvelope({...cleanupFailure,sessionId:wrongRow.id,target:wrongRow.target},wrongRow,cohort,10));
    reject(()=>reports.validateSessionProfileAccounting({...cleanupFailure,nodeCount:1},cases,execution,failedExit));
    reject(()=>reports.validateSessionProfileAccounting(cleanupFailure,cases,
      execution.map((test,index)=>index===0?{...test,outcome:'Failed'}:test),failedExit));
    reject(()=>reports.validateSessionProfileAccounting(cleanupFailure,cases,execution,{...failedExit,runtimeExitCode:0}));

  }
  const qualifierFailure={...capabilitySession,outcome:'failure',reason:'SessionQualificationFailed'};
  const failedQualifier={schemaVersion:1,sessionId:qualifierFailure.sessionId,target:qualifierFailure.target,
    nodeCount:qualifierFailure.nodeCount,runtimeExitCode:0,status:'failed',reason:'SessionQualificationFailed',
    steps:[{id:'server-resource-cancellation',status:'notApplicable',failureType:null},
      {id:'execution-correctness',status:'failed',failureType:'ComparisonFailureException'}]};
  const passed=reports.validateSessionResults(runtimeXml('Passed'),originalReports);
  reports.validateSessionEnvelope(qualifierFailure,selected.at(-1),cohort,10);
  reports.validateSessionQualification(failedQualifier,qualifierFailure,passed);
  reports.validateSessionProfileAccounting(qualifierFailure,originalReports,passed,failedQualifier);
  const row=contract.sessionRows().find(row=>row.target==='KeyLoad'&&row.nodeCount===1);
  const failure={...capabilitySession,sessionId:row.id,target:row.target,nodeCount:1,targetProfile:null,images:{server:null},processes:processes('KeyLoad',1),
    resourceReport:null,outcome:'failure',reason:'SessionTestFailure'};
  const failed=originalReports.map(report=>({...report,status:'failed',detail:'InitializationFailed'}));
  failed[0].elapsedSeconds=1;
  failed[0].lanes=[{id:'create',firstStartedMs:0,lastCompletedMs:1,samples:[
    {operation:0,worker:0,startedMs:0,completedMs:1,success:true,error:null},
    {operation:1,worker:1,startedMs:0,completedMs:1,success:false,error:'OperationFailed'}],
    measurement:{attempts:2,successes:1,failures:1,elapsedSeconds:1,usefulOperationsPerSecond:1,
      latency:{p50Ms:1,p95Ms:1,p99Ms:1},uniqueCompletedMessages:0,enqueue:null,receive:null,ack:null,clientResources:null}}];
  failed.forEach(report=>reports.validateSessionCase(report,report.caseId));
  reports.validateSessionEnvelope(failure,row,cohort,10);
  const execution=reports.validateSessionResults(runtimeXml('Failed'),failed);
  const qualification={schemaVersion:1,sessionId:row.id,target:row.target,nodeCount:1,runtimeExitCode:1,
    status:'notExecuted',reason:'ClientFailed',steps:[pendingResources]};
  reports.validateSessionQualification(qualification,failure,execution);
  reports.validateSessionProfileAccounting(failure,failed,execution,qualification);
  reject(()=>reports.validateSessionProfileAccounting(failure,failed,execution,{...qualification,runtimeExitCode:0,status:'passed'}));
} else if (scenario === 'publication') {
  const {validateSessionProducer,requirePublishableSessionMeasurements} = await import(new URL('site-session-selection.mjs',base));
  const workflow={id:10,path:'.github/workflows/benchmarks.yml',name:contract.SESSION.workflow,state:'active'};
  const repository={id:477801965,full_name:cohort.repository};
  const run={workflow_id:10,path:workflow.path,name:workflow.name,head_branch:'main',event:'push',
    repository,head_repository:repository,status:'completed',conclusion:'success',head_sha:cohort.sourceRevision,
    id:cohort.runId,run_attempt:cohort.attempt};
  validateSessionProducer(run,workflow,'publish');
  validateSessionProducer({...run,conclusion:'failure'},workflow,'validate');
  reject(()=>validateSessionProducer({...run,conclusion:'failure'},workflow,'publish'));
  reject(()=>validateSessionProducer({...run,conclusion:'cancelled'},workflow,'validate'));
  const value={workers:contract.sessionRows().map(row=>{
    const unavailable=['Qdrant','RabbitMQ','KurrentDB'].includes(row.target);
    return {job:{conclusion:'success'},session:{outcome:unavailable?'unavailable':'success',
      reason:unavailable?contract.SESSION.capabilitiesUnavailable:null,
      targetProfile:unavailable?null:{version:'1.0',topology:'execution cluster',cluster:{state:'healthy'}}},
      qualification:{status:'passed'},resourceQualified:!unavailable,
      execution:contract.SESSION.caseIds.map(()=>({outcome:'Passed'})),
      reports:contract.SESSION.caseIds.map(caseId=>({caseId,status:unavailable?'unavailable':'measured'}))};
  })};
  requirePublishableSessionMeasurements(value);accepted=true;
  for(const change of [v=>v.workers.pop(),v=>v.workers[0].job.conclusion='failure',
    v=>v.workers[0].qualification.status='failed',v=>v.workers[0].execution[0].outcome='Failed',
    v=>v.workers[0].reports[0].status='failed',v=>v.workers[0].reports[0].status='notQualified',
    v=>v.workers[0].resourceQualified=false]) {
    const altered=clone(value);change(altered);reject(()=>requirePublishableSessionMeasurements(altered));
  }
  const measured=clone(value);measured.workers[0].resourceQualified=true;
  measured.workers[0].reports[0].status='measured';
  measured.workers[0].session.targetProfile={version:'1.0',topology:'execution cluster',cluster:{state:'healthy'}};
  requirePublishableSessionMeasurements(measured);
  measured.workers[0].session.targetProfile.version='unverified';
  reject(()=>requirePublishableSessionMeasurements(measured));
} else if (scenario === 'site-context') {
  const { sessionSiteContext } = await import(new URL('site-session-context.mjs',base));
  const revision='a'.repeat(40),repository=contract.SESSION.repository;
  const env={GITHUB_ACTIONS:'true',RUNNER_OS:'Linux',GITHUB_REPOSITORY:repository,GH_REPO:repository,
    GITHUB_REPOSITORY_ID:'477801965',GITHUB_WORKFLOW:'Website',GITHUB_REF:contract.SESSION.ref,GITHUB_JOB:'qualify',
    GITHUB_EVENT_NAME:'push',GITHUB_WORKFLOW_REF:repository+'/.github/workflows/website.yml@'+contract.SESSION.ref,
    GITHUB_WORKFLOW_SHA:revision,GITHUB_SHA:revision,GITHUB_RUN_ID:'1',GITHUB_RUN_ATTEMPT:'1',GH_TOKEN:'controlled-test-token',
    GITHUB_WORKSPACE:process.cwd()};
  const args={input:process.cwd(),mode:'publish',optional:'true','site-revision':revision,'workflow-revision':revision};
  accepted=sessionSiteContext(env,args,'linux').optional;
  sessionSiteContext(env,{...args,mode:'validate',optional:undefined,'requested-run':'123'},'linux');
  for(const change of [v=>v.mode='validate',v=>v['requested-run']='123',v=>v.optional='false',
    v=>v['site-revision']='bad',v=>v.input='relative']) {
    const altered={...args};change(altered);reject(()=>sessionSiteContext(env,altered,'linux'));
  }
  for(const [key,value] of [['GITHUB_REPOSITORY_ID','1'],['GITHUB_WORKFLOW','Other'],['RUNNER_OS','Windows'],
    ['GITHUB_WORKFLOW_SHA','b'.repeat(40)],['GITHUB_JOB','foreign']]) reject(()=>sessionSiteContext({...env,[key]:value},args,'linux'));
  reject(()=>sessionSiteContext(env,args,'darwin'));
} else if (scenario === 'runtime') {
  const { validateSessionRuntimeImport } = await import(new URL('session-runtime-identity.mjs',base));
  const { runtimeBundle } = await import(new URL('compiled-test-bundle-contract.mjs',base));
  const value={schemaVersion:1,kind:'keyload-benchmark-runtime-import',sourceRevision:cohort.sourceRevision,
    runId:cohort.runId,attempt:cohort.attempt,repository:cohort.repository,ref:cohort.ref,workspace:process.cwd(),
    platform:'linux',architecture:'x64',sdkVersion:'10.0.401',descriptorSha256:'d'.repeat(64),
    inventorySha256:'e'.repeat(64),archiveSha256:'f'.repeat(64),fileCount:2,
    assemblies:runtimeBundle.assemblies.map(([role,file])=>({role,path:file+'.dll',sha256:'1'.repeat(64),pdbPath:file+'.pdb',pdbSha256:'2'.repeat(64)}))};
  const expected={descriptor:clone(value),descriptorSha256:value.descriptorSha256,inventorySha256:value.inventorySha256};
  validateSessionRuntimeImport(value,cohort,expected);accepted=true;
  for(const change of [v=>v.runId++,v=>v.attempt++,v=>v.sourceRevision='b'.repeat(40),
    v=>v.descriptorSha256='c'.repeat(64),v=>v.inventorySha256='c'.repeat(64),v=>v.assemblies[0].pdbSha256='3'.repeat(64),
    v=>v.workspace+='-foreign',v=>v.platform='darwin',v=>v.fileCount++]) {
    const altered=clone(value);change(altered);reject(()=>validateSessionRuntimeImport(altered,cohort,expected));
  }
} else if (scenario === 'qualification') {
  const session = {sessionId:'redis-n1',target:'Redis',nodeCount:1,targetProfile:{},outcome:'success',reason:null};
  const execution = contract.SESSION.caseIds.map(() => ({outcome:'Passed'}));
  const value = {schemaVersion:1,sessionId:session.sessionId,target:session.target,nodeCount:1,runtimeExitCode:0,
    status:'passed',reason:null,steps:['server-resource-cancellation','execution-correctness'].map(id => ({id,status:'passed',failureType:null}))};
  reports.validateSessionQualification(value,session,execution);accepted=true;
  for (const change of [v=>v.steps.pop(),v=>v.steps[0].status='notApplicable',v=>v.runtimeExitCode=1,
    v=>v.steps[0].failureType='fake',v=>v.sessionId='redis-n3',v=>v.steps.reverse()]) {
    const altered=clone(value);change(altered);reject(()=>reports.validateSessionQualification(altered,session,execution));
  }
  const cleanup={...value,runtimeExitCode:1,status:'notExecuted',reason:'ClientFailed',steps:[]};
  reports.validateSessionQualification(cleanup,{...session,outcome:'failure',reason:'SessionTestFailure'},execution);
  const failed=clone(value);failed.status='failed';failed.reason='SessionQualificationFailed';
  failed.steps[0]={...failed.steps[0],status:'failed',failureType:'ActualControlledException'};
  reports.validateSessionQualification(failed,{...session,outcome:'failure',reason:failed.reason},execution);
} else if (scenario === 'environment') {
  const { selectSession } = await import(new URL('session-github-context.mjs', base));
  const row = contract.sessionRows()[0];
  const env = {KEYLOAD_BENCHMARK_SESSION_ID:row.id,KEYLOAD_COMPARISON_JOB_NAME:row.jobName,
    Benchmarks__Target:row.target,Benchmarks__NodeCount:String(row.nodeCount),Benchmarks__Session:'true'};
  accepted = selectSession(env).id === row.id;
  for (const [key,value] of [['KEYLOAD_BENCHMARK_SESSION_ID','keyload-n2'],['KEYLOAD_COMPARISON_JOB_NAME','foreign'],
    ['Benchmarks__Target','Qdrant'],['Benchmarks__NodeCount','3'],['Benchmarks__Session','false']]) {
    const altered={...env,[key]:value};reject(() => selectSession(altered));
  }
} else if (scenario === 'selection') {
  const rows = contract.sessionRows();
  const started = '2026-10-16T20:00:00Z', completed = '2026-10-16T20:10:00Z';
  const repository = {id:477801965,full_name:contract.SESSION.repository};
  const run = {id:cohort.runId,run_attempt:cohort.attempt,head_sha:cohort.sourceRevision,head_branch:'main',
    repository,head_repository:repository,run_started_at:started};
  const job = (id,name,steps,conclusion='success') => ({id,name,run_id:cohort.runId,run_attempt:cohort.attempt,
    head_sha:cohort.sourceRevision,html_url:`https://github.com/${cohort.repository}/actions/runs/${cohort.runId}/job/${id}`,
    started_at:started,completed_at:completed,status:'completed',conclusion,
    steps:steps.map((name,index) => ({name,number:index+1,status:'completed',conclusion:name === 'Run database workload' ? conclusion : 'success'}))});
  const artifact = (id,name) => ({id,name,size_in_bytes:100,digest:'sha256:'+'b'.repeat(64),expired:false,
    created_at:'2026-10-16T20:09:00Z',workflow_run:{id:run.id,head_sha:run.head_sha,head_branch:'main',
      repository_id:repository.id,head_repository_id:repository.id}});
  const capture = {run,jobs:rows.map((row,index) => job(index+1,row.jobName,contract.SESSION.workerSteps,index === 0 ? 'failure':'success')),
    artifacts:rows.map((row,index) => artifact(index+100,row.artifactName))};
  capture.jobs.push(job(99,contract.SESSION.prepareJob,contract.SESSION.prepareSteps));
  capture.artifacts.push(artifact(201,'comparison-compiled-test-bundle'),artifact(202,'comparison-compiled-runtime-identity'));
  accepted = selection.selectSessionEvidence(capture,cohort).workers.length === 22;
  for (const change of [value => value.jobs.shift(),value => value.jobs[0].head_sha='c'.repeat(40),
    value => value.jobs[0].run_attempt=1,value => value.artifacts[0].expired=true,
    value => value.artifacts.push(value.artifacts[0]),value => value.artifacts[0].workflow_run.id++,
    value => value.jobs[0].steps[1].conclusion='failure']) {
    const altered=clone(capture);change(altered);reject(() => selection.selectSessionEvidence(altered,cohort));
  }
} else throw new Error('Unknown session contract scenario.');
process.stdout.write(JSON.stringify({accepted,rejections,expectedRejections}));
