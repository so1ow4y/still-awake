// Маршрутизация по #/путь?параметры — серверу не нужна настройка под SPA.

import { html } from './html.js';

export const HOME = '/overview';

export function parseHash() {
  const raw = location.hash.replace(/^#/, '') || HOME;
  const [path, qs = ''] = raw.split('?');
  const parts = path.split('/').filter(Boolean);
  return {
    name: parts[0] || HOME.slice(1),
    param: parts[1] ? decodeURIComponent(parts[1]) : null,
    query: Object.fromEntries(new URLSearchParams(qs)),
    path: '/' + parts.join('/'),
    /** Адрес, из которого разобран маршрут, — чтобы заметить переход, случившийся до подписки. */
    hash: location.hash,
  };
}

export function navigate(to, { replace = false } = {}) {
  const url = '#' + to;
  if (replace) history.replaceState(null, '', url);
  else history.pushState(null, '', url);
  window.dispatchEvent(new HashChangeEvent('hashchange'));
}

/** Поменять параметры адреса текущего экрана, не добавляя запись в историю. */
export function setQuery(patch) {
  const route = parseHash();
  const q = { ...route.query, ...patch };
  for (const k of Object.keys(q)) if (q[k] === '' || q[k] == null) delete q[k];
  const qs = new URLSearchParams(q).toString();
  navigate(route.path + (qs ? '?' + qs : ''), { replace: true });
}

export function goBack(fallback) {
  if (history.length > 1) history.back();
  else navigate(fallback, { replace: true });
}

const plainClick = (e) => !(e.ctrlKey || e.metaKey || e.shiftKey || e.button === 1);

export function Link({ to, className = '', children, ...rest }) {
  return html`<a href=${'#' + to} class=${className} ...${rest}
    onClick=${(e) => { if (!plainClick(e)) return; e.preventDefault(); navigate(to); }}>${children}</a>`;
}
