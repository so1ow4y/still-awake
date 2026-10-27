// Сессия панели: кто вошёл и какие у него права. Гейтить меню и кнопки — только по правам (как в license-store):
// сервер проверяет те же права на каждом маршруте, а интерфейс лишь не показывает недоступное.

import { api, setUnauthorizedHandler } from './api.js';
import { notify } from './store.js';

let state = { status: 'loading', me: null };

export const session = () => state;
export const me = () => state.me;
export const can = (perm) => !!state.me?.permissions?.includes(perm);
export const canAny = (perms) => !perms || perms.length === 0 || perms.some(can);
export const myPriority = () => state.me?.priority ?? 0;

function set(next) {
  state = next;
  notify();
}

export async function loadSession() {
  try {
    set({ status: 'ready', me: await api('/v1/auth/me') });
  } catch {
    set({ status: 'anon', me: null });
  }
}

export async function login(username, password) {
  const data = await api('/v1/auth/login', { method: 'POST', body: { username, password } });
  set({ status: 'ready', me: data });
}

export async function logout() {
  try {
    await api('/v1/auth/logout', { method: 'POST' });
  } catch {
    // выйти локально обязаны, даже если сервер не ответил
  }
  set({ status: 'anon', me: null });
}

/** Профиль поменялся (логин, имя, пароль) — подхватить новые данные. */
export function updateMe(data) {
  set({ status: 'ready', me: data });
}

setUnauthorizedHandler(() => {
  if (state.status === 'ready') set({ status: 'anon', me: null, expired: true });
});
