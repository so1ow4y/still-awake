import { useState, useEffect, useCallback, useRef } from './html.js';
import { subscribe, storeVersion } from './store.js';
import { parseHash } from './router.js';

/**
 * Перерисовывать компонент при любом изменении настроек или сессии. Эффект с подпиской запускается после
 * отрисовки, а ответ сервера может прийти раньше — поэтому при подписке сверяем версию хранилища.
 */
export function useStore() {
  const [, setN] = useState(0);
  const seen = useRef(storeVersion());
  seen.current = storeVersion();
  useEffect(() => {
    const unsubscribe = subscribe(() => setN((n) => n + 1));
    if (storeVersion() !== seen.current) setN((n) => n + 1);
    return unsubscribe;
  }, []);
}

export function useRoute() {
  const [route, setRoute] = useState(parseHash);
  useEffect(() => {
    const f = () => setRoute(parseHash());
    window.addEventListener('hashchange', f);
    // Эффекты детей идут раньше: переход из них при первой отрисовке (Redirect) случился до подписки — сверяемся.
    if (location.hash !== route.hash) f();
    return () => window.removeEventListener('hashchange', f);
  }, []);
  return route;
}

export function useMedia(query) {
  const [m, setM] = useState(() => matchMedia(query).matches);
  useEffect(() => {
    const mq = matchMedia(query);
    const f = () => setM(mq.matches);
    mq.addEventListener('change', f);
    return () => mq.removeEventListener('change', f);
  }, [query]);
  return m;
}

/**
 * Загрузка данных: { data, error, loading, reload, setData }. deps — когда перезагружать.
 * Ответ устаревшего запроса (deps уже сменились) отбрасывается.
 */
export function useLoad(fn, deps = []) {
  const [state, setState] = useState({ data: null, error: null, loading: true });
  const seq = useRef(0);
  const live = useRef(fn);
  live.current = fn;
  const reload = useCallback(async () => {
    const my = ++seq.current;
    setState((s) => ({ ...s, loading: true, error: null }));
    try {
      const data = await live.current();
      if (my === seq.current) setState({ data, error: null, loading: false });
      return data;
    } catch (error) {
      if (my === seq.current) setState((s) => ({ ...s, error, loading: false }));
      return null;
    }
  }, []);
  useEffect(() => { reload(); }, deps);
  const setData = (data) => setState((s) => ({ ...s, data: typeof data === 'function' ? data(s.data) : data }));
  return { ...state, reload, setData };
}

/** Мелкие запоминания интерфейса (раскрытые группы и т. п.). Всегда через try/catch. */
export function readLocal(key, fallback) {
  try {
    const v = localStorage.getItem('sa.' + key);
    return v === null ? fallback : JSON.parse(v);
  } catch {
    return fallback;
  }
}

export function writeLocal(key, value) {
  try {
    localStorage.setItem('sa.' + key, JSON.stringify(value));
  } catch {
    // недоступно — не страшно
  }
}
