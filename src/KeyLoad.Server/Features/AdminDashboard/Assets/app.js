import {Api,Config,Dom,Id,Text,View} from './constants.js';
import {el,status,write} from './dom.js';
import {hasSelectedResource,initializeBrowsing,loadData,loadResources,resetBrowsing,viewChanged} from './browsing.js';
import {clearObservations,physicalFiles,renderSnapshot,selectView} from './rendering.js';
import {resetMetrics} from './metrics.js';
let credential=Text.empty;
let epoch=Config.zero;
let controller=null;
let inFlight=false;
let poll=null;
let view=View.overview;
function connection(active){document.body.classList.toggle(Dom.connected,active);write(Id.state,active?Text.connected:Text.disconnected);el(Id.disconnect).disabled=!active;el(Id.refresh).disabled=!active;}
function stopPolling(){if(poll!==null)clearTimeout(poll);poll=null;}
function schedule(){stopPolling();if(credential&&!document.hidden)poll=setTimeout(refresh,Config.pollMs);}
function cancelPending(){epoch+=Config.one;controller?.abort();controller=null;inFlight=false;stopPolling();}
function disconnect(message=Text.disconnectedHint){cancelPending();credential=Text.empty;el(Id.key).value=Text.empty;el(Id.tenant).value=Text.defaultTenant;el(Id.database).value=Text.defaultDatabase;el(Id.partition).value=Text.defaultPartition;connection(false);clearObservations();resetBrowsing();status(message,message===Text.unauthorized);}
async function request(path,body=null){if(!credential||inFlight)return null;inFlight=true;const started=epoch;const current=new AbortController();controller=current;stopPolling();try{const headers={[Api.authorization]:Api.bearer+credential};if(body!==null)headers[Api.contentType]=Api.json;const response=await fetch(path,{method:body===null?Api.get:Api.post,headers,body:body===null?undefined:JSON.stringify(body),credentials:Api.sameOrigin,cache:Api.noStore,signal:current.signal});if(started!==epoch)return null;if(response.status===Config.unauthenticated||response.status===Config.forbidden){disconnect(Text.unauthorized);return null;}if(!response.ok)throw new Error(Text.httpError+response.status+Text.closeParen);const result=await response.json();return started===epoch?result:null;}catch(error){if(started!==epoch||error.name===Api.abort)return null;resetMetrics();write(Id.state,Text.observationUnavailable);el(Id.refresh).disabled=false;status(Text.failed,true);return null;}finally{if(started===epoch){inFlight=false;controller=null;schedule();}}}
async function refresh(){if(!credential||inFlight||document.hidden)return;status(Text.loading);const result=await request(Api.snapshot);if(!result)return;renderSnapshot(result);connection(true);status(Text.ready);if(view===View.files&&!hasSelectedResource())physicalFiles();}
async function connect(event){event.preventDefault();const key=el(Id.key).value.trim();if(!key)return;disconnect();credential=key;el(Id.key).value=Text.empty;write(Id.state,Text.connecting);el(Id.disconnect).disabled=false;await refresh();}
async function navigate(next){cancelPending();view=next;selectView(view);viewChanged(view);schedule();if(credential&&view!==View.overview&&view!==View.cluster)await loadResources();}
function wireBrowsing(){el(Id.scope).addEventListener(Dom.submit,event=>{event.preventDefault();loadResources();});el(Id.resourcesFirst).addEventListener(Dom.click,()=>loadResources());el(Id.resourcesNext).addEventListener(Dom.click,()=>loadResources(true));el(Id.dataFirst).addEventListener(Dom.click,()=>loadData());el(Id.dataNext).addEventListener(Dom.click,()=>loadData(true));}
function start(){initializeBrowsing(request,cancelPending);el(Id.form).addEventListener(Dom.submit,connect);el(Id.disconnect).addEventListener(Dom.click,()=>disconnect());el(Id.refresh).addEventListener(Dom.click,refresh);document.querySelectorAll(Dom.nav).forEach(button=>button.addEventListener(Dom.click,()=>navigate(button.dataset.view)));el(Id.closeDetails).addEventListener(Dom.click,()=>el(Id.dialog).close());document.addEventListener(Dom.visibility,()=>{if(document.hidden)stopPolling();else refresh();});wireBrowsing();}
start();
