// Игровые данные: сессии (ночи), установки игры и запросы к модели — обозреватель в духе SIEM (components/Explorer.js):
// строка «поле:значение», период, гистограмма, топ значений; клик по любому значению — в таблице или в карточке
// справа — добавляет его условием в строку поиска. Строка открывает карточку: состояние, сообщения, события
// и запросы к модели. Править и удалять — с правом sessions.manage.

import { html, useState, useEffect } from '../html.js';
import { api, pretty, parseJSON } from '../api.js';
import { useLoad } from '../hooks.js';
import { setQuery } from '../router.js';
import { can } from '../session.js';
import { fmtDate, fmtNum } from '../format.js';
import { usePageKeys } from '../pageKeys.js';
import { withTerm } from '../query.js';
import { Field, Input, Textarea, Choice, ActionButton, ErrorText, Loading, Empty, Badge, Dialog, CodeBlock } from '../components/ui.js';
import { SidePanel, PanelSection, Facts } from '../components/SidePanel.js';
import { Explorer, Value, localDate, weekday } from '../components/Explorer.js';
import { toast } from '../components/Toast.js';
import { ScreenHead } from './common.js';

const TABS = [['sessions', 'Сессии'], ['installations', 'Установки'], ['requests', 'Запросы к модели']];
/** Сколько последних записей грузить: фильтрует уже браузер. */
const LIMIT = 2000;
const REQUEST_LIMIT = 1000;

/** Итог ночи (result события night_end) по-русски — так он и ищется: «результат:полиция». */
const RESULTS = {
  police: 'полиция', killed_prison: 'тюрьма', killed_free: 'самооборона', caught: 'поймана', dawn: 'рассвет', abandoned: 'брошена',
};
const resultOf = (s) => (s.result ? RESULTS[s.result] || s.result : 'не завершена');
const RESULT_TONE = { полиция: 'ok', самооборона: 'ok', рассвет: 'ok', тюрьма: 'warn', поймана: 'danger' };
const state = (s) => s.state || {};

// ---------- Сессии ----------

const SESSION_FIELDS = [
  { name: 'сессия', aliases: ['id', 'session'], get: (s) => s.id },
  { name: 'установка', aliases: ['installation', 'inst'], get: (s) => s.installation_id },
  { name: 'сценарий', aliases: ['scenario', 'ночь'], get: (s) => s.scenario },
  { name: 'результат', aliases: ['result', 'итог'], get: resultOf, hint: Object.values(RESULTS).join(', ') + ', не завершена' },
  { name: 'версия', aliases: ['version'], get: (s) => s.game_version },
  { name: 'платформа', aliases: ['platform'], get: (s) => s.platform },
  { name: 'фаза', aliases: ['phase'], get: (s) => state(s).phase, hint: 'calm, strange, threat, danger' },
  { name: 'сталкер', aliases: ['stalker'], get: (s) => state(s).stalker, hint: 'away, street, window, door, room' },
  { name: 'укрытие', aliases: ['hidden'], get: (s) => state(s).hidden, hint: 'none, wardrobe, under_bed' },
  { name: 'время', aliases: ['clock'], get: (s) => state(s).clock, hint: 'время ночи, например 05:*' },
  { name: 'визиты', aliases: ['visits'], get: (s) => state(s).visits ?? 0, numeric: true },
  { name: 'сообщений', aliases: ['messages'], get: (s) => s.messages || 0, numeric: true },
  { name: 'событий', aliases: ['events'], get: (s) => s.events || 0, numeric: true },
  { name: 'запросов', aliases: ['requests'], get: (s) => s.requests || 0, numeric: true },
  { name: 'дата', aliases: ['date'], get: (s) => localDate(s.updated_at), hint: '2026-10-27 или 2026-10*' },
  { name: 'день', aliases: ['day'], get: (s) => weekday(s.updated_at) },
];
const sessionText = (s) => [s.id, s.installation_id, s.scenario, s.game_version, s.platform, resultOf(s)];
const sessionTime = (s) => s.updated_at;

