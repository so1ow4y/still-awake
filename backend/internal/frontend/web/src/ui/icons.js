// Иконки — инлайновый SVG, цвет наследуется (currentColor). Набор — из pdd-trainer (Kingdom), плюс свои для панели.

import { html } from './html.js';

const PATHS = {
  check: 'M5 12.5l4.5 4.5L19 7.5',
  plus: 'M12 5v14M5 12h14',
  minus: 'M5 12h14',
  close: 'M6 6l12 12M18 6L6 18',
  back: 'M15 5l-7 7 7 7',
  chevron: 'M9 6l6 6-6 6',
  chevronDown: 'M6 9l6 6 6-6',
  more: 'M5 12h.01M12 12h.01M19 12h.01',
  dots: 'M12 5h.01M12 12h.01M12 19h.01',
  settings: 'M4 6h9M17 6h3M4 12h3M11 12h9M4 18h11M19 18h1M15 4v4M9 10v4M17 16v4',
  grip: 'M9 6h.01M15 6h.01M9 12h.01M15 12h.01M9 18h.01M15 18h.01',
  restore: 'M4 12a8 8 0 108-8 8 8 0 00-6.3 3M4 4v4h4',
  list: 'M4 6h16M4 12h16M4 18h10',
  warn: 'M12 3l10 18H2zM12 10v5M12 18h.01',
  chart: 'M4 20V10M10 20V4M16 20v-7M22 20H2',
  search: 'M10.5 4a6.5 6.5 0 100 13 6.5 6.5 0 000-13zM15.5 15.5L20 20',
  palette: 'M12 3a9 9 0 100 18c1.1 0 1.6-.7 1.6-1.5 0-1-.8-1.3-.8-2.2 0-.9.7-1.6 1.6-1.6H17a4 4 0 004-4c0-4.9-4-8.7-9-8.7zM7.5 12h.01M9.5 7.5h.01M14.5 7.5h.01M17 11h.01',
  keyboard: 'M4 6h16a1 1 0 011 1v10a1 1 0 01-1 1H4a1 1 0 01-1-1V7a1 1 0 011-1zM7 10h.01M11 10h.01M15 10h.01M17 14h.01M7 14h.01M10 14h4',
  download: 'M12 5v11M7 11l5 5 5-5M5 19h14',
  upload: 'M12 16V5M7 10l5-5 5 5M5 19h14',
  trash: 'M4 7h16M10 11v6M14 11v6M9 7V4h6v3M6 7l1 13h10l1-13',
  eye: 'M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12zM12 9a3 3 0 100 6 3 3 0 000-6z',
  eyeOff: 'M3 3l18 18M10.6 5.1A10.5 10.5 0 0112 5c6.4 0 10 7 10 7a17.6 17.6 0 01-3.1 4M6.6 6.6C3.8 8.4 2 12 2 12s3.6 7 10 7a9.9 9.9 0 005.4-1.6M9.9 9.9a3 3 0 004.2 4.2',
  info: 'M12 3a9 9 0 100 18 9 9 0 000-18zM12 11v5M12 8h.01',
  logout: 'M10 17l-5-5 5-5M5 12h11M14 4h4a2 2 0 012 2v12a2 2 0 01-2 2h-4',
  login: 'M14 7l5 5-5 5M19 12H8M10 4H6a2 2 0 00-2 2v12a2 2 0 002 2h4',
  // Панель
  home: 'M4 11l8-7 8 7M6 9.5V20h12V9.5M10 20v-6h4v6',
  cpu: 'M8 4v3M16 4v3M8 17v3M16 17v3M4 8h3M4 16h3M17 8h3M17 16h3M7 7h10v10H7zM10 10h4v4h-4z',
  prompt: 'M5 4h14a1 1 0 011 1v14a1 1 0 01-1 1H5a1 1 0 01-1-1V5a1 1 0 011-1zM8 9l3 3-3 3M13 15h3',
  chat: 'M4 5h16v11H9l-5 4zM8 9h8M8 12h5',
  gamepad: 'M7 8h10a4 4 0 014 4v1a3 3 0 01-5.4 1.8L14.5 14h-5l-1.1 1.8A3 3 0 013 13v-1a4 4 0 014-4zM8 11v3M6.5 12.5h3M15.5 11.5h.01M17.5 13.5h.01',
  database: 'M12 3c4.4 0 8 1.3 8 3s-3.6 3-8 3-8-1.3-8-3 3.6-3 8-3zM4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3',
  users: 'M9 11a4 4 0 100-8 4 4 0 000 8zM2 20a7 7 0 0114 0M16 4a4 4 0 010 7M22 20a7 7 0 00-4.5-6.5',
  user: 'M12 12a4 4 0 100-8 4 4 0 000 8zM5 20a7 7 0 0114 0',
  shield: 'M12 3l8 3v6c0 4.5-3.4 8.3-8 9-4.6-.7-8-4.5-8-9V6zM9 12l2 2 4-4',
  lock: 'M6 11h12v9H6zM8 11V8a4 4 0 018 0v3',
  key: 'M14 10a4 4 0 10-3.9 4H11l1.5 1.5L14 14l1.5 1.5L17 14l-2.5-2.5A4 4 0 0014 10zM8 10h.01',
  copy: 'M9 9h10v11H9zM5 15V4h10',
  play: 'M7 4l12 8-12 8z',
  refresh: 'M20 12a8 8 0 11-2.3-5.7M20 4v5h-5',
  send: 'M4 12l16-8-6 16-2.5-6.5z',
  ban: 'M12 3a9 9 0 100 18 9 9 0 000-18zM5.6 5.6l12.8 12.8',
  phone: 'M7 3h10a1 1 0 011 1v16a1 1 0 01-1 1H7a1 1 0 01-1-1V4a1 1 0 011-1zM11 18h2',
  history: 'M12 7v5l3 2M4 12a8 8 0 108-8 8 8 0 00-6.3 3M4 4v3h3',
};

export function Icon({ name, size = 22, className = '' }) {
  const d = PATHS[name] || PATHS.dots;
  return html`<svg class=${'icon ' + className} width=${size} height=${size} viewBox="0 0 24 24" aria-hidden="true"
    fill="none" stroke="currentColor" stroke-width=${['more', 'dots', 'grip'].includes(name) ? 3 : 2}
    stroke-linecap="round" stroke-linejoin="round"><path d=${d}/></svg>`;
}
