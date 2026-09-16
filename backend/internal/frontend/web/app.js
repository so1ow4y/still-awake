const $ = (id) => document.getElementById(id);
const pretty = (x) => JSON.stringify(x, null, 2);
const AI_STORAGE = 'stillawake_ai_settings_v3';
const INSTALL_STORAGE = 'stillawake_installation_id';
const SESSION_STORAGE = 'stillawake_session_id';
let selectedSession = null;
let selectedInstallation = null;
let toastTimer = null;

function toast(text) {
  const el = $('toast');
  el.textContent = text;
  el.classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => el.classList.remove('show'), 2200);
}
function esc(s){return String(s ?? '').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));}
function jsonField(id, fallback={}) {
  const v = $(id).value.trim();
  if (!v) return fallback;
  return JSON.parse(v);
}
async function api(path, opts={}) {
  const init = {...opts};
  init.headers = {'Content-Type':'application/json', ...(opts.headers||{})};
  const r = await fetch(path, init);
  const text = await r.text();
  let body = text;
  try { body = text ? JSON.parse(text) : null; } catch {}
  if (!r.ok) {
    const msg = typeof body === 'object' && body ? (body.error || pretty(body)) : (body || `${r.status} ${r.statusText}`);
    throw new Error(msg);
  }
  return body;
}
function adminHeaders(){return {'X-Admin-Token':$('adminToken').value};}
function analyticsHeaders(){return {'X-Analytics-Token':$('analyticsToken').value};}
function queryString(obj) {
  const q = new URLSearchParams();
  Object.entries(obj).forEach(([k,v]) => { if (v !== '' && v != null) q.set(k, String(v)); });
  return q.toString();
}

function showView(name) {
  document.querySelectorAll('.view').forEach(v => v.classList.toggle('active', v.id === `view-${name}`));
  document.querySelectorAll('.nav-btn').forEach(b => b.classList.toggle('active', b.dataset.view === name));
  window.scrollTo({top:0, behavior:'smooth'});
  if (name === 'ai') checkServices(false);
}
document.querySelectorAll('.nav-btn').forEach(b => b.addEventListener('click', () => showView(b.dataset.view)));

function currentProvider() {
  return {
    mode: $('providerMode').value,
    base_url: $('providerBaseUrl').value.trim(),
    path: $('providerPath').value.trim(),
    model: $('providerModel').value.trim(),
    api_key: $('providerApiKey').value,
    temperature: Number($('temperature').value || 0.8)
  };
}
function publicProvider(p=currentProvider()) {
  return {mode:p.mode, base_url:p.base_url, path:p.path, model:p.model, temperature:p.temperature, api_key_set:Boolean(p.api_key)};
}
function saveAISettings() {
  const p = currentProvider();
  const safe = {
    mode:p.mode, base_url:p.base_url, path:p.path, model:p.model,
    temperature:p.temperature, system_prompt:$('systemPrompt').value,
    request_mode:$('requestMode').value, history_limit:Number($('historyLimit').value || 30)
  };
  localStorage.setItem(AI_STORAGE, JSON.stringify(safe));
  toast('AI настройки сохранены без API token');
}
function loadAISettings() {
  let v = null;
  try { v = JSON.parse(localStorage.getItem(AI_STORAGE) || 'null'); } catch {}
  if (!v) return;
  if (v.mode) $('providerMode').value = v.mode;
  if (v.base_url) $('providerBaseUrl').value = v.base_url;
  if (v.path) $('providerPath').value = v.path;
  if (v.model) $('providerModel').value = v.model;
  if (Number.isFinite(v.temperature)) $('temperature').value = v.temperature;
  if (v.system_prompt) $('systemPrompt').value = v.system_prompt;
  if (v.request_mode) $('requestMode').value = v.request_mode;
  if (v.history_limit) $('historyLimit').value = v.history_limit;
}
$('saveAISettings').onclick = saveAISettings;
$('resetAISettings').onclick = () => {
  localStorage.removeItem(AI_STORAGE);
  $('providerMode').value='openai_compatible';
  $('providerBaseUrl').value='http://127.0.0.1:1234';
  $('providerPath').value='/v1/chat/completions';
  $('providerModel').value='';
  $('providerApiKey').value='';
  $('temperature').value='0.8';
  $('requestMode').value='backend_history';
  $('historyLimit').value='30';
  toast('Настройки сброшены');
  checkServices(false);
};

