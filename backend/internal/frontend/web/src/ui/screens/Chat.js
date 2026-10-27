// Отладочный чат: та же цепочка, что в игре (активный промпт + правила сервера + история из SQLite + ситуация ночи →
// модель), но от имени тестовой установки. Можно подставить черновик промпта, вызвать событие «сталкер пишет первым»
// и посмотреть фактический messages[], который уйдёт модели.

import { html, useState, useEffect, useRef } from '../html.js';
import { api, pretty, parseJSON } from '../api.js';
import { useLoad, readLocal, writeLocal } from '../hooks.js';
import { usePageKeys } from '../pageKeys.js';
import { fmtTime, fmtNum } from '../format.js';
import { bindingOf } from '../keys.js';
import { Field, Input, Textarea, Select, Check, ActionButton, ErrorText, Empty, Badge, CodeBlock } from '../components/ui.js';
import { Keys } from '../components/Shortcuts.js';
import { toast } from '../components/Toast.js';
import { ScreenHead, Stat } from './common.js';
import { DRAFT_KEY } from './Prompt.js';

// То же, что присылает игра: сервер берёт из состояния только известные поля и значения.
const DEFAULT_STATE = { clock: '01:30', phase: 'strange', stalker: 'window', light_on: true, curtains_closed: false, door_locked: false, hidden: 'none', visits: 0, phone_sound: true, items: [] };

// Поводы, по которым сталкер пишет первым (список — gameEvents в prompt.go).
const EVENTS = [
  ['', 'нет — ответ на сообщение'],
  ['first_contact', 'первое сообщение за ночь'],
  ['stalker_at_window', 'подошёл к окну'],
  ['seen_at_window', 'она увидела его в окне'],
  ['light_off', 'она выключила свет'],
  ['curtains_closed', 'задёрнула шторы'],
  ['door_locked', 'дверь заперта'],
  ['door_open', 'дверь не заперта'],
  ['stalker_entered', 'вошёл в комнату'],
  ['visit_survived', 'ушёл ни с чем'],
  ['danger_phase', 'скоро рассвет'],
  ['silence', 'она молчит'],
  ['dawn', 'рассвет, уходит'],
];

