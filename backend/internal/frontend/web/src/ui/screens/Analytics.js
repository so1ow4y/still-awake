// Аналитика запросов к модели: доступ по роли (право analytics.read), без отдельного токена.

import { html } from '../html.js';
import { api } from '../api.js';
import { useLoad } from '../hooks.js';
import { setQuery, Link } from '../router.js';
import { fmtDate, fmtNum } from '../format.js';
import { usePageKeys } from '../pageKeys.js';
import { can } from '../session.js';
import { Choice, ErrorText, Loading, Empty } from '../components/ui.js';
import { ScreenHead, Stat } from './common.js';

const PERIODS = [['1', '24 часа'], ['7', '7 дней'], ['30', '30 дней'], ['0', 'всё время']];

export function AnalyticsScreen({ query = {} }) {
  const days = PERIODS.some(([v]) => v === query.days) ? query.days : '7';
  const data = useLoad(() => api('/v1/analytics/summary', { query: { days } }), [days]);
  usePageKeys({ refresh: () => data.reload() });
  const v = data.data;
  const maxDay = Math.max(1, ...(v?.daily || []).map((d) => d.requests));
  const toSessions = can('sessions.read');

  return html`
    <div class="screen">
      <${ScreenHead} title="Аналитика" desc="Запросы к модели: сколько, чьи, сколько токенов и как быстро."
        actions=${html`<${Choice} label="Период" value=${days} options=${PERIODS} onChange=${(d) => setQuery({ days: d })}/>`}/>
      <${ErrorText} error=${data.error}/>
      ${!v ? html`<${Loading}/>` : html`
        <div class="stats-grid five">
          <${Stat} label="запросов" value=${fmtNum(v.requests)}/>
          <${Stat} label="успешно" value=${fmtNum(v.success)} tone="good"/>
          <${Stat} label="ошибок" value=${fmtNum(v.errors)} tone=${v.errors ? 'bad' : ''}/>
          <${Stat} label="игроков (установок)" value=${fmtNum(v.active_installations)}/>
          <${Stat} label="сессий" value=${fmtNum(v.active_sessions)}/>
          <${Stat} label="входных токенов" value=${fmtNum(v.prompt_tokens)}/>
          <${Stat} label="выходных токенов" value=${fmtNum(v.completion_tokens)}/>
          <${Stat} label="средняя задержка, мс" value=${fmtNum(Math.round(v.avg_latency_ms || 0))}/>
          <${Stat} label="токенов/с в среднем" value=${Number(v.avg_tokens_per_second || 0).toFixed(1)}/>
          <${Stat} label="до первого токена, мс" value=${fmtNum(Math.round(v.avg_time_to_first_token_ms || 0))}/>
        </div>

        <div class="two-col">
          <section class="card">
            <h3>По дням</h3>
            ${v.daily?.length ? html`<div class="bars">${v.daily.map((d) => html`
              <div class="bar-row" key=${d.day}>
                <span class="bar-label">${d.day}</span>
                <span class="bar"><i style=${{ width: Math.max(2, (d.requests / maxDay) * 100) + '%' }}></i></span>
                <span class="bar-value">${fmtNum(d.requests)}${d.errors ? html` <span class="bad-text">· ${d.errors} ош.</span>` : ''}</span>
              </div>`)}</div>` : html`<${Empty}>Нет запросов за период.<//>`}
          </section>
          <section class="card">
            <h3>Модели</h3>
            ${v.models?.length ? html`<div class="table-wrap"><table class="tbl">
              <thead><tr><th>Модель</th><th>Запросов</th><th>Ошибок</th><th>Токенов</th><th>мс</th><th>ток/с</th></tr></thead>
              <tbody>${v.models.map((x) => html`<tr key=${x.model + x.provider}>
                <td><code>${x.model || '—'}</code><div class="muted small">${x.provider}</div></td><td>${fmtNum(x.requests)}</td>
                <td>${fmtNum(x.errors)}</td><td>${fmtNum(x.total_tokens)}</td><td>${fmtNum(Math.round(x.avg_latency_ms))}</td>
                <td>${Number(x.avg_tokens_per_second || 0).toFixed(1)}</td></tr>`)}</tbody>
            </table></div>` : html`<${Empty}>Нет данных.<//>`}
          </section>
        </div>

        <section class="card">
          <h3>Установки игры</h3>
          <p class="muted small">Кто чаще вызывает модель, сколько токенов тратит и сколько сессий создаёт.</p>
          ${v.users?.length ? html`<div class="table-wrap"><table class="tbl">
            <thead><tr><th>Установка</th><th>Запросов</th><th>Сессий</th><th>Ошибок</th><th>Токенов</th><th>мс</th><th>Последний</th></tr></thead>
            <tbody>${v.users.map((x) => html`<tr key=${x.installation_id}>
              <td>${toSessions ? html`<${Link} to=${'/sessions?q=' + encodeURIComponent(x.installation_id)}><code>${x.installation_id}</code><//>` : html`<code>${x.installation_id}</code>`}</td>
              <td>${fmtNum(x.requests)}</td><td>${fmtNum(x.sessions)}</td><td>${fmtNum(x.errors)}</td><td>${fmtNum(x.total_tokens)}</td>
              <td>${fmtNum(Math.round(x.avg_latency_ms))}</td><td>${fmtDate(x.last_request_at)}</td></tr>`)}</tbody>
          </table></div>` : html`<${Empty}>Нет данных.<//>`}
        </section>`}
    </div>`;
}