async function loadModels() {
  const out = $('providerStatus');
  out.textContent = 'Проверяю provider…';
  try {
    const p = currentProvider();
    const r = await api('/v1/debug/provider/models', {method:'POST', body:pretty(p)});
    const models = r.models || [];
    $('modelOptions').innerHTML = models.map(m => `<option value="${esc(m.id)}"></option>`).join('');
    if (!$('providerModel').value && models.length) $('providerModel').value = models[0].id;
    out.textContent = models.length ? pretty(models) : 'Provider доступен, но /v1/models вернул пустой список.';
    toast(`Моделей: ${models.length}`);
    await checkProviderStatus();
  } catch(e) {
    out.textContent = e.message;
  }
}
$('loadModels').onclick = loadModels;

async function health() {
  $('backendUrl').textContent = window.location.origin;
  $('backendUrl').href = window.location.origin + '/healthz';
  try {
    await api('/healthz');
    $('health').textContent='backend online'; $('health').className='pill ok';
    $('backendStatus').textContent='ONLINE'; $('backendStatus').className='status-ok';
    return true;
  } catch {
    $('health').textContent='backend offline'; $('health').className='pill bad';
    $('backendStatus').textContent='OFFLINE'; $('backendStatus').className='status-bad';
    return false;
  }
}
async function checkProviderStatus() {
  const p = currentProvider();
  $('providerHealthUrl').textContent = p.base_url || '—';
  $('modelHealthName').textContent = p.model || 'не выбрана';
  $('providerHealth').textContent='проверка…'; $('providerHealth').className='';
  $('modelHealth').textContent='проверка…'; $('modelHealth').className='';
  $('modelHealthMeta').textContent='';
  try {
    const st = await api('/v1/debug/provider/status', {method:'POST', body:pretty(p)});
    $('providerHealth').textContent = st.online ? 'ONLINE' : 'OFFLINE';
    $('providerHealth').className = st.online ? 'status-ok' : 'status-bad';
    if (!p.model) {
      $('modelHealth').textContent='модель не выбрана';
      return st;
    }
    if (!st.model_found) {
      $('modelHealth').textContent='NOT FOUND'; $('modelHealth').className='status-bad';
    } else if (st.model_loaded === true) {
      $('modelHealth').textContent='LOADED'; $('modelHealth').className='status-ok';
    } else if (st.model_loaded === false) {
      $('modelHealth').textContent='AVAILABLE / UNLOADED'; $('modelHealth').className='status-warn';
    } else {
      $('modelHealth').textContent='AVAILABLE'; $('modelHealth').className='status-ok';
    }
    const meta=[];
    if (st.context_length) meta.push(`context ${st.context_length}`);
    if (st.max_context_length) meta.push(`max ${st.max_context_length}`);
    if (st.loaded_instance_id) meta.push(`instance ${st.loaded_instance_id}`);
    if (st.note) meta.push(st.note);
    $('modelHealthMeta').textContent=meta.join(' · ');
    return st;
  } catch(e) {
    $('providerHealth').textContent='OFFLINE'; $('providerHealth').className='status-bad';
    $('modelHealth').textContent='UNKNOWN'; $('modelHealth').className='status-bad';
    $('modelHealthMeta').textContent=e.message;
    return null;
  }
}
async function checkServices(showToast=true) {
  await health();
  await checkProviderStatus();
  if (showToast) toast('Статусы обновлены');
}
$('checkServices').onclick = () => checkServices(true);
['providerBaseUrl','providerModel','providerMode'].forEach(id => $(id).addEventListener('change', () => checkProviderStatus()));

