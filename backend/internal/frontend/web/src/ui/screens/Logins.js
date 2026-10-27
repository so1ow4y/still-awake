// Журнал входов: удачные и неудачные попытки входа в панель и регистрации из игры (право logins.read).

import { html, useState } from '../html.js';
import { api } from '../api.js';
import { useLoad } from '../hooks.js';
import { setQuery } from '../router.js';
import { fmtDate, fmtAgent } from '../format.js';
import { usePageKeys } from '../pageKeys.js';
import { Choice, ErrorText, Loading, Empty, Badge, SearchBox } from '../components/ui.js';
import { ScreenHead } from './common.js';

export const LOGIN_REASONS = {
  invalid_password: 'неверный пароль', user_not_found: 'нет такого логина', blocked: 'аккаунт заблокирован',
  rate_limited: 'слишком много попыток', registered: 'регистрация',
};
const METHODS = { password: 'панель', phone: 'игра (телефон)' };

export function LoginRows({ rows, showUser = true }) {
  if (!rows.length) return html`<${Empty}>Записей нет.<//>`;
  return html`
    <div class="table-wrap"><table class="tbl">
      <thead><tr><th>Когда</th>${showUser ? html`<th>Кто</th>` : null}<th>Итог</th><th>Способ</th><th>Адрес</th><th>Устройство</th></tr></thead>
      <tbody>${rows.map((e) => html`<tr key=${e.id}>
        <td>${fmtDate(e.created_at)}</td>
        ${showUser ? html`<td>${e.username || html`<span class="muted">${e.identifier || '—'}</span>`}</td>` : null}
        <td>${e.success ? html`<${Badge} tone="ok">${e.reason === 'registered' ? 'регистрация' : 'вход'}<//>` : html`<${Badge} tone="danger">${LOGIN_REASONS[e.reason] || e.reason || 'отказ'}<//>`}</td>
        <td>${METHODS[e.method] || e.method}</td><td><code>${e.ip || '—'}</code></td><td title=${e.user_agent}>${fmtAgent(e.user_agent)}</td>
      </tr>`)}</tbody>
    </table></div>`;
}

const RESULTS = [['', 'все'], ['true', 'успешные'], ['false', 'неудачные']];

export function LoginsScreen({ query = {} }) {
  const [search, setSearch] = useState(query.q || '');
  const success = RESULTS.some(([v]) => v === query.success) ? query.success || '' : '';
  const list = useLoad(() => api('/v1/admin/logins', { query: { q: query.q || '', success, limit: 300 } }), [query.q, success]);
  usePageKeys({ refresh: () => list.reload(), search: () => { document.querySelector('[data-search-input]')?.focus(); return true; } });
  return html`
    <div class="screen">
      <${ScreenHead} title="Журнал входов" desc="Входы в панель и регистрации из игры — с неудачными попытками и причинами."
        actions=${html`<${Choice} label="Итог" value=${success} options=${RESULTS} onChange=${(v) => setQuery({ success: v })}/>`}/>
      <div class="toolbar">
        <${SearchBox} value=${search} onInput=${setSearch} placeholder="логин, телефон, адрес, причина" onEnter=${() => setQuery({ q: search.trim() })}/>
        <button type="button" class="btn" onClick=${() => setQuery({ q: search.trim() })}>Найти</button>
      </div>
      <section class="card">
        <${ErrorText} error=${list.error}/>
        ${list.loading && !list.data ? html`<${Loading}/>` : html`<${LoginRows} rows=${list.data || []}/>`}
      </section>
    </div>`;
}
