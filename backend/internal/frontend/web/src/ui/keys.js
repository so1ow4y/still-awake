// Горячие клавиши — перенос ui/keys.js из pdd-trainer (Kingdom 0.16): реестр действий, сочетания по умолчанию,
// переназначение в «Настройки → Горячие клавиши». На действие можно повесить сколько угодно сочетаний (одно
// сочетание — одно действие). Клавиши узнаются по физической кнопке (KeyboardEvent.code), поэтому работают при
// любой раскладке. Клавиши списков (W/S/D/A) — из license-store: курсор по строкам и панель справа.

import { getPrefs, setPrefs } from './prefs.js';

export const IS_MAC = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform || navigator.userAgent || '');

/** Действия: id, группа, подпись, сочетание по умолчанию (null — не назначено; список — несколько). allow — см. RESERVED. */
export const SHORTCUTS = [
  { id: 'goOverview', group: 'Переходы', label: 'Обзор', def: 'KeyO' },
  { id: 'goModel', group: 'Переходы', label: 'Провайдер и статус модели', def: 'KeyM' },
  { id: 'goPrompt', group: 'Переходы', label: 'Системный промпт', def: 'KeyP' },
  { id: 'goChat', group: 'Переходы', label: 'Отладочный чат', def: 'KeyC' },
  { id: 'goAnalytics', group: 'Переходы', label: 'Аналитика', def: 'KeyN' },
  { id: 'goSessions', group: 'Переходы', label: 'Игровые сессии', def: 'KeyG' },
  { id: 'goDatabase', group: 'Переходы', label: 'База данных', def: 'KeyB' },
  { id: 'goUsers', group: 'Переходы', label: 'Пользователи', def: 'KeyU' },
  { id: 'goRoles', group: 'Переходы', label: 'Роли и права', def: 'KeyR' },
  { id: 'goLogins', group: 'Переходы', label: 'Журнал входов', def: 'KeyL' },
  { id: 'goSettings', group: 'Переходы', label: 'Настройки', def: 'Comma' },
  { id: 'search', group: 'Переходы', label: 'Поиск на экране', hint: 'Встать в строку поиска или фильтра', def: 'Slash' },
  { id: 'back', group: 'Переходы', label: 'Назад', def: 'Backspace', allow: ['Backspace'] },

  { id: 'listDown', group: 'Списки и панель справа', label: 'Строка ниже', hint: 'Если панель справа открыта — она переключается на строку', def: 'KeyS' },
  { id: 'listUp', group: 'Списки и панель справа', label: 'Строка выше', def: 'KeyW' },
  { id: 'listOpen', group: 'Списки и панель справа', label: 'Открыть строку в панели справа', def: 'KeyD' },
  { id: 'listClose', group: 'Списки и панель справа', label: 'Закрыть панель справа', def: 'KeyA' },
  { id: 'refresh', group: 'Списки и панель справа', label: 'Обновить данные экрана', def: 'Shift+KeyR' },

  { id: 'send', group: 'Чат и редакторы', label: 'Отправить или сохранить', hint: 'Сообщение в отладочном чате, промпт, SQL', def: IS_MAC ? 'Meta+Enter' : 'Ctrl+Enter' },

  { id: 'menuUp', group: 'Панель навигации', label: 'Меню: пункт выше', hint: 'Ходит по пунктам панели, включая раскрытую ветку', def: 'KeyQ' },
  { id: 'menuDown', group: 'Панель навигации', label: 'Меню: пункт ниже', def: 'KeyE' },
  { id: 'menuActivate', group: 'Панель навигации', label: 'Меню: открыть пункт', hint: 'Работает, когда выбран пункт панели', def: ['Enter', 'Space'], allow: ['Enter', 'NumpadEnter', 'Space'] },
  { id: 'dockExpand', group: 'Панель навигации', label: 'Свернуть или развернуть панель', def: 'BracketLeft' },
  { id: 'dockHide', group: 'Панель навигации', label: 'Спрятать или показать панель', def: 'BracketRight' },

  { id: 'help', group: 'Справка', label: 'Список горячих клавиш', def: 'Shift+Slash' },
];

export const SHORTCUT_GROUPS = [...new Set(SHORTCUTS.map((s) => s.group))];
const BY_ID = new Map(SHORTCUTS.map((s) => [s.id, s]));

export const defaultsOf = (id) => {
  const def = BY_ID.get(id)?.def;
  return def == null ? [] : (Array.isArray(def) ? def : [def]).map(normalizeCombo);
};

/** Esc — не переназначается. */
export const FIXED = [{ combo: 'Escape', label: 'Закрыть окно, панель справа, шторку, список клавиш' }];