function updateIdentityUI() {
  $('identityInstallation').textContent = $('installationId').value || '—';
  $('identitySession').textContent = $('sessionId').value || '—';
}
async function createInstallation() {
  const r = await api('/v1/installations', {method:'POST', body:pretty({metadata:{source:'backend-lab', platform:navigator.platform || 'browser'}})});
  $('installationId').value = r.installation_id;
  localStorage.setItem(INSTALL_STORAGE, r.installation_id);
  updateIdentityUI();
  return r.installation_id;
}
async function createSession() {
  let installationID = $('installationId').value.trim();
  if (!installationID) installationID = await createInstallation();
  const r = await api('/v1/sessions', {method:'POST', body:pretty({
    installation_id:installationID,
    game_version:$('gameVersion').value,
    scenario:'night-1',
    state:jsonField('gameState', {})
  })});
  $('sessionId').value = r.session_id;
  localStorage.setItem(SESSION_STORAGE, r.session_id);
  updateIdentityUI();
  await loadHistory();
  await refreshRawAndPreview();
  return r.session_id;
}
$('createInstallation').onclick = async () => { try { await createInstallation(); toast('Installation создан'); } catch(e){$('response').textContent=e.message;} };
$('createSession').onclick = async () => { try { await createSession(); toast('Session создана'); } catch(e){$('response').textContent=e.message;} };
$('initClient').onclick = async () => {
  try { await createInstallation(); await createSession(); toast('Клиент и чат готовы'); }
  catch(e) { $('response').textContent = e.message; }
};
['installationId','sessionId'].forEach(id => $(id).addEventListener('input', updateIdentityUI));

function buildBody(includeSecret=true) {
  const p = currentProvider();
  const provider = {mode:p.mode, base_url:p.base_url, path:p.path, model:p.model, temperature:p.temperature};
  if (includeSecret && p.api_key) provider.api_key = p.api_key;
  return {
    request_mode: $('requestMode').value,
    history_limit: Number($('historyLimit').value || 30),
    user_message: $('userMessage').value,
    game_state: jsonField('gameState', {}),
    context: jsonField('extraContext', {}),
    system_prompt: $('systemPrompt').value,
    provider
  };
}
function refreshRaw() {
  try { $('rawRequest').value = pretty(buildBody(false)); }
  catch(e) { $('rawRequest').value = pretty({error:e.message}); }
}
function renderResolvedPrompt(data) {
  if (!data) return;
  const messages = data.messages || data.resolved_messages || [];
  const count = data.history_count ?? 0;
  const limit = data.history_limit ?? Number($('historyLimit').value || 30);
  $('historyDebugBadge').textContent = `history: ${count}/${limit}`;
  $('resolvedPrompt').textContent = pretty({
    request_mode:data.request_mode || $('requestMode').value,
    history_limit:limit,
    history_count:count,
    provider:data.provider || publicProvider(),
    messages
  });
}
async function previewPrompt() {
  const id = $('sessionId').value.trim();
  if (!id) {
    $('resolvedPrompt').textContent='Сначала создай session: history хранится и выбирается по session_id.';
    $('historyDebugBadge').textContent='history: —';
    return;
  }
  try {
    const body = buildBody(true);
    const r = await api(`/v1/sessions/${encodeURIComponent(id)}/prompt-preview`, {method:'POST', body:pretty(body)});
    renderResolvedPrompt(r);
  } catch(e) {
    $('resolvedPrompt').textContent=e.message;
  }
}
async function refreshRawAndPreview() { refreshRaw(); await previewPrompt(); }
$('buildRequest').onclick = refreshRawAndPreview;

function renderMetrics(r) {
  const u = r?.usage || {};
  const p = r?.performance || {};
  const cells = $('lastMetrics').children;
  cells[0].querySelector('strong').textContent = u.prompt_tokens ?? 0;
  cells[1].querySelector('strong').textContent = u.completion_tokens ?? 0;
  cells[2].querySelector('strong').textContent = r?.latency_ms != null ? `${r.latency_ms} ms` : '—';
  cells[3].querySelector('strong').textContent = p.tokens_per_second ? Number(p.tokens_per_second).toFixed(1) : '—';
}
function renderTranscript(messages) {
  const root = $('chatTranscript');
  root.innerHTML = '';
  if (!messages?.length) { root.innerHTML = '<div class="empty">История пуста.</div>'; return; }
  messages.forEach(m => {
    const b = document.createElement('div');
    b.className = `bubble ${m.role === 'user' ? 'user' : 'assistant'}`;
    const text = document.createElement('div'); text.textContent = m.content;
    const meta = document.createElement('span'); meta.className='meta'; meta.textContent = `${m.role}${m.model ? ' · '+m.model : ''} · ${m.created_at || ''}`;
    b.append(text, meta); root.append(b);
  });
  root.scrollTop = root.scrollHeight;
}
async function loadHistory() {
  const id = $('sessionId').value.trim();
  if (!id) { renderTranscript([]); return; }
  try { renderTranscript(await api(`/v1/sessions/${encodeURIComponent(id)}/messages`)); }
  catch(e) { $('chatTranscript').innerHTML = `<div class="empty">${esc(e.message)}</div>`; }
}
$('loadHistory').onclick = loadHistory;

