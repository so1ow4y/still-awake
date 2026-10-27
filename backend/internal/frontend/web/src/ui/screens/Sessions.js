// Игровые данные: сессии (ночи), установки игры и запросы к модели. Строка открывает карточку справа:
// состояние, сообщения, события и запросы к модели этой сессии. Править и удалять — с правом sessions.manage.

import { html, useState, useEffect } from '../html.js';
import { api, pretty, parseJSON } from '../api.js';
import { useLoad } from '../hooks.js';
import { setQuery } from '../router.js';
import { can } from '../session.js';
import { fmtDate, fmtNum } from '../format.js';
import { usePageKeys, useListKeys } from '../pageKeys.js';
import { Field, Input, Textarea, Choice, ActionButton, ErrorText, Loading, Empty, Badge, SearchBox, Dialog, CodeBlock } from '../components/ui.js';
import { SidePanel, PanelSection, Facts } from '../components/SidePanel.js';
import { toast } from '../components/Toast.js';
import { ScreenHead } from './common.js';

const TABS = [['sessions', 'Сессии'], ['installations', 'Установки'], ['requests', 'Запросы к модели']];

export function SessionsScreen({ query = {} }) {
  const tab = TABS.some(([t]) => t === query.tab) ? query.tab : 'sessions';
  const [search, setSearch] = useState(query.q || '');
  const q = query.q || '';
  usePageKeys({ search: () => { document.querySelector('[data-search-input]')?.focus(); return true; } });
  return html`
    <div class="screen">
      <${ScreenHead} title="Игровые сессии" desc="Каждая ночь игрока — сессия: состояние, переписка, события и запросы к модели."
        actions=${html`<${Choice} label="Раздел" value=${tab} options=${TABS} onChange=${(t) => setQuery({ tab: t })}/>`}/>
      <div class="toolbar">
        <${SearchBox} value=${search} onInput=${setSearch} placeholder="inst_…, sess_…, сценарий, версия" onEnter=${() => setQuery({ q: search.trim() })}/>
        <button type="button" class="btn" onClick=${() => setQuery({ q: search.trim() })}>Найти</button>
        ${q ? html`<button type="button" class="btn" onClick=${() => { setSearch(''); setQuery({ q: '' }); }}>Сбросить</button>` : null}
      </div>
      ${tab === 'sessions' ? html`<${SessionList} q=${q}/>` : tab === 'installations' ? html`<${InstallationList} q=${q}/>` : html`<${RequestList} q=${q}/>`}
    </div>`;
}

function SessionList({ q }) {
  const list = useLoad(() => api('/v1/admin/sessions', { query: { q, limit: 300 } }), [q]);
  const [opened, setOpened] = useState(null);
  const rows = list.data || [];
  const cursor = useListKeys({ ids: rows.map((s) => s.id), openedId: opened, onOpen: setOpened, onClose: () => setOpened(null) });
  usePageKeys({ refresh: () => list.reload() });
  return html`
    <section class="card">
      <${ErrorText} error=${list.error}/>
      ${list.loading && !list.data ? html`<${Loading}/>` : rows.length === 0 ? html`<${Empty}>Сессий нет.<//>` : html`
        <div class="table-wrap"><table class="tbl rows">
          <thead><tr><th>Сессия</th><th>Установка</th><th>Сценарий</th><th>Версия</th><th>Обновлена</th></tr></thead>
          <tbody>${rows.map((s) => html`
            <tr key=${s.id} data-row-id=${s.id} onClick=${() => setOpened(opened === s.id ? null : s.id)}
              class=${(opened === s.id ? 'active ' : '') + (cursor === s.id ? 'cursor' : '')}>
              <td><code>${s.id}</code></td><td><code>${s.installation_id}</code></td><td>${s.scenario}</td><td>${s.game_version || '—'}</td>
              <td>${fmtDate(s.updated_at)}</td>
            </tr>`)}</tbody>
        </table></div>`}
      ${opened ? html`<${SessionPanel} id=${opened} onClose=${() => setOpened(null)} onChanged=${() => list.reload()}/>` : null}
    </section>`;
}