export function SessionsScreen({ query = {} }) {
  const tab = TABS.some(([t]) => t === query.tab) ? query.tab : 'sessions';
  const q = query.q || '';
  const onQuery = (next) => setQuery({ q: next });
  usePageKeys({ search: () => { document.querySelector('[data-search-input]')?.focus(); return true; } });
  return html`
    <div class="screen wide-screen">
      <${ScreenHead} title="Игровые сессии" desc="Каждая ночь игрока — сессия: состояние, переписка, события и запросы к модели."
        actions=${html`<${Choice} label="Раздел" value=${tab} options=${TABS} onChange=${(t) => setQuery({ tab: t, q: '' })}/>`}/>
      ${tab === 'sessions' ? html`<${SessionList} key="s" q=${q} onQuery=${onQuery}/>`
        : tab === 'installations' ? html`<${InstallationList} key="i" q=${q} onQuery=${onQuery}/>`
        : html`<${RequestList} key="r" q=${q} onQuery=${onQuery}/>`}
    </div>`;
}

function SessionList({ q, onQuery }) {
  const list = useLoad(() => api('/v1/admin/sessions', { query: { limit: LIMIT } }), []);
  const [opened, setOpened] = useState(null);
  usePageKeys({ refresh: () => list.reload() });
  const addTerm = (field, value, negate) => onQuery(withTerm(q, field, value, negate));
  const columns = [
    { title: 'Обновлена', className: 'ex-time', render: (s) => fmtDate(s.updated_at) },
    { title: 'Сессия', render: (s, f) => html`<${Value} field="сессия" value=${s.id} onFilter=${f} mono=${true}/>` },
    { title: 'Установка', render: (s, f) => html`<${Value} field="установка" value=${s.installation_id} onFilter=${f} mono=${true}/>` },
    { title: 'Сценарий', render: (s, f) => html`<${Value} field="сценарий" value=${s.scenario} onFilter=${f}/>` },
    { title: 'Результат', render: (s, f) => html`<${Value} field="результат" value=${resultOf(s)} onFilter=${f}>
      <${Badge} tone=${RESULT_TONE[resultOf(s)]}>${resultOf(s)}<//><//>` },
    { title: 'Сообщ. / событ. / запр.', className: 'ex-nums', render: (s) => `${s.messages || 0} / ${s.events || 0} / ${s.requests || 0}` },
    { title: 'Версия', render: (s, f) => html`<${Value} field="версия" value=${s.game_version} onFilter=${f}/>` },
  ];
  return html`
    <${Explorer} rows=${list.data} loading=${list.loading} error=${list.error} fields=${SESSION_FIELDS} text=${sessionText} time=${sessionTime}
      facets=${['результат', 'сценарий', 'платформа', 'версия', 'установка', 'день']} columns=${columns}
      q=${q} onQuery=${onQuery} placeholder="результат:поймана сценарий:night-2 сообщений:>5 -платформа:WindowsEditor"
      histogramTitle="Сессии" opened=${opened} onOpen=${setOpened} onClose=${() => setOpened(null)}
      note=${list.data && list.data.length >= LIMIT ? `загружены последние ${LIMIT}` : null}/>
    ${opened ? html`<${SessionPanel} key=${opened} id=${opened} row=${(list.data || []).find((s) => s.id === opened)}
      onFilter=${addTerm} onClose=${() => setOpened(null)} onChanged=${() => list.reload()}/>` : null}`;
}