async function sendBody(body) {
  let id = $('sessionId').value.trim();
  if (!id) id = await createSession();
  const payload = typeof body === 'string' ? body : pretty(body);
  const r = await api(`/v1/sessions/${encodeURIComponent(id)}/chat`, {method:'POST', body:payload});
  $('response').textContent = pretty(r);
  renderMetrics(r);
  if (r.debug) renderResolvedPrompt({
    ...r.debug,
    request_mode:r.request_mode,
    messages:r.debug.resolved_messages
  });
  await loadHistory();
  return r;
}
$('sendMessage').onclick = async () => {
  try {
    const msg = $('userMessage').value.trim();
    if (!msg) return toast('Напиши сообщение');
    refreshRaw();
    await sendBody(buildBody(true));
    $('userMessage').value='';
    refreshRaw();
  } catch(e) { $('response').textContent = e.message; }
};
$('userMessage').addEventListener('keydown', e => {
  if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') $('sendMessage').click();
});
$('sendRaw').onclick = async () => {
  try {
    const parsed = JSON.parse($('rawRequest').value);
    if (!parsed.provider) parsed.provider = currentProvider();
    else if (!parsed.provider.api_key && currentProvider().api_key) parsed.provider.api_key = currentProvider().api_key;
    await sendBody(parsed);
  } catch(e) { $('response').textContent = e.message; }
};

function renderAnalyticsSummary(v) {
  const data = [
    ['requests',v.requests],['success',v.success],['errors',v.errors],['users',v.active_installations],['sessions',v.active_sessions],
    ['input tokens',v.prompt_tokens],['output tokens',v.completion_tokens],['total tokens',v.total_tokens],
    ['avg latency',`${Math.round(v.avg_latency_ms||0)} ms`],['avg tok/s',Number(v.avg_tokens_per_second||0).toFixed(1)],['avg TTFT',`${Math.round(v.avg_time_to_first_token_ms||0)} ms`]
  ];
  $('analyticsSummary').innerHTML=data.map(([k,n])=>`<div><strong>${esc(n ?? 0)}</strong><span>${esc(k)}</span></div>`).join('');
}
function renderAnalyticsDaily(rows) {
  const root=$('analyticsDaily'); root.innerHTML='';
  const max=Math.max(1,...(rows||[]).map(x=>x.requests||0));
  (rows||[]).forEach(x=>{
    const d=document.createElement('div'); d.className='analytics-row';
    d.innerHTML=`<div class="analytics-row-head"><b>${esc(x.day)}</b><span>${x.requests} req · ${x.errors} errors · ${x.total_tokens} tok</span></div><div class="bar"><i style="width:${Math.max(2,(x.requests/max)*100)}%"></i></div>`;
    root.append(d);
  }); if(!(rows||[]).length) root.textContent='Нет данных';
}
function renderAnalyticsModels(rows) {
  const root=$('analyticsModels');root.innerHTML='';
  (rows||[]).forEach(x=>{
    const d=document.createElement('div');d.className='analytics-row';
    d.innerHTML=`<b>${esc(x.model)}</b><small>${esc(x.provider)} · ${x.requests} req · ${x.errors} errors · ${x.total_tokens} tok · ${Math.round(x.avg_latency_ms||0)} ms · ${Number(x.avg_tokens_per_second||0).toFixed(1)} tok/s</small>`;
    root.append(d);
  }); if(!(rows||[]).length)root.textContent='Нет данных';
}
function renderAnalyticsUsers(rows) {
  const root=$('analyticsUsers');root.innerHTML='';
  (rows||[]).forEach(x=>{
    const d=document.createElement('div');d.className='analytics-row user-analytics-row';
    d.innerHTML=`<div><code>${esc(x.installation_id)}</code><small>${x.requests} req · ${x.sessions} sessions · ${x.errors} errors · ${x.total_tokens} tok · avg ${Math.round(x.avg_latency_ms||0)} ms</small><small>last: ${esc(x.last_request_at||'—')}</small></div>`;
    const b=document.createElement('button');b.className='btn mini';b.textContent='Открыть в Admin';b.onclick=()=>{showView('admin');$('adminSearch').value=x.installation_id;runAdminSearch();selectInstallation(x.installation_id);};
    d.append(b);root.append(d);
  }); if(!(rows||[]).length)root.textContent='Нет данных';
}
async function refreshAnalytics() {
  try {
    const days=Number($('analyticsDays').value||0);
    const v=await api(`/v1/analytics/summary?days=${days}`,{headers:analyticsHeaders()});
    renderAnalyticsSummary(v); renderAnalyticsDaily(v.daily); renderAnalyticsModels(v.models); renderAnalyticsUsers(v.users);
  } catch(e) {
    $('analyticsSummary').innerHTML=`<div><strong>!</strong><span>${esc(e.message)}</span></div>`;
  }
}
$('refreshAnalytics').onclick=refreshAnalytics;