function SessionPanel({ id, onClose, onChanged }) {
  const manage = can('sessions.manage');
  const data = useLoad(() => Promise.all([
    api(`/v1/admin/sessions/${encodeURIComponent(id)}`),
    api(`/v1/admin/sessions/${encodeURIComponent(id)}/messages`, { query: { limit: 200 } }),
    api(`/v1/admin/sessions/${encodeURIComponent(id)}/events`, { query: { limit: 300 } }),
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

  const [session, messages, events, requests] = data.data || [];
  const save = async () => {
    await api(`/v1/admin/sessions/${encodeURIComponent(id)}`, { method: 'PATCH', body: { game_version: form.game_version, scenario: form.scenario, state: parseJSON(form.state) } });
    toast('Сессия сохранена');
    onChanged();
  };
  return html`
    <${SidePanel} title="Сессия" subtitle=${html`<code>${id}</code>`} onClose=${onClose}
      actions=${manage ? html`<button type="button" class="btn small danger-outline" onClick=${() => setDel(true)}>Удалить</button>` : null}>
      <${ErrorText} error=${data.error}/>
      ${!session || !form ? html`<${Loading}/>` : html`
        <${Facts} rows=${[['Установка', html`<code>${session.installation_id}</code>`], ['Создана', fmtDate(session.created_at)], ['Обновлена', fmtDate(session.updated_at)],
          ['Сообщений', fmtNum(messages.length)], ['Событий', fmtNum(events.length)], ['Запросов к модели', fmtNum(requests.length)]]}/>
        <${PanelSection} title="Состояние">
          <div class="form-grid">
            <${Field} label="Версия игры"><${Input} value=${form.game_version} onInput=${(v) => setForm({ ...form, game_version: v })} disabled=${!manage}/><//>
            <${Field} label="Сценарий"><${Input} value=${form.scenario} onInput=${(v) => setForm({ ...form, scenario: v })} disabled=${!manage}/><//>
          </div>
          <${Field} label="GAME_STATE (JSON)"><${Textarea} value=${form.state} onInput=${(v) => setForm({ ...form, state: v })} rows=${7} mono=${true} disabled=${!manage}/><//>
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

function RequestItem({ r, onDeleted }) {
  const manage = can('sessions.manage');
  return html`
    <details class="request">
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

function InstallationList({ q }) {
  const list = useLoad(() => api('/v1/admin/installations', { query: { q, limit: 300 } }), [q]);
  const [opened, setOpened] = useState(null);
  const rows = list.data || [];
  const cursor = useListKeys({ ids: rows.map((s) => s.id), openedId: opened, onOpen: setOpened, onClose: () => setOpened(null) });
  usePageKeys({ refresh: () => list.reload() });
  const row = rows.find((x) => x.id === opened);
  return html`
    <section class="card">
      <${ErrorText} error=${list.error}/>
      ${list.loading && !list.data ? html`<${Loading}/>` : rows.length === 0 ? html`<${Empty}>Установок нет.<//>` : html`
        <div class="table-wrap"><table class="tbl rows">
          <thead><tr><th>Установка</th><th>Откуда</th><th>Создана</th></tr></thead>
          <tbody>${rows.map((x) => html`
            <tr key=${x.id} data-row-id=${x.id} onClick=${() => setOpened(opened === x.id ? null : x.id)}
              class=${(opened === x.id ? 'active ' : '') + (cursor === x.id ? 'cursor' : '')}>
              <td><code>${x.id}</code></td><td>${x.metadata?.source || '—'} ${x.metadata?.platform ? '· ' + x.metadata.platform : ''}</td><td>${fmtDate(x.created_at)}</td>
            </tr>`)}</tbody>
        </table></div>`}
      ${row ? html`<${InstallationPanel} row=${row} onClose=${() => setOpened(null)} onChanged=${() => list.reload()}/>` : null}
    </section>`;
}

function InstallationPanel({ row, onClose, onChanged }) {
  const manage = can('sessions.manage');
  const [meta, setMeta] = useState(pretty(row.metadata || {}));
  const [del, setDel] = useState(false);
  useEffect(() => setMeta(pretty(row.metadata || {})), [row.id]);
  return html`
    <${SidePanel} title="Установка игры" subtitle=${html`<code>${row.id}</code>`} onClose=${onClose}
      actions=${manage ? html`<button type="button" class="btn small danger-outline" onClick=${() => setDel(true)}>Удалить</button>` : null}>
      <${Facts} rows=${[['Создана', fmtDate(row.created_at)]]}/>
      <${PanelSection} title="Метаданные">
        <${Textarea} value=${meta} onInput=${setMeta} rows=${6} mono=${true} disabled=${!manage}/>
        ${manage ? html`<div class="btn-row"><${ActionButton} className="btn primary" onClick=${async () => {
          await api(`/v1/admin/installations/${encodeURIComponent(row.id)}`, { method: 'PATCH', body: { metadata: parseJSON(meta) } });
          toast('Сохранено');
          onChanged();
        }}>Сохранить<//></div>` : null}
      <//>
      <${PanelSection} title="Сессии установки">
        <button type="button" class="btn" onClick=${() => setQuery({ tab: 'sessions', q: row.id })}>Показать сессии этой установки</button>
      <//>
      ${del ? html`<${Dialog} title="Удалить установку?" danger=${true} confirmLabel="Удалить" onClose=${() => setDel(false)}
        onConfirm=${async () => { await api(`/v1/admin/installations/${encodeURIComponent(row.id)}`, { method: 'DELETE' }); toast('Удалено'); onClose(); onChanged(); }}>
        <p>Удалятся все сессии установки, их сообщения, события и запросы к модели.</p><//>` : null}
    <//>`;
}

function RequestList({ q }) {
  const list = useLoad(() => api('/v1/admin/llm-requests', { query: { q, limit: 200 } }), [q]);
  usePageKeys({ refresh: () => list.reload() });
  const rows = list.data || [];
  return html`
    <section class="card">
      <${ErrorText} error=${list.error}/>
      ${list.loading && !list.data ? html`<${Loading}/>` : rows.length === 0 ? html`<${Empty}>Запросов нет.<//>`
        : rows.map((r) => html`<${RequestItem} key=${r.id} r=${r} onDeleted=${() => list.reload()}/>`)}
    </section>`;
}
