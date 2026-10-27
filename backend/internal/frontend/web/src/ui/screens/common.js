// Общие куски экранов: шапка раздела, плитки статуса сервисов, метки ролей и статусов.

import { html } from '../html.js';
import { Badge } from '../components/ui.js';

export function ScreenHead({ title, desc, actions }) {
  return html`
    <header class="screen-head">
      <div><h2>${title}</h2>${desc ? html`<p class="muted">${desc}</p>` : null}</div>
      ${actions ? html`<div class="screen-actions">${actions}</div>` : null}
    </header>`;
}

/** Плитка статуса: заголовок, крупное значение с цветом, подпись. */
export function StatusTile({ label, value, tone = '', meta }) {
  return html`
    <div class=${'status-tile ' + tone}>
      <span class="status-label">${label}</span>
      <strong>${value}</strong>
      ${meta ? html`<span class="status-meta">${meta}</span>` : null}
    </div>`;
}

export function Stat({ label, value, tone = '' }) {
  return html`<div class=${'stat ' + tone}><strong>${value}</strong>${label}</div>`;
}

export function RoleBadges({ roles = [] }) {
  if (!roles.length) return html`<span class="muted small">без ролей</span>`;
  return html`<span class="badges">${roles.map((r) => html`<${Badge} key=${r.slug} tone=${r.priority >= 80 ? 'accent' : ''} title=${r.slug + ' · уровень ' + r.priority}>${r.name}<//>`)}</span>`;
}

export function StatusBadge({ user }) {
  if (user.status === 'blocked') return html`<${Badge} tone="danger" title=${user.blocked_reason}>заблокирован<//>`;
  if (user.must_change_password) return html`<${Badge} tone="warn">сменить пароль<//>`;
  return html`<${Badge} tone="ok">активен<//>`;
}

export function KindBadge({ kind }) {
  return kind === 'player' ? html`<${Badge}>игрок<//>` : html`<${Badge} tone="accent-soft">сотрудник<//>`;
}