function renderOverview(v) {
  const c = v.counts || {};
  const data = [['installations',c.installations],['sessions',c.sessions],['messages',c.messages],['events',c.events],['llm requests',c.llm_requests]];
  $('overview').innerHTML = data.map(([k,n])=>`<div><strong>${n ?? 0}</strong><span>${k}</span></div>`).join('');
}
function adminSearchValue(){return $('adminSearch').value.trim();}
async function loadInstallations(query=adminSearchValue()){
  const qs=queryString({limit:200,q:query});
  const rows = await api(`/v1/admin/installations?${qs}`,{headers:adminHeaders()});
  const root=$('installations');root.innerHTML='';
  rows.forEach(x=>{
    const div=document.createElement('div');div.className='dbrow';
    div.innerHTML=`<div class="grow"><code>${esc(x.id)}</code><small>${esc(x.created_at)}</small><pre>${esc(pretty(x.metadata||{}))}</pre></div>`;
    const a=document.createElement('div');a.className='actions';
    const history=document.createElement('button');history.textContent='History';history.onclick=()=>selectInstallation(x.id);
    const use=document.createElement('button');use.textContent='Use';use.onclick=()=>{$('installationId').value=x.id;localStorage.setItem(INSTALL_STORAGE,x.id);updateIdentityUI();showView('chat');};
    const edit=document.createElement('button');edit.textContent='Edit';edit.onclick=async()=>{const raw=prompt('metadata JSON',pretty(x.metadata||{}));if(raw===null)return;let m;try{m=JSON.parse(raw)}catch(e){return alert(e.message)};await api(`/v1/admin/installations/${encodeURIComponent(x.id)}`,{method:'PATCH',headers:adminHeaders(),body:pretty({metadata:m})});await refreshAdmin();};
    const del=document.createElement('button');del.textContent='Delete';del.className='danger';del.onclick=async()=>{if(!confirm(`Delete ${x.id} and cascaded data?`))return;await api(`/v1/admin/installations/${encodeURIComponent(x.id)}`,{method:'DELETE',headers:adminHeaders()});await refreshAdmin();};
    a.append(history,use,edit,del);div.append(a);root.append(div);
  }); if(!rows.length)root.textContent='No installations';
  return rows;
}
function renderSessionRows(root, rows) {
  root.innerHTML='';
  rows.forEach(s=>{
    const div=document.createElement('div');div.className='dbrow';
    div.innerHTML=`<div class="grow"><code>${esc(s.id)}</code><small>user: ${esc(s.installation_id)}</small><small>${esc(s.scenario)} · ${esc(s.game_version)} · ${esc(s.updated_at)}</small><pre>${esc(pretty(s.state||{}))}</pre></div>`;
    const a=document.createElement('div');a.className='actions';
    const use=document.createElement('button');use.textContent='Use';use.onclick=()=>{$('sessionId').value=s.id;$('installationId').value=s.installation_id;$('gameState').value=pretty(s.state||{});localStorage.setItem(SESSION_STORAGE,s.id);localStorage.setItem(INSTALL_STORAGE,s.installation_id);updateIdentityUI();showView('chat');loadHistory();};
    const inspect=document.createElement('button');inspect.textContent='Inspect';inspect.onclick=()=>selectSession(s.id);
    const del=document.createElement('button');del.textContent='Delete';del.className='danger';del.onclick=async()=>{if(!confirm(`Delete session ${s.id}?`))return;await api(`/v1/admin/sessions/${encodeURIComponent(s.id)}`,{method:'DELETE',headers:adminHeaders()});await refreshAdmin();};
    a.append(use,inspect,del);div.append(a);root.append(div);
  }); if(!rows.length)root.textContent='No sessions';
}
async function loadSessionsAdmin(query=adminSearchValue(), installationID=''){
  const qs=queryString({limit:200,q:query,installation_id:installationID});
  const rows=await api(`/v1/admin/sessions?${qs}`,{headers:adminHeaders()});
  renderSessionRows($('sessions'),rows); return rows;
}
async function loadAdminMessages(id){
  const rows=await api(`/v1/admin/sessions/${encodeURIComponent(id)}/messages?limit=500`,{headers:adminHeaders()});
  const root=$('adminMessages');root.innerHTML='';
  rows.forEach(m=>{
    const d=document.createElement('div');d.className='dbrow';d.innerHTML=`<div class="grow"><b>#${m.id} ${esc(m.role)}</b><small>${esc(m.provider||'')} ${esc(m.model||'')} · ${esc(m.created_at)}</small><pre>${esc(m.content)}</pre></div>`;
    const a=document.createElement('div');a.className='actions';
    const edit=document.createElement('button');edit.textContent='Edit';edit.onclick=async()=>{const raw=prompt('Message JSON',pretty({role:m.role,content:m.content,provider:m.provider||'',model:m.model||''}));if(raw===null)return;let v;try{v=JSON.parse(raw)}catch(e){return alert(e.message)};await api(`/v1/admin/messages/${m.id}`,{method:'PATCH',headers:adminHeaders(),body:pretty(v)});await loadAdminMessages(id);};
    const del=document.createElement('button');del.textContent='Delete';del.className='danger';del.onclick=async()=>{if(!confirm(`Delete message #${m.id}?`))return;await api(`/v1/admin/messages/${m.id}`,{method:'DELETE',headers:adminHeaders()});await loadAdminMessages(id);};
    a.append(edit,del);d.append(a);root.append(d);
  });if(!rows.length)root.textContent='No messages';
}
async function loadAdminEvents(id){
  const rows=await api(`/v1/admin/sessions/${encodeURIComponent(id)}/events?limit=500`,{headers:adminHeaders()});
  const root=$('adminEvents');root.innerHTML='';
  rows.forEach(e=>{
    const d=document.createElement('div');d.className='dbrow';d.innerHTML=`<div class="grow"><b>#${e.id} ${esc(e.type)}</b><small>seq=${e.client_seq} · ${esc(e.created_at)}</small><pre>${esc(pretty(e.payload||{}))}</pre></div>`;
    const a=document.createElement('div');a.className='actions';
    const edit=document.createElement('button');edit.textContent='Edit';edit.onclick=async()=>{const raw=prompt('Event JSON',pretty({client_seq:e.client_seq,type:e.type,payload:e.payload||{}}));if(raw===null)return;let v;try{v=JSON.parse(raw)}catch(err){return alert(err.message)};await api(`/v1/admin/events/${e.id}`,{method:'PATCH',headers:adminHeaders(),body:pretty(v)});await loadAdminEvents(id);};
    const del=document.createElement('button');del.textContent='Delete';del.className='danger';del.onclick=async()=>{if(!confirm(`Delete event #${e.id}?`))return;await api(`/v1/admin/events/${e.id}`,{method:'DELETE',headers:adminHeaders()});await loadAdminEvents(id);};
    a.append(edit,del);d.append(a);root.append(d);
  });if(!rows.length)root.textContent='No events';
}
function renderLLMRows(root, rows) {
  root.innerHTML='';
  rows.forEach(x=>{
    const details=document.createElement('details');details.className='request-detail';
    const perf=x.tokens_per_second?`${Number(x.tokens_per_second).toFixed(1)} tok/s`:'—';
    const prompt=x.request_messages || [];
    details.innerHTML=`<summary><b>#${x.id} ${esc(x.status)} · ${esc(x.model||x.provider)}</b><span>${esc(x.installation_id)} · ${esc(x.session_id)} · ${x.prompt_tokens}→${x.completion_tokens} tok · ${x.latency_ms} ms · ${perf}</span></summary>
      <div class="request-body">
        <div class="request-pair"><div><small>PLAYER / user message #${x.user_message_id||'—'}</small><pre>${esc(x.user_message||'—')}</pre></div><div><small>MODEL / assistant #${x.assistant_message_id||'—'}</small><pre>${esc(x.assistant_message||x.error_text||'—')}</pre></div></div>
        <div class="request-meta"><code>user=${esc(x.installation_id)}</code><code>session=${esc(x.session_id)}</code><code>mode=${esc(x.request_mode)}</code><code>history=${x.history_count ?? 0}</code><code>${esc(x.endpoint||'')}</code></div>
        <details><summary>Фактический messages[] отправленный модели</summary><pre class="request-json">${esc(pretty(prompt))}</pre></details>
        ${x.error_text?`<div class="notice warn">${esc(x.error_text)}</div>`:''}
      </div>`;
    const actions=document.createElement('div');actions.className='request-actions';
    const user=document.createElement('button');user.className='btn mini';user.textContent='User';user.onclick=()=>selectInstallation(x.installation_id);
    const session=document.createElement('button');session.className='btn mini';session.textContent='Session';session.onclick=()=>selectSession(x.session_id);
    const del=document.createElement('button');del.className='btn mini danger';del.textContent='Delete';del.onclick=async(e)=>{e.preventDefault();if(!confirm(`Delete LLM request #${x.id}?`))return;await api(`/v1/admin/llm-requests/${x.id}`,{method:'DELETE',headers:adminHeaders()});await refreshAdmin();};
    actions.append(user,session,del);details.querySelector('.request-body').append(actions);root.append(details);
  });if(!rows.length)root.textContent='No LLM requests';
}
async function loadLLMRequests(query=adminSearchValue(), installationID='', sessionID=''){
  const qs=queryString({limit:300,q:query,installation_id:installationID,session_id:sessionID});
  const rows=await api(`/v1/admin/llm-requests?${qs}`,{headers:adminHeaders()});renderLLMRows($('llmRequests'),rows);return rows;
}
async function loadSessionLLMRequests(id){
  const rows=await api(`/v1/admin/sessions/${encodeURIComponent(id)}/llm-requests?limit=300`,{headers:adminHeaders()});renderLLMRows($('sessionLLMRequests'),rows);
}
async function selectInstallation(id){
  if(!id)return;
  selectedInstallation=id;
  $('installationInspector').classList.remove('hidden');$('selectedInstallationId').textContent=id;
  const [sessions,requests]=await Promise.all([
    api(`/v1/admin/sessions?${queryString({limit:500,installation_id:id})}`,{headers:adminHeaders()}),
    api(`/v1/admin/llm-requests?${queryString({limit:500,installation_id:id})}`,{headers:adminHeaders()})
  ]);
  renderSessionRows($('userSessions'),sessions);renderLLMRows($('userLLMRequests'),requests);
  $('installationInspector').scrollIntoView({behavior:'smooth',block:'start'});
}
$('reloadSelectedInstallation').onclick=()=>selectedInstallation&&selectInstallation(selectedInstallation);
async function selectSession(id){
  if(!id)return;
  selectedSession=await api(`/v1/sessions/${encodeURIComponent(id)}`);
  $('sessionInspector').classList.remove('hidden');$('selectedSessionId').textContent=id;
  $('adminSessionInstallation').value=selectedSession.installation_id||'';$('adminGameVersion').value=selectedSession.game_version||'';$('adminScenario').value=selectedSession.scenario||'';$('adminSessionState').value=pretty(selectedSession.state||{});
  await Promise.all([loadAdminMessages(id),loadAdminEvents(id),loadSessionLLMRequests(id)]);
  $('sessionInspector').scrollIntoView({behavior:'smooth',block:'start'});
}
$('reloadSelectedSession').onclick=()=>selectedSession&&selectSession(selectedSession.id);
$('saveSession').onclick=async()=>{if(!selectedSession)return;try{await api(`/v1/admin/sessions/${encodeURIComponent(selectedSession.id)}`,{method:'PATCH',headers:adminHeaders(),body:pretty({game_version:$('adminGameVersion').value,scenario:$('adminScenario').value,state:jsonField('adminSessionState',{})})});await refreshAdmin();}catch(e){alert(e.message)}};
$('deleteSelectedSession').onclick=async()=>{if(!selectedSession)return;const id=selectedSession.id;if(!confirm(`Delete session ${id}?`))return;await api(`/v1/admin/sessions/${encodeURIComponent(id)}`,{method:'DELETE',headers:adminHeaders()});selectedSession=null;$('sessionInspector').classList.add('hidden');await refreshAdmin();};