function SessionPanel({ id, row, onFilter, onClose, onChanged }) {
  const manage = can('sessions.manage');
  const data = useLoad(() => Promise.all([
    api(`/v1/admin/sessions/${encodeURIComponent(id)}`),
    api(`/v1/admin/sessions/${encodeURIComponent(id)}/messages`, { query: { limit: 200 } }),
    api(`/v1/admin/sessions/${encodeURIComponent(id)}/events`, { query: { limit: 500 } }),
    api(`/v1/admin/sessions/${encodeURIComponent(id)}/llm-requests`, { query: { limit: 100 } }),
  ]), [id]);
  const [form, setForm] = useState(null);
  const [del, setDel] = useState(false);
  useEffect(() => {
    if (data.data) {
      const s = data.data[0];
      setForm({ game_version: s.game_version, scenario: s.scenario, state: pretty(s.state || {}) });
    }
  }, [data.data]);

  // Пустые списки сервер раньше отдавал как null — `|| []`, чтобы карточка не падала ни на какой сессии.
  const [session, messagesRaw, eventsRaw, requestsRaw] = data.data || [];
  const messages = messagesRaw || [];
  const events = eventsRaw || [];
  const requests = requestsRaw || [];
  const save = async () => {
    await api(`/v1/admin/sessions/${encodeURIComponent(id)}`, { method: 'PATCH', body: { game_version: form.game_version, scenario: form.scenario, state: parseJSON(form.state) } });
    toast('Сессия сохранена');
    onChanged();
  };
  const v = (field, value, mono = false) => html`<${Value} field=${field} value=${value} onFilter=${onFilter} mono=${mono}/>`;
  return html`
    <${SidePanel} title="Сессия" subtitle=${html`<code>${id}</code>`} onClose=${onClose}
      actions=${manage ? html`<button type="button" class="btn small danger-outline" onClick=${() => setDel(true)}>Удалить</button>` : null}>
      <${ErrorText} error=${data.error}/>
      ${!session || !form ? html`<${Loading}/>` : html`
        <p class="muted small">Клик по значению — добавить его в фильтр списка.</p>
        <${Facts} rows=${[
          ['Установка', v('установка', session.installation_id, true)],
          ['Сценарий', v('сценарий', session.scenario)],
          ['Версия', v('версия', session.game_version)],
          row && ['Результат', v('результат', resultOf(row))],
          row?.platform && ['Платформа', v('платформа', row.platform)],
          ['Создана', fmtDate(session.created_at)], ['Обновлена', fmtDate(session.updated_at)],
          ['Сообщений', fmtNum(messages.length)], ['Событий', fmtNum(events.length)], ['Запросов к модели', fmtNum(requests.length)],
        ]}/>
        <${PanelSection} title="Состояние">
          <div class="form-grid">
            <${Field} label="Версия игры"><${Input} value=${form.game_version} onInput=${(x) => setForm({ ...form, game_version: x })} disabled=${!manage}/><//>
            <${Field} label="Сценарий"><${Input} value=${form.scenario} onInput=${(x) => setForm({ ...form, scenario: x })} disabled=${!manage}/><//>
          </div>
          <${Field} label="GAME_STATE (JSON)"><${Textarea} value=${form.state} onInput=${(x) => setForm({ ...form, state: x })} rows=${7} mono=${true} disabled=${!manage}/><//>
          ${manage ? html`<div class="btn-row"><${ActionButton} className="btn primary" onClick=${save}>Сохранить<//></div>` : null}
        <//>
        <${PanelSection} title=${'Переписка (' + messages.length + ')'}>
          ${messages.length ? html`<div class="transcript compact">${messages.map((m) => html`
            <div key=${m.id} class=${'bubble ' + (m.role === 'user' ? 'user' : 'assistant')}><div>${m.content}</div>
              <span class="meta">#${m.id} · ${m.role}${m.model ? ' · ' + m.model : ''} · ${fmtDate(m.created_at)}</span></div>`)}</div>` : html`<${Empty}>Сообщений нет.<//>`}
        <//>
        <${PanelSection} title=${'События (' + events.length + ')'}>
          ${events.length ? html`<div class="table-wrap"><table class="tbl">
            <thead><tr><th>#</th><th>Тип</th><th>Данные</th><th>Когда</th></tr></thead>
            <tbody>${events.map((e) => html`<tr key=${e.id}><td>${e.client_seq || e.id}</td><td><${Badge}>${e.type}<//></td>
              <td><code class="wrap">${JSON.stringify(e.payload)}</code></td><td>${fmtDate(e.created_at)}</td></tr>`)}</tbody>
          </table></div>` : html`<${Empty}>Событий нет.<//>`}
        <//>
        <${PanelSection} title=${'Запросы к модели (' + requests.length + ')'}>
          ${requests.length ? requests.map((r) => html`<${RequestItem} key=${r.id} r=${r} onDeleted=${() => data.reload()}/>`) : html`<${Empty}>Запросов нет.<//>`}
        <//>`}
      ${del ? html`<${Dialog} title="Удалить сессию?" danger=${true} confirmLabel="Удалить" onClose=${() => setDel(false)}
        onConfirm=${async () => { await api(`/v1/admin/sessions/${encodeURIComponent(id)}`, { method: 'DELETE' }); toast('Сессия удалена'); onClose(); onChanged(); }}>
        <p>Вместе с сессией удалятся её сообщения, события и запросы к модели.</p><//>` : null}
    <//>`;
}

function RequestItem({ r, onDeleted, open = false }) {
  const manage = can('sessions.manage');
  return html`
    <details class="request" open=${open}>
      <summary>
        <${Badge} tone=${r.status === 'ok' ? 'ok' : 'danger'}>${r.status}<//>
        <span><b>#${r.id}</b> ${r.model || r.provider}</span>
        <span class="muted small">${r.prompt_tokens}→${r.completion_tokens} ток · ${fmtNum(r.latency_ms)} мс · ${fmtDate(r.created_at)}</span>
      </summary>
      <div class="request-body">
        <div class="request-pair">
          <div><span class="muted small">Игрок</span><pre>${r.user_message || '—'}</pre></div>
          <div><span class="muted small">Модель</span><pre>${r.assistant_message || r.error_text || '—'}</pre></div>
        </div>
        <p class="muted small"><code>${r.installation_id}</code> · <code>${r.session_id}</code> · история ${r.history_count} · ${r.endpoint || ''}</p>
        <${CodeBlock} title="Фактический messages[], отправленный модели" value=${r.request_messages || []}/>
        ${manage ? html`<div class="btn-row"><${ActionButton} className="btn small danger-outline"
          onClick=${async () => { await api(`/v1/admin/llm-requests/${r.id}`, { method: 'DELETE' }); toast('Запрос удалён'); onDeleted(); }}>Удалить запись<//></div>` : null}
      </div>
    </details>`;
}

// ---------- Установки ----------

const meta = (x) => x.metadata || {};
const INSTALLATION_FIELDS = [
  { name: 'установка', aliases: ['installation', 'inst', 'id'], get: (x) => x.id },
  { name: 'платформа', aliases: ['platform'], get: (x) => meta(x).platform },
  { name: 'источник', aliases: ['source'], get: (x) => meta(x).source },
  { name: 'версия', aliases: ['version'], get: (x) => meta(x).game_version },
  { name: 'сессий', aliases: ['sessions'], get: (x) => x.sessions || 0, numeric: true },
  { name: 'дата', aliases: ['date'], get: (x) => localDate(x.created_at), hint: 'дата создания: 2026-10-27 или 2026-10*' },
  { name: 'последняя', aliases: ['last'], get: (x) => (x.last_session_at ? localDate(x.last_session_at) : ''), hint: 'дата последней ночи' },
  { name: 'день', aliases: ['day'], get: (x) => weekday(x.created_at) },
];
const installationText = (x) => [x.id, meta(x).platform, meta(x).source, meta(x).game_version];
const installationTime = (x) => x.created_at;

function InstallationList({ q, onQuery }) {
  const list = useLoad(() => api('/v1/admin/installations', { query: { limit: LIMIT } }), []);
  const [opened, setOpened] = useState(null);
  usePageKeys({ refresh: () => list.reload() });
  const addTerm = (field, value, negate) => onQuery(withTerm(q, field, value, negate));
  const row = (list.data || []).find((x) => x.id === opened);
  const columns = [
    { title: 'Создана', className: 'ex-time', render: (x) => fmtDate(x.created_at) },
    { title: 'Установка', render: (x, f) => html`<${Value} field="установка" value=${x.id} onFilter=${f} mono=${true}/>` },
    { title: 'Платформа', render: (x, f) => html`<${Value} field="платформа" value=${meta(x).platform} onFilter=${f}/>` },
    { title: 'Источник', render: (x, f) => html`<${Value} field="источник" value=${meta(x).source} onFilter=${f}/>` },
    { title: 'Версия', render: (x, f) => html`<${Value} field="версия" value=${meta(x).game_version} onFilter=${f}/>` },
    { title: 'Ночей', className: 'ex-nums', render: (x) => html`<button type="button" class="ex-value" title="Открыть сессии этой установки"
      onClick=${(e) => { e.stopPropagation(); setQuery({ tab: 'sessions', q: `установка:${x.id}` }); }}>${x.sessions || 0}</button>` },
    { title: 'Последняя ночь', className: 'ex-time', render: (x) => (x.last_session_at ? fmtDate(x.last_session_at) : '—') },
  ];
  return html`
    <${Explorer} rows=${list.data} loading=${list.loading} error=${list.error} fields=${INSTALLATION_FIELDS} text=${installationText}
      time=${installationTime} facets=${['платформа', 'источник', 'версия', 'последняя']} columns=${columns}
      q=${q} onQuery=${onQuery} placeholder="платформа:WindowsPlayer сессий:>3 последняя:2026-10*" histogramTitle="Новые установки"
      opened=${opened} onOpen=${setOpened} onClose=${() => setOpened(null)}
      note=${list.data && list.data.length >= LIMIT ? `загружены последние ${LIMIT}` : null}/>
    ${row ? html`<${InstallationPanel} key=${row.id} row=${row} onFilter=${addTerm} onClose=${() => setOpened(null)} onChanged=${() => list.reload()}/>` : null}`;
}

function InstallationPanel({ row, onFilter, onClose, onChanged }) {
  const manage = can('sessions.manage');
  const [metaText, setMeta] = useState(pretty(row.metadata || {}));
  const [del, setDel] = useState(false);
  useEffect(() => setMeta(pretty(row.metadata || {})), [row.id]);
  const v = (field, value) => html`<${Value} field=${field} value=${value} onFilter=${onFilter}/>`;
  return html`
    <${SidePanel} title="Установка игры" subtitle=${html`<code>${row.id}</code>`} onClose=${onClose}
      actions=${manage ? html`<button type="button" class="btn small danger-outline" onClick=${() => setDel(true)}>Удалить</button>` : null}>
      <${Facts} rows=${[
        ['Платформа', v('платформа', meta(row).platform)], ['Источник', v('источник', meta(row).source)], ['Версия', v('версия', meta(row).game_version)],
        ['Создана', fmtDate(row.created_at)], ['Ночей', fmtNum(row.sessions || 0)], ['Последняя ночь', row.last_session_at ? fmtDate(row.last_session_at) : '—'],
      ]}/>
      <${PanelSection} title="Сессии установки">
        <div class="btn-row">
          <button type="button" class="btn" onClick=${() => setQuery({ tab: 'sessions', q: `установка:${row.id}` })}>Сессии этой установки</button>
          <button type="button" class="btn" onClick=${() => setQuery({ tab: 'requests', q: `установка:${row.id}` })}>Запросы к модели</button>
          <a class="btn" href=${'#/users?q=' + encodeURIComponent(`установка:${row.id}`)}>Игрок</a>
        </div>
      <//>
      <${PanelSection} title="Метаданные">
        <${Textarea} value=${metaText} onInput=${setMeta} rows=${6} mono=${true} disabled=${!manage}/>
        ${manage ? html`<div class="btn-row"><${ActionButton} className="btn primary" onClick=${async () => {
          await api(`/v1/admin/installations/${encodeURIComponent(row.id)}`, { method: 'PATCH', body: { metadata: parseJSON(metaText) } });
          toast('Сохранено');
          onChanged();
        }}>Сохранить<//></div>` : null}
      <//>
      ${del ? html`<${Dialog} title="Удалить установку?" danger=${true} confirmLabel="Удалить" onClose=${() => setDel(false)}
        onConfirm=${async () => { await api(`/v1/admin/installations/${encodeURIComponent(row.id)}`, { method: 'DELETE' }); toast('Удалено'); onClose(); onChanged(); }}>
        <p>Удалятся все сессии установки, их сообщения, события и запросы к модели.</p><//>` : null}
    <//>`;
}

// ---------- Запросы к модели ----------

const REQUEST_FIELDS = [
  { name: 'запрос', aliases: ['id'], get: (r) => r.id, numeric: true },
  { name: 'статус', aliases: ['status'], get: (r) => r.status, hint: 'ok, error' },
  { name: 'модель', aliases: ['model'], get: (r) => r.model },
  { name: 'провайдер', aliases: ['provider'], get: (r) => r.provider },
  { name: 'режим', aliases: ['mode'], get: (r) => r.request_mode },
  { name: 'сессия', aliases: ['session'], get: (r) => r.session_id },
  { name: 'установка', aliases: ['installation', 'inst'], get: (r) => r.installation_id },
  { name: 'задержка', aliases: ['latency', 'мс'], get: (r) => r.latency_ms, numeric: true, hint: 'миллисекунды: задержка:>10000' },
  { name: 'токены', aliases: ['tokens'], get: (r) => r.total_tokens, numeric: true },
  { name: 'история', aliases: ['history'], get: (r) => r.history_count, numeric: true },
  { name: 'игрок', aliases: ['user'], get: (r) => r.user_message, hint: 'текст игрока, *слово*' },
  { name: 'ответ', aliases: ['reply'], get: (r) => r.assistant_message || r.error_text, hint: 'ответ модели, *слово*' },
  { name: 'дата', aliases: ['date'], get: (r) => localDate(r.created_at) },
  { name: 'день', aliases: ['day'], get: (r) => weekday(r.created_at) },
];
const requestText = (r) => [r.model, r.session_id, r.installation_id, r.user_message, r.assistant_message, r.error_text];
const requestTime = (r) => r.created_at;
const short = (s, n = 70) => (s && s.length > n ? s.slice(0, n) + '…' : s || '—');

function RequestList({ q, onQuery }) {
  const list = useLoad(() => api('/v1/admin/llm-requests', { query: { limit: REQUEST_LIMIT } }), []);
  const [opened, setOpened] = useState(null);
  usePageKeys({ refresh: () => list.reload() });
  const addTerm = (field, value, negate) => onQuery(withTerm(q, field, value, negate));
  const row = (list.data || []).find((r) => r.id === opened);
  const columns = [
    { title: 'Когда', className: 'ex-time', render: (r) => fmtDate(r.created_at) },
    { title: 'Статус', render: (r, f) => html`<${Value} field="статус" value=${r.status} onFilter=${f}><${Badge} tone=${r.status === 'ok' ? 'ok' : 'danger'}>${r.status}<//><//>` },
    { title: 'Модель', render: (r, f) => html`<${Value} field="модель" value=${r.model || r.provider} onFilter=${f}/>` },
    { title: 'Сессия', render: (r, f) => html`<${Value} field="сессия" value=${r.session_id} onFilter=${f} mono=${true}/>` },
    { title: 'Задержка', className: 'ex-nums', render: (r) => fmtNum(r.latency_ms) + ' мс' },
    { title: 'Ответ', render: (r) => html`<span class="ex-text">${short(r.assistant_message || r.error_text)}</span>` },
  ];
  return html`
    <${Explorer} rows=${list.data} loading=${list.loading} error=${list.error} fields=${REQUEST_FIELDS} text=${requestText}
      time=${requestTime} facets=${['статус', 'модель', 'режим', 'сессия', 'день']} columns=${columns}
      q=${q} onQuery=${onQuery} placeholder="статус:error задержка:>20000 модель:qwen* ответ:*окно*" histogramTitle="Запросы"
      opened=${opened} onOpen=${setOpened} onClose=${() => setOpened(null)}
      note=${list.data && list.data.length >= REQUEST_LIMIT ? `загружены последние ${REQUEST_LIMIT}` : null}/>
    ${row ? html`<${RequestPanel} key=${row.id} r=${row} onFilter=${addTerm} onClose=${() => setOpened(null)}
      onDeleted=${() => { setOpened(null); list.reload(); }}/>` : null}`;
}

function RequestPanel({ r, onFilter, onClose, onDeleted }) {
  const v = (field, value, mono = false) => html`<${Value} field=${field} value=${value} onFilter=${onFilter} mono=${mono}/>`;
  return html`
    <${SidePanel} title=${'Запрос #' + r.id} subtitle=${fmtDate(r.created_at)} onClose=${onClose}>
      <${Facts} rows=${[
        ['Статус', v('статус', r.status)], ['Модель', v('модель', r.model)], ['Провайдер', v('провайдер', r.provider)], ['Режим', v('режим', r.request_mode)],
        ['Сессия', v('сессия', r.session_id, true)], ['Установка', v('установка', r.installation_id, true)],
        ['Токены', `${r.prompt_tokens} → ${r.completion_tokens}`], ['Задержка', fmtNum(r.latency_ms) + ' мс'],
      ]}/>
      <div class="btn-row"><button type="button" class="btn" onClick=${() => setQuery({ tab: 'sessions', q: `сессия:${r.session_id}` })}>Открыть сессию в списке</button></div>
      <${RequestItem} r=${r} open=${true} onDeleted=${onDeleted}/>
    <//>`;
}
