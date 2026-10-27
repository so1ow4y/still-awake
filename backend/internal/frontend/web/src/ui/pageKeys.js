// Клавиши, смысл которых зависит от экрана: поиск, обновить, отправить, клавиши списков.
// Экран при показе подставляет сюда свои обработчики (usePageKeys), при уходе — убирает. false — нажатие не его.
// Списков с панелью справа может быть несколько (список внутри открытой карточки) — управляет последний подключённый.

import { useEffect, useRef, useState } from './html.js';

const none = () => false;
export const pageKeys = {};

export function usePageKeys(handlers) {
  const live = useRef(handlers);
  live.current = handlers;
  useEffect(() => {
    const ids = Object.keys(live.current);
    for (const id of ids) pageKeys[id] = (...a) => live.current[id]?.(...a) ?? false;
    return () => {
      for (const id of ids) pageKeys[id] = none;
    };
  }, []);
}

export const runPageKey = (id, ...args) => (pageKeys[id] ? pageKeys[id](...args) !== false : false);

// ---------- Списки: W/S — курсор, D — открыть в панели справа, A — закрыть (license-store, use-list-hotkeys) ----------

const lists = [];

export const listKeys = {
  run(action) {
    const top = lists[lists.length - 1];
    return top ? top(action) : false;
  },
};

function scrollToRow(id) {
  setTimeout(() => document.querySelector(`[data-row-id="${CSS.escape(id)}"]`)?.scrollIntoView({ block: 'nearest' }), 0);
}

/** Возвращает id строки под курсором — для подсветки. Строкам нужен атрибут data-row-id. */
export function useListKeys({ ids, openedId, onOpen, onClose, enabled = true }) {
  const [cursor, setCursor] = useState(null);
  const state = useRef({});
  state.current = { ids, openedId, onOpen, onClose, cursor };

  useEffect(() => {
    if (openedId) setCursor(openedId);
  }, [openedId]);

  const key = ids.join('|');
  useEffect(() => {
    setCursor((cur) => (cur && !state.current.ids.includes(cur) ? null : cur));
  }, [key]);

  useEffect(() => {
    if (!enabled) return undefined;
    const handler = (action) => {
      const s = state.current;
      const move = (delta) => {
        if (s.ids.length === 0) return false;
        const index = s.cursor ? s.ids.indexOf(s.cursor) : -1;
        const nextIndex = index < 0 ? (delta > 0 ? 0 : s.ids.length - 1) : Math.max(0, Math.min(s.ids.length - 1, index + delta));
        const next = s.ids[nextIndex];
        s.cursor = next;
        setCursor(next);
        if (s.openedId) s.onOpen(next);
        scrollToRow(next);
        return true;
      };
      switch (action) {
        case 'listDown': return move(1);
        case 'listUp': return move(-1);
        case 'listOpen': {
          const id = s.cursor ?? s.ids[0];
          if (!id) return false;
          setCursor(id);
          s.onOpen(id);
          return true;
        }
        case 'listClose':
          if (!s.openedId) return false;
          s.onClose();
          return true;
        default: return false;
      }
    };
    lists.push(handler);
    return () => {
      const i = lists.indexOf(handler);
      if (i >= 0) lists.splice(i, 1);
    };
  }, [enabled]);

  return cursor;
}