const RESERVED = new Set([
  'Escape', 'Tab', 'Shift+Tab', 'Enter', 'Space', 'Shift+Space', 'Backspace', 'F5', 'Ctrl+F5', 'F11', 'F12',
  'Ctrl+KeyW', 'Ctrl+KeyT', 'Ctrl+KeyN', 'Ctrl+Shift+KeyT', 'Ctrl+Shift+KeyN', 'Ctrl+Shift+KeyW', 'Ctrl+Tab', 'Ctrl+Shift+Tab',
  'Ctrl+KeyR', 'Ctrl+Shift+KeyR', 'Ctrl+KeyL', 'Ctrl+KeyC', 'Ctrl+KeyV', 'Ctrl+KeyX', 'Ctrl+KeyZ', 'Ctrl+KeyY', 'Ctrl+KeyA',
  'Ctrl+Shift+KeyI', 'Ctrl+Shift+KeyJ', 'Ctrl+Shift+KeyC', 'Alt+F4', 'Alt+ArrowLeft', 'Alt+ArrowRight',
  'Meta+KeyW', 'Meta+KeyT', 'Meta+KeyN', 'Meta+KeyQ', 'Meta+KeyR', 'Meta+KeyL', 'Meta+KeyC', 'Meta+KeyV', 'Meta+KeyX',
  'Meta+KeyZ', 'Meta+KeyA', 'Meta+Shift+KeyZ', 'Meta+Tab', 'Meta+KeyH', 'Meta+KeyM',
]);

const MOD_ORDER = ['Ctrl', 'Alt', 'Shift', 'Meta'];
const isModifierCode = (code) => /^(Shift|Control|Alt|Meta|OS)(Left|Right)?$/.test(code) || ['CapsLock', 'Fn', 'FnLock', 'AltGraph', 'ContextMenu'].includes(code);

export function comboFromEvent(e) {
  const code = e.code;
  if (!code || code === 'Unidentified' || isModifierCode(code)) return null;
  const mods = [];
  if (e.ctrlKey) mods.push('Ctrl');
  if (e.altKey) mods.push('Alt');
  if (e.shiftKey) mods.push('Shift');
  if (e.metaKey) mods.push('Meta');
  return [...mods, code].join('+');
}

export function normalizeCombo(combo) {
  if (!combo) return null;
  const parts = combo.split('+').filter(Boolean);
  const code = parts.pop();
  const mods = MOD_ORDER.filter((m) => parts.includes(m));
  return [...mods, code].join('+');
}

const CODE_LABEL = {
  Slash: '/', Backslash: '\\', BracketLeft: '[', BracketRight: ']', Comma: ',', Period: '.', Semicolon: ';', Quote: "'",
  Backquote: '`', Minus: '−', Equal: '=', Space: 'Пробел', Enter: 'Enter', NumpadEnter: 'Enter', Escape: 'Esc',
  Backspace: 'Backspace', Delete: 'Delete', Insert: 'Insert', Tab: 'Tab', Home: 'Home', End: 'End', PageUp: 'PageUp',
  PageDown: 'PageDown', ArrowLeft: '←', ArrowRight: '→', ArrowUp: '↑', ArrowDown: '↓', IntlBackslash: '\\',
  NumpadAdd: 'Num +', NumpadSubtract: 'Num −', NumpadMultiply: 'Num *', NumpadDivide: 'Num /', NumpadDecimal: 'Num .',
};
const RU = {
  KeyQ: 'Й', KeyW: 'Ц', KeyE: 'У', KeyR: 'К', KeyT: 'Е', KeyY: 'Н', KeyU: 'Г', KeyI: 'Ш', KeyO: 'Щ', KeyP: 'З',
  BracketLeft: 'Х', BracketRight: 'Ъ', KeyA: 'Ф', KeyS: 'Ы', KeyD: 'В', KeyF: 'А', KeyG: 'П', KeyH: 'Р', KeyJ: 'О',
  KeyK: 'Л', KeyL: 'Д', Semicolon: 'Ж', Quote: 'Э', KeyZ: 'Я', KeyX: 'Ч', KeyC: 'С', KeyV: 'М', KeyB: 'И', KeyN: 'Т',
  KeyM: 'Ь', Comma: 'Б', Period: 'Ю', Backquote: 'Ё',
};
const MOD_LABEL = IS_MAC ? { Ctrl: '⌃', Alt: '⌥', Shift: '⇧', Meta: '⌘' } : { Ctrl: 'Ctrl', Alt: 'Alt', Shift: 'Shift', Meta: 'Win' };

export function keyLabel(code) {
  if (/^Key[A-Z]$/.test(code)) return code.slice(3);
  if (/^Digit\d$/.test(code)) return code.slice(5);
  if (/^Numpad\d$/.test(code)) return 'Num ' + code.slice(6);
  return CODE_LABEL[code] || code;
}

