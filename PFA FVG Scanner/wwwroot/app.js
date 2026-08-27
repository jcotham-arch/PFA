const candidates=[
 {name:'Bullish · Boundary Touch · 1.5R',days:5,trades:66,expectancy:.268,pf:1.537,stability:84,score:54.02},
 {name:'75% Entry · 3R',days:5,trades:50,expectancy:.520,pf:1.839,stability:72,score:48.20},
 {name:'Bullish · 25% Entry · 1.5R',days:5,trades:56,expectancy:.356,pf:1.767,stability:76,score:47.34},
 {name:'Bullish · 50% Entry · 2R',days:5,trades:51,expectancy:.412,pf:1.682,stability:68,score:45.91}
];
const titles={overview:['Command center','Market intelligence'],market:['Canonical timeline','Market explorer'],patterns:['Pattern module #1','Fair value gaps'],research:['Hypothesis laboratory','Research candidates'],evidence:['Independent evidence','Cross-day review'],system:['Operations','System health']};
const rows=document.getElementById('candidateRows');
rows.innerHTML=candidates.map((c,i)=>`<tr><td>${c.name}</td><td class="mono">${c.days}/5</td><td class="mono">${c.trades}</td><td class="mono up">+${c.expectancy.toFixed(3)}R</td><td class="mono">${c.pf.toFixed(3)}</td><td><div class="stability-bar"><i style="width:${c.stability}%"></i></div></td><td><button class="row-button" data-candidate="${i}" aria-label="Open ${c.name}">›</button></td></tr>`).join('');
document.getElementById('candidateCards').innerHTML=candidates.map((c,i)=>`<article data-candidate="${i}"><p class="eyebrow">Persistence score ${c.score.toFixed(2)}</p><h3>${c.name}</h3><p>Observed on ${c.days} independent trading days with ${c.trades} distinct FVGs.</p><div class="candidate-card-stats"><span>+${c.expectancy.toFixed(3)}R<small>Expectancy</small></span><span>${c.pf.toFixed(3)}<small>Profit factor</small></span></div></article>`).join('');

function showView(id){document.querySelectorAll('.view').forEach(v=>v.classList.toggle('active',v.id===id));document.querySelectorAll('[data-view]').forEach(b=>b.classList.toggle('active',b.dataset.view===id));document.getElementById('viewEyebrow').textContent=titles[id][0];document.getElementById('viewTitle').textContent=titles[id][1];window.scrollTo({top:0,behavior:'smooth'});}
document.querySelectorAll('[data-view]').forEach(b=>b.addEventListener('click',()=>showView(b.dataset.view)));
document.querySelectorAll('[data-view-link]').forEach(b=>b.addEventListener('click',()=>showView(b.dataset.viewLink)));
document.querySelectorAll('.segmented button').forEach(b=>b.addEventListener('click',()=>{b.parentElement.querySelectorAll('button').forEach(x=>x.classList.remove('active'));b.classList.add('active')}));

const dialog=document.getElementById('candidateDialog');
function openCandidate(index){const c=candidates[index];document.getElementById('dialogTitle').textContent=c.name;document.getElementById('dialogStats').innerHTML=`<div><strong>+${c.expectancy.toFixed(3)}R</strong><small>Expectancy</small></div><div><strong>${c.pf.toFixed(3)}</strong><small>Profit factor</small></div><div><strong>${c.trades}</strong><small>Distinct FVGs</small></div>`;dialog.showModal();}
document.addEventListener('click',e=>{const target=e.target.closest('[data-candidate]');if(target)openCandidate(target.dataset.candidate)});
dialog.querySelector('.dialog-close').addEventListener('click',()=>dialog.close());
dialog.addEventListener('click',e=>{if(e.target===dialog)dialog.close()});
function showToast(message){const toast=document.getElementById('toast');toast.lastChild.textContent=` ${message}`;toast.classList.add('show');setTimeout(()=>toast.classList.remove('show'),2200)}
document.getElementById('refreshButton').addEventListener('click',async()=>{await hydrateOperationalData();showToast('Market intelligence refreshed')});
document.getElementById('prepareValidationButton').addEventListener('click',()=>showToast('Validation preparation remains locked until its governance phase'));

function drawChart(){const canvas=document.getElementById('marketChart');if(!canvas)return;const dpr=window.devicePixelRatio||1;const rect=canvas.getBoundingClientRect();canvas.width=rect.width*dpr;canvas.height=rect.height*dpr;const ctx=canvas.getContext('2d');ctx.scale(dpr,dpr);const w=rect.width,h=rect.height,pad=16;ctx.clearRect(0,0,w,h);ctx.strokeStyle='#1d2530';ctx.lineWidth=1;for(let i=1;i<6;i++){ctx.beginPath();ctx.moveTo(pad,(h/6)*i);ctx.lineTo(w-pad,(h/6)*i);ctx.stroke()}for(let i=1;i<10;i++){ctx.beginPath();ctx.moveTo((w/10)*i,pad);ctx.lineTo((w/10)*i,h-pad);ctx.stroke()}const points=[.69,.66,.72,.64,.59,.63,.51,.55,.48,.43,.47,.38,.42,.35,.29,.32,.25,.27,.19,.23,.16,.22,.14,.18,.11,.15,.08];ctx.beginPath();points.forEach((p,i)=>{const x=pad+i*(w-pad*2)/(points.length-1),y=pad+p*(h-pad*2);i?ctx.lineTo(x,y):ctx.moveTo(x,y)});const gradient=ctx.createLinearGradient(0,0,w,0);gradient.addColorStop(0,'#4f7cff');gradient.addColorStop(1,'#49dfac');ctx.strokeStyle=gradient;ctx.lineWidth=2;ctx.stroke();ctx.lineTo(w-pad,h-pad);ctx.lineTo(pad,h-pad);ctx.closePath();const fill=ctx.createLinearGradient(0,0,0,h);fill.addColorStop(0,'#4fa8ff28');fill.addColorStop(1,'#4fa8ff00');ctx.fillStyle=fill;ctx.fill();ctx.fillStyle='#f4bf5818';ctx.fillRect(w*.17,h*.39,w*.28,h*.13);ctx.strokeStyle='#f4bf5866';ctx.setLineDash([4,4]);ctx.strokeRect(w*.17,h*.39,w*.28,h*.13);ctx.setLineDash([])}
drawChart();window.addEventListener('resize',drawChart);

async function hydrateOperationalData(){try{const response=await fetch('/api/product/overview');if(!response.ok)return false;const data=await response.json();if(data.canonical.bars>0){document.querySelectorAll('[data-live="canonicalBars"]').forEach(el=>el.textContent=el.tagName==='EM'?`${data.canonical.bars.toLocaleString()} bars`:data.canonical.bars.toLocaleString())}if(data.features.definitions>0){document.querySelectorAll('[data-live="featureDefinitions"]').forEach(el=>el.textContent=`${data.features.definitions} definitions`)}return true}catch{ return false }}
hydrateOperationalData();
