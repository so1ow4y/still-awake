// «Мои ночи» — панель глазами игрока: только его сессии (ночи) с перепиской и событиями, тот же обозреватель
// «поле:значение», что у сотрудников (components/Explorer.js). Запросов к модели здесь нет — в них системный
// промпт. Логин и пароль игрок получил в игре; сменить пароль — «Настройки → Профиль» или в самой игре.

import { html, useState } from '../html.js';
import { api } from '../api.js';
import { useLoad } from '../hooks.js';
import { setQuery } from '../router.js';
import { me } from '../session.js';
import { fmtDate, fmtNum } from '../format.js';
import { usePageKeys } from '../pageKeys.js';
import { withTerm } from '../query.js';
import { ErrorText, Loading, Empty, Badge } from '../components/ui.js';
import { SidePanel, PanelSection, Facts } from '../components/SidePanel.js';
import { Explorer, Value, localDate, weekday } from '../components/Explorer.js';
import { ScreenHead } from './common.js';

const RESULTS = {
  police: 'полиция', killed_prison: 'тюрьма', killed_free: 'самооборона', caught: 'поймана', dawn: 'рассвет', abandoned: 'брошена',
};
const resultOf = (s) => (s.result ? RESULTS[s.result] || s.result : 'не завершена');
const RESULT_TONE = { полиция: 'ok', самооборона: 'ok', рассвет: 'ok', тюрьма: 'warn', поймана: 'danger' };
const state = (s) => s.state || {};

const FIELDS = [
  { name: 'ночь', aliases: ['scenario', 'сценарий'], get: (s) => s.scenario },
  { name: 'результат', aliases: ['result', 'итог'], get: resultOf, hint: Object.values(RESULTS).join(', ') + ', не завершена' },
  { name: 'версия', aliases: ['version'], get: (s) => s.game_version },
  { name: 'время', aliases: ['clock'], get: (s) => state(s).clock, hint: 'на чём остановилась ночь, например 05:*' },
  { name: 'визиты', aliases: ['visits'], get: (s) => state(s).visits ?? 0, numeric: true },
  { name: 'сообщений', aliases: ['messages'], get: (s) => s.messages || 0, numeric: true },
  { name: 'событий', aliases: ['events'], get: (s) => s.events || 0, numeric: true },
  { name: 'дата', aliases: ['date'], get: (s) => localDate(s.updated_at), hint: '2026-10-27 или 2026-10*' },
  { name: 'день', aliases: ['day'], get: (s) => weekday(s.updated_at) },
  { name: 'сессия', aliases: ['id'], get: (s) => s.id },
];
const text = (s) => [s.id, s.scenario, s.game_version, resultOf(s)];
const time = (s) => s.updated_at;

export function MyNightsScreen({ query = {} }) {
  const q = query.q || '';
  const list = useLoad(() => api('/v1/me/sessions'), []);
  const [opened, setOpened] = useState(null);
  const onQuery = (next) => setQuery({ q: next });
  const addTerm = (field, value, negate) => onQuery(withTerm(q, field, value, negate));
  usePageKeys({ refresh: () => list.reload(), search: () => { document.querySelector('[data-search-input]')?.focus(); return true; } });
  const user = me()?.user;
  const rows = list.data || null;
  const columns = [
    { title: 'Когда', className: 'ex-time', render: (s) => fmtDate(s.updated_at) },
    { title: 'Ночь', render: (s, f) => html`<${Value} field="ночь" value=${s.scenario} onFilter=${f}/>` },
    { title: 'Результат', render: (s, f) => html`<${Value} field="результат" value=${resultOf(s)} onFilter=${f}>
      <${Badge} tone=${RESULT_TONE[resultOf(s)]}>${resultOf(s)}<//><//>` },
    { title: 'Остановилась на', className: 'ex-nums', render: (s) => state(s).clock || '—' },
    { title: 'Сообщ. / событ.', className: 'ex-nums', render: (s) => `${s.messages || 0} / ${s.events || 0}` },
  ];
  return html`
    <div class="screen wide-screen">
      <${ScreenHead} title="Мои ночи" desc=${html`Всё, что вы сыграли${user ? html` под логином <b>${user.username}</b>` : ''}: переписка с ним,
        события ночи и чем она закончилась. Сменить пароль — <a href="#/settings">Настройки → Профиль</a> или в самой игре.`}/>
      <${Explorer} rows=${rows} loading=${list.loading} error=${list.error} fields=${FIELDS} text=${text} time=${time}
        facets=${['результат', 'ночь', 'день']} columns=${columns} q=${q} onQuery=${onQuery}
        placeholder="результат:поймана ночь:night-2 сообщений:>5" histogramTitle="Ночи"
        emptyText=${rows && rows.length === 0 && !q ? 'Вы ещё не играли с этого аккаунта.' : 'Ничего не найдено.'}
        opened=${opened} onOpen=${setOpened} onClose=${() => setOpened(null)}/>
      ${opened ? html`<${NightPanel} key=${opened} id=${opened} row=${(rows || []).find((s) => s.id === opened)}
        onFilter=${addTerm} onClose=${() => setOpened(null)}/>` : null}
    </div>`;
}

function NightPanel({ id, row, onFilter, onClose }) {
  const data = useLoad(() => Promise.all([
    api(`/v1/me/sessions/${encodeURIComponent(id)}/messages`),
    api(`/v1/me/sessions/${encodeURIComponent(id)}/events`),
  ]), [id]);
  const [messagesRaw, eventsRaw] = data.data || [];
  const messages = messagesRaw || [];
  const events = eventsRaw || [];
  const v = (field, value) => html`<${Value} field=${field} value=${value} onFilter=${onFilter}/>`;
  return html`
    <${SidePanel} title="Ночь" subtitle=${row ? fmtDate(row.updated_at) : html`<code>${id}</code>`} onClose=${onClose}>
      <${ErrorText} error=${data.error}/>
      ${row ? html`<${Facts} rows=${[
        ['Ночь', v('ночь', row.scenario)], ['Результат', v('результат', resultOf(row))], ['Версия игры', v('версия', row.game_version)],
        ['Началась', fmtDate(row.created_at)], ['Остановилась на', state(row).clock || '—'], ['Визитов', fmtNum(state(row).visits || 0)],
      ]}/>` : null}
      ${!data.data ? html`<${Loading}/>` : html`
        <${PanelSection} title=${'Переписка (' + messages.length + ')'}>
          ${messages.length ? html`<div class="transcript compact">${messages.map((m) => html`
            <div key=${m.id} class=${'bubble ' + (m.role === 'user' ? 'user' : 'assistant')}><div>${m.content}</div>
              <span class="meta">${m.role === 'user' ? 'вы' : 'Неизвестный'} · ${fmtDate(m.created_at)}</span></div>`)}</div>` : html`<${Empty}>Переписки не было.<//>`}
        <//>
        <${PanelSection} title=${'События (' + events.length + ')'}>
          ${events.length ? html`<div class="table-wrap"><table class="tbl">
            <thead><tr><th>Тип</th><th>Данные</th><th>Когда</th></tr></thead>
            <tbody>${events.map((e) => html`<tr key=${e.id}><td><${Badge}>${e.type}<//></td>
              <td><code class="wrap">${JSON.stringify(e.payload)}</code></td><td>${fmtDate(e.created_at)}</td></tr>`)}</tbody>
          </table></div>` : html`<${Empty}>Событий нет.<//>`}
        <//>`}
    <//>`;
}