export function ChatScreen({ query = {} }) {
  const provider = useLoad(() => api('/v1/llm/provider'));
  const prompt = useLoad(() => api('/v1/llm/prompt'));
  const [installation, setInstallation] = useState(() => readLocal('lab.installation', ''));
  const [sessionId, setSessionId] = useState(() => readLocal('lab.session', ''));
  const [messages, setMessages] = useState([]);
  const [text, setText] = useState('');
  const [state, setState] = useState(() => readLocal('lab.state', pretty(DEFAULT_STATE)));
  const [extra, setExtra] = useState('{}');
  const [event, setEvent] = useState('');
  const [historyLimit, setHistoryLimit] = useState(30);
  const [useDraft, setUseDraft] = useState(query.draft === '1');
  const [draft, setDraft] = useState(() => sessionStorage.getItem(DRAFT_KEY) || '');
  const [preview, setPreview] = useState(null);
  const [last, setLast] = useState(null);
  const [error, setError] = useState(null);
  const [sending, setSending] = useState(false);
  const transcript = useRef(null);

  const overrides = !!provider.data?.overrides_allowed;
  useEffect(() => {
    if (!draft && prompt.data) setDraft(prompt.data.text);
  }, [prompt.data]);
  useEffect(() => writeLocal('lab.state', state), [state]);
  useEffect(() => {
    if (transcript.current) transcript.current.scrollTop = transcript.current.scrollHeight;
  }, [messages]);

  const loadHistory = async (id = sessionId) => {
    if (!id) return setMessages([]);
    setMessages((await api(`/v1/sessions/${encodeURIComponent(id)}/messages`)) || []);
  };
  useEffect(() => { loadHistory().catch((e) => setError(e)); }, [sessionId]);

  const newSession = async () => {
    let inst = installation;
    if (!inst) {
      inst = (await api('/v1/installations', { method: 'POST', body: { metadata: { source: 'panel-lab', platform: navigator.platform || 'browser' } } })).installation_id;
      setInstallation(inst);
      writeLocal('lab.installation', inst);
    }
    const r = await api('/v1/sessions', { method: 'POST', body: { installation_id: inst, game_version: 'lab', scenario: 'night-1', state: parseJSON(state) } });
    setSessionId(r.session_id);
    writeLocal('lab.session', r.session_id);
    setPreview(null);
    setLast(null);
    toast('Новая сессия');
    return r.session_id;
  };

  const body = (withMessage) => ({
    history_limit: Number(historyLimit) || 30,
    user_message: withMessage && !event ? text.trim() : '',
    ...(event ? { event } : {}),
    game_state: parseJSON(state),
    context: parseJSON(extra),
    ...(useDraft && overrides && draft.trim() ? { system_prompt: draft } : {}),
  });

  const runPreview = async () => {
    const id = sessionId || (await newSession());
    setPreview(await api(`/v1/sessions/${encodeURIComponent(id)}/prompt-preview`, { method: 'POST', body: body(true) }));
  };

  const send = async () => {
    if ((!text.trim() && !event) || sending) return;
    setSending(true);
    setError(null);
    try {
      const id = sessionId || (await newSession());
      const r = await api(`/v1/sessions/${encodeURIComponent(id)}/chat`, { method: 'POST', body: body(true) });
      setLast(r);
      setPreview({ ...r.debug, messages: r.debug.resolved_messages });
      if (event) setEvent('');
      else setText('');
      await loadHistory(id);
    } catch (err) {
      setError(err);
      await loadHistory().catch(() => {});
    } finally {
      setSending(false);
    }
  };

  usePageKeys({ send: () => { send(); return true; }, refresh: () => { loadHistory().catch(toast.error); } });

  const usage = last?.usage || {};
  return html`
    <div class="screen">
      <${ScreenHead} title="Отладочный чат" desc="Та же цепочка, что в игре: история из SQLite, состояние игры и активный промпт."
        actions=${html`<${ActionButton} className="btn" onClick=${newSession}>Новая сессия<//>`}/>
      <div class="identity-line">
        <span class="muted small">установка</span><code>${installation || '—'}</code>
        <span class="muted small">сессия</span><code>${sessionId || '—'}</code>
        ${provider.data ? html`<${Badge}>${provider.data.provider.mode === 'mock' ? 'mock' : provider.data.provider.model || 'модель не выбрана'}<//>` : null}
        <${Badge} tone=${useDraft && overrides ? 'warn' : ''}>${useDraft && overrides ? 'черновик промпта' : prompt.data?.active ? 'промпт #' + prompt.data.active.id : 'промпт по умолчанию'}<//>
      </div>

      <div class="chat-layout">
        <section class="card chat-card">
          <div ref=${transcript} class="transcript">
            ${messages.length === 0 ? html`<${Empty}>Напишите первое сообщение — сессия создастся сама.<//>` : messages.map((m) => html`
              <div key=${m.id} class=${'bubble ' + (m.role === 'user' ? 'user' : 'assistant')}>
                <div>${m.content}</div><span class="meta">${m.role === 'user' ? 'игрок' : 'незнакомец'}${m.model ? ' · ' + m.model : ''} · ${fmtTime(m.created_at)}</span>
              </div>`)}
            ${sending ? html`<div class="bubble assistant typing"><span></span><span></span><span></span></div>` : null}
          </div>
          <${ErrorText} error=${error}/>
          <div class="composer">
            <${Textarea} value=${event ? '' : text} onInput=${setText} rows=${2} disabled=${!!event}
              placeholder=${event ? 'Сталкер напишет первым — сообщение игрока не нужно' : 'Кто ты и откуда у тебя мой номер?'}/>
            <button type="button" class="btn primary" disabled=${(!text.trim() && !event) || sending} onClick=${send}
              title=${'Отправить'}>${event ? 'Событие' : 'Отправить'}</button>
          </div>
          <p class="muted small"><${Keys} combo=${bindingOf('send')} ru=${false}/> — отправить</p>
        </section>

        <section class="card">
          <h3>Контекст игры</h3>
          <${Field} label="Сталкер пишет первым" hint="Событие из игры вместо сообщения игрока">
            <${Select} value=${event} onChange=${setEvent} options=${EVENTS}/>
          <//>
          <${Field} label="Состояние ночи (JSON)" hint="Как присылает игра. Неизвестные поля и значения сервер отбрасывает"><${Textarea} value=${state} onInput=${setState} rows=${9} mono=${true} spellcheck="false"/><//>
          <${Field} label="Доп. контекст (JSON)" hint="Только в отладке: в игре его нет"><${Textarea} value=${extra} onInput=${setExtra} rows=${3} mono=${true} spellcheck="false"/><//>
          <${Field} label="Сообщений истории в запросе"><${Input} type="number" min="1" max="100" value=${historyLimit} onInput=${setHistoryLimit}/><//>
          ${overrides ? html`
            <${Check} label="Черновик системного промпта" checked=${useDraft} onChange=${setUseDraft}
              hint="Только для этого запроса: активная версия не меняется. Сохранить — на экране «Системный промпт»."/>
            ${useDraft ? html`<${Textarea} value=${draft} onInput=${setDraft} rows=${8} mono=${true} spellcheck="false"/>` : null}`
            : html`<p class="muted small">Подмена промпта выключена (ALLOW_CLIENT_PROVIDER_OVERRIDES=false или нет права llm.debug).</p>`}
        </section>
      </div>

      <section class="card">
        <div class="row-between"><h3>Что уходит модели</h3><${ActionButton} className="btn small" onClick=${runPreview}>Собрать запрос<//></div>
        ${last ? html`<div class="stats-grid four">
          <${Stat} label="входных токенов" value=${fmtNum(usage.prompt_tokens)}/>
          <${Stat} label="выходных токенов" value=${fmtNum(usage.completion_tokens)}/>
          <${Stat} label="задержка, мс" value=${fmtNum(last.latency_ms)}/>
          <${Stat} label="токенов/с" value=${last.performance?.tokens_per_second ? Number(last.performance.tokens_per_second).toFixed(1) : '—'}/>
        </div>` : null}
        ${preview ? html`
          <p class="muted small">История: ${preview.history_count ?? 0} из ${preview.history_limit ?? historyLimit} · промпт: ${preview.prompt_source || '—'}</p>
          <div class="messages-preview">${(preview.messages || []).map((m, i) => html`
            <div key=${i} class=${'msg-row role-' + m.role}><span class="msg-role">${m.role}</span><pre>${m.content}</pre></div>`)}</div>`
          : html`<p class="muted small">Нажмите «Собрать запрос» или отправьте сообщение.</p>`}
        ${last ? html`<${CodeBlock} title="Ответ backend целиком" value=${last}/>` : null}
      </section>
    </div>`;
}
