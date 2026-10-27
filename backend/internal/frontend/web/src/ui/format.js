// Форматирование для таблиц: даты по-русски, числа с разрядами.

const dateTime = new Intl.DateTimeFormat('ru-RU', { day: '2-digit', month: '2-digit', year: 'numeric', hour: '2-digit', minute: '2-digit' });
const timeOnly = new Intl.DateTimeFormat('ru-RU', { hour: '2-digit', minute: '2-digit', second: '2-digit' });
const number = new Intl.NumberFormat('ru-RU');

export function fmtDate(v) {
  if (!v) return '—';
  const d = new Date(v);
  return Number.isNaN(d.getTime()) ? v : dateTime.format(d);
}

export function fmtTime(v) {
  if (!v) return '';
  const d = new Date(v);
  return Number.isNaN(d.getTime()) ? v : timeOnly.format(d);
}

/** «5 мин назад», «вчера» — для «последний вход». */
export function fmtAgo(v) {
  if (!v) return 'никогда';
  const d = new Date(v);
  if (Number.isNaN(d.getTime())) return v;
  const s = Math.round((Date.now() - d.getTime()) / 1000);
  if (s < 60) return 'только что';
  if (s < 3600) return `${Math.round(s / 60)} мин назад`;
  if (s < 86400) return `${Math.round(s / 3600)} ч назад`;
  if (s < 172800) return 'вчера';
  return fmtDate(v);
}

export const fmtNum = (v) => (v == null || v === '' ? '—' : number.format(Number(v)));

/** «Chrome / Windows» из user-agent — коротко для списков сессий. */
export function fmtAgent(ua) {
  if (!ua) return '—';
  if (/UnityPlayer|Unity/.test(ua)) return 'Игра (Unity)';
  const browser = /Edg\//.test(ua) ? 'Edge' : /Firefox\//.test(ua) ? 'Firefox' : /Chrome\//.test(ua) ? 'Chrome' : /Safari\//.test(ua) ? 'Safari' : 'Клиент';
  const os = /Windows/.test(ua) ? 'Windows' : /Android/.test(ua) ? 'Android' : /iPhone|iPad/.test(ua) ? 'iOS' : /Mac OS/.test(ua) ? 'macOS' : /Linux/.test(ua) ? 'Linux' : '';
  return os ? `${browser} / ${os}` : browser;
}