export function comboParts(combo) {
  if (!combo) return [];
  if (combo === 'Shift+Slash') return ['?'];
  const parts = combo.split('+');
  const code = parts.pop();
  return [...parts.map((m) => MOD_LABEL[m] || m), keyLabel(code)];
}

export const formatCombo = (combo) => comboParts(combo).join(IS_MAC ? '' : ' + ');

export function ruLetter(combo) {
  if (!combo) return '';
  return RU[combo.split('+').pop()] || '';
}

// ---------- Назначения ----------

export const shortcutsOn = () => getPrefs().shortcutsOn !== false;
const hasOwn = (o, k) => Object.prototype.hasOwnProperty.call(o, k);

/** Все сочетания действия (с учётом переназначений). В настройках: '' (отключено), строка или список. */
export function bindingsOf(id, prefs = getPrefs()) {
  const own = prefs.shortcuts || {};
  if (!hasOwn(own, id)) return defaultsOf(id);
  const v = own[id];
  const list = Array.isArray(v) ? v : v ? [v] : [];
  return [...new Set(list.filter((c) => typeof c === 'string' && c).map(normalizeCombo))];
}

export const bindingOf = (id, prefs = getPrefs()) => bindingsOf(id, prefs)[0] ?? null;

export function bindingMap(prefs = getPrefs()) {
  const m = new Map();
  for (const s of SHORTCUTS) for (const c of bindingsOf(s.id, prefs)) if (!m.has(c)) m.set(c, s.id);
  return m;
}

export function conflictOf(combo, exceptId = null, prefs = getPrefs()) {
  const c = normalizeCombo(combo);
  for (const s of SHORTCUTS) if (s.id !== exceptId && bindingsOf(s.id, prefs).includes(c)) return s;
  return null;
}

export const isReserved = (combo, id = null) => {
  const c = normalizeCombo(combo);
  return RESERVED.has(c) && !(BY_ID.get(id)?.allow || []).includes(c);
};

const sameList = (a, b) => a.length === b.length && a.every((x, i) => x === b[i]);

function put(own, id, list) {
  if (sameList(list, defaultsOf(id))) delete own[id];
  else own[id] = list.length === 0 ? '' : list.length === 1 ? list[0] : list;
}

function takeFrom(own, combo, id, prefs) {
  for (const s of SHORTCUTS) {
    if (s.id === id) continue;
    const list = bindingsOf(s.id, { ...prefs, shortcuts: own });
    if (list.includes(combo)) put(own, s.id, list.filter((c) => c !== combo));
  }
}

export function addShortcut(id, combo) {
  const prefs = getPrefs();
  const own = { ...(prefs.shortcuts || {}) };
  const c = normalizeCombo(combo);
  takeFrom(own, c, id, prefs);
  const cur = bindingsOf(id, { ...prefs, shortcuts: own });
  if (!cur.includes(c)) put(own, id, [...cur, c]);
  setPrefs({ shortcuts: own });
}

export function removeShortcut(id, combo) {
  const prefs = getPrefs();
  const own = { ...(prefs.shortcuts || {}) };
  put(own, id, bindingsOf(id, prefs).filter((x) => x !== normalizeCombo(combo)));
  setPrefs({ shortcuts: own });
}

export function resetShortcut(id) {
  const prefs = getPrefs();
  const own = { ...(prefs.shortcuts || {}) };
  delete own[id];
  for (const c of defaultsOf(id)) takeFrom(own, c, id, prefs);
  setPrefs({ shortcuts: own });
}

export const resetAllShortcuts = () => setPrefs({ shortcuts: {} });

/** Сочетание для подсказок: 'N', 'Ctrl + Enter' или ''. */
export const shortcutText = (id) => (shortcutsOn() ? formatCombo(bindingOf(id)) : '');

// ---------- Обработка нажатий ----------

/** Пока в настройках записывают сочетание, общий обработчик молчит. */
export const recorder = { active: false };

export const isEditing = (el) => {
  const tag = el?.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || !!el?.isContentEditable;
};

/** Какое действие вызвать по событию (или null). При вводе текста работают только сочетания с Ctrl / Alt / ⌘. */
export function actionFor(e, prefs = getPrefs()) {
  if (recorder.active || prefs.shortcutsOn === false || e.defaultPrevented || e.isComposing) return null;
  const combo = comboFromEvent(e);
  if (!combo) return null;
  const strong = e.ctrlKey || e.metaKey || e.altKey;
  if (isEditing(e.target) && !strong) return null;
  return bindingMap(prefs).get(combo) || null;
}