async function loadSchema(){
  const r=await api('/v1/admin/db/schema',{headers:adminHeaders()});
  const root=$('dbSchema');root.innerHTML='';
  (r.objects||[]).forEach(o=>{const d=document.createElement('div');d.className='schema-item';d.innerHTML=`<b>${esc(o.type)} · ${esc(o.name)}</b><small>table: ${esc(o.table)}</small><pre>${esc(o.sql||'')}</pre>`;root.append(d)});
  if(!(r.objects||[]).length)root.textContent='Schema is empty';
  $('allowWriteSQL').disabled=!r.sql_writes_enabled;
  if(!r.sql_writes_enabled)$('allowWriteSQL').checked=false;
}
function renderSQLResult(r){
  const root=$('sqlResult');
  if(r.kind==='exec'){root.innerHTML=`<div class="notice">rows affected: <b>${r.rows_affected||0}</b> · last insert id: <b>${r.last_insert_id||0}</b></div>`;return;}
  const cols=r.columns||[],rows=r.rows||[];
  if(!cols.length){root.textContent=pretty(r);return;}
  let html='<table class="sql-table"><thead><tr>'+cols.map(c=>`<th>${esc(c)}</th>`).join('')+'</tr></thead><tbody>';
  html+=rows.map(row=>'<tr>'+cols.map(c=>`<td>${esc(typeof row[c]==='object'?pretty(row[c]):row[c])}</td>`).join('')+'</tr>').join('');
  html+='</tbody></table>';root.innerHTML=html;
}
$('loadSchema').onclick=async()=>{try{await loadSchema()}catch(e){$('dbSchema').textContent=e.message}};
$('runSQL').onclick=async()=>{try{const r=await api('/v1/admin/db/query',{method:'POST',headers:adminHeaders(),body:pretty({sql:$('sqlQuery').value,allow_write:$('allowWriteSQL').checked})});renderSQLResult(r);await Promise.all([loadSchema(),loadOverviewOnly()]);}catch(e){$('sqlResult').textContent=e.message}};
async function loadOverviewOnly(){const v=await api('/v1/admin/overview',{headers:adminHeaders()});renderOverview(v);}
async function refreshAdmin(){
  try {
    const q=adminSearchValue();
    const [v]=await Promise.all([api('/v1/admin/overview',{headers:adminHeaders()}),loadInstallations(q),loadSessionsAdmin(q),loadLLMRequests(q),loadSchema()]);
    renderOverview(v);
    if(selectedInstallation)await selectInstallation(selectedInstallation);
    if(selectedSession)await selectSession(selectedSession.id);
  } catch(e) { $('overview').innerHTML=`<div><strong>!</strong><span>${esc(e.message)}</span></div>`; }
}
$('refreshAdmin').onclick=refreshAdmin;
async function runAdminSearch(){await refreshAdmin();}
$('runAdminSearch').onclick=runAdminSearch;
$('adminSearch').addEventListener('keydown',e=>{if(e.key==='Enter')runAdminSearch();});
$('clearAdminSearch').onclick=()=>{$('adminSearch').value='';selectedInstallation=null;$('installationInspector').classList.add('hidden');refreshAdmin();};

(async function boot(){
  loadAISettings();
  $('installationId').value=localStorage.getItem(INSTALL_STORAGE)||'';
  $('sessionId').value=localStorage.getItem(SESSION_STORAGE)||'';
  updateIdentityUI();
  refreshRaw();
  await health();
  await checkProviderStatus();
  if($('sessionId').value) { await loadHistory(); await previewPrompt(); }
  setInterval(() => { if(document.visibilityState==='visible') checkServices(false); }, 15000);
})();
