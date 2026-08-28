const esc=value=>String(value??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const number=value=>Number(value??0).toLocaleString();
async function hydrate(){
  const [moduleResponse,trainingResponse,datasetResponse,baselineResponse]=await Promise.all([
    fetch('/api/product/modules'),fetch('/api/product/modules/agent-training-readiness'),
    fetch('/api/agent/research-datasets'),fetch('/api/agent/baseline-runs')]);
  if(!moduleResponse.ok)throw new Error(`Module API ${moduleResponse.status}`);
  const modules=await moduleResponse.json();
  document.getElementById('moduleTotal').textContent=`${modules.length} modules`;
  document.getElementById('moduleCatalog').innerHTML=modules.map(({module,preview})=>
    `<article class="module-card ${module.requiresPaidEntitlement?'paid':'core'}"><header><span><strong>${esc(module.displayName)}</strong><small>${esc(module.kind)} · ${esc(module.version)}</small></span><em>${esc(preview.state)}</em></header><p>${esc(module.description)}</p><footer><span>${module.requiresPaidEntitlement?esc(module.subscriptionSku):'INCLUDED'}</span><span>${esc(module.integration)}</span></footer></article>`).join('');
  const agent=modules.find(x=>x.module.moduleId==='agent-research-lab');
  if(agent)document.getElementById('agentAccess').textContent=agent.preview.state;
  if(trainingResponse.ok){
    const training=await trainingResponse.json();
    document.getElementById('trainingObservations').textContent=number(training.observations);
    document.getElementById('genericOutcomes').textContent=number(training.outcomes);
    document.getElementById('trainingLabels').textContent=number(training.pointInTimeLabels);
    document.getElementById('trainingRange').textContent=training.earliestObservationUtc&&training.latestObservationUtc?`${new Date(training.earliestObservationUtc).toLocaleDateString()} – ${new Date(training.latestObservationUtc).toLocaleDateString()}`:'No observation range';
    document.getElementById('trainingStatus').textContent=training.status;
  }
  if(datasetResponse.ok){
    const datasets=await datasetResponse.json();
    if(datasets.length){const dataset=datasets[0];document.getElementById('datasetExamples').textContent=number(dataset.exampleCount);document.getElementById('datasetStatus').textContent=`${number(dataset.trainCount)} train · ${number(dataset.validationCount)} validation · ${number(dataset.testCount)} test`;}
  }
  if(baselineResponse.ok){
    const runs=await baselineResponse.json();
    if(runs.length){const run=runs[0];document.getElementById('baselineState').textContent='EVALUATED';document.getElementById('baselineMeta').textContent=`${esc(run.modelVersion)} · ${number(run.trainingSamples)} training samples`;document.getElementById('baselineMetrics').innerHTML=run.metrics.map(metric=>`<tr><td>${esc(metric.split)}</td><td>${number(metric.sampleCount)}</td><td>${Number(metric.meanAbsoluteError).toFixed(3)} ticks</td><td>${(Number(metric.directionalAccuracy)*100).toFixed(1)}%</td></tr>`).join('');}
  }
}
hydrate().catch(error=>{document.getElementById('moduleCatalog').innerHTML=`<p>${esc(error.message)}</p>`});
