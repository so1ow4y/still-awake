// Запросы к backend. Cookie сессии ставит сервер (HttpOnly); заголовок X-Requested-With обязателен для
// изменяющих запросов — так сервер отличает панель от чужого сайта (защита от CSRF).

export class ApiError extends Error {
  constructor(status, code, message) {
    super(message);
    this.status = status;
    this.code = code;
  }
}

let onUnauthorized = () => {};
export const setUnauthorizedHandler = (fn) => { onUnauthorized = fn; };

export async function api(path, { method = 'GET', body, query } = {}) {
  let url = path;
  if (query) {
    const q = new URLSearchParams();
    for (const [k, v] of Object.entries(query)) if (v !== '' && v != null) q.set(k, String(v));
    const qs = q.toString();
    if (qs) url += '?' + qs;
  }
  const res = await fetch(url, {
    method,
    credentials: 'same-origin',
    headers: { 'Content-Type': 'application/json', 'X-Requested-With': 'StillAwakePanel' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });
  const text = await res.text();
  let data = null;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    data = text;
  }
  if (!res.ok) {
    const message = (data && typeof data === 'object' && data.error) || (typeof data === 'string' && data) || `${res.status} ${res.statusText}`;
    if (res.status === 401 && path !== '/v1/auth/login') onUnauthorized();
    throw new ApiError(res.status, (data && data.code) || '', message);
  }
  return data;
}

export const pretty = (v) => JSON.stringify(v, null, 2);

/** JSON из текстового поля: пусто — fallback, ошибка — понятное сообщение. */
export function parseJSON(text, fallback = {}) {
  const v = (text || '').trim();
  if (!v) return fallback;
  try {
    return JSON.parse(v);
  } catch (e) {
    throw new Error('Неверный JSON: ' + e.message);
  }
}
