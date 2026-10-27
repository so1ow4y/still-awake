// Настройки этого устройства (localStorage 'sa.ui'): док, панель справа, тема, цвет, клавиши.
// Док — из pdd-trainer (Kingdom): положение, развёрнутость, автоскрытие, «спрятать целиком», подпункты веток,
// рост, длина и ширина, сдвиг страницы, dockFill (на телефоне — во всю ширину, на компьютере — плашкой).
// Панель справа — из license-store: сдвигать ли страницу, тянуть ли мышью, ширина.
// Значения из хранилища проверяются: там может лежать что угодно.

import { notify } from './store.js';

const KEY = 'sa.ui';
export const DOCK_POSITIONS = ['left', 'bottom', 'right', 'top'];
export const DOCK_SUB_MODES = ['side', 'inline'];
export const DOCK_GROW = ['down', 'up', 'both'];
export const DOCK_SIZE_MODES = ['auto', 'fixed'];
export const DOCK_SIZE_MIN = 200;
export const DOCK_SIZE_MAX = 1400;
export const DOCK_WIDTH_MIN = 48;
export const DOCK_WIDTH_MAX = 400;
export const DOCK_PUSH = ['none', 'dock', 'all'];
export const THEMES = ['system', 'light', 'dark'];
export const SIDE_PANEL_MIN = 380;
export const SIDE_PANEL_MAX = 1400;

const DOCK_DEFAULTS = {
  dock: null, // null — по устройству: телефон снизу, компьютер слева
  expanded: true,
  autoHide: false,
  dockFill: null, // null — по устройству: телефон — во всю ширину, компьютер — плашкой
  dockSubMode: 'side',
  dockGrow: 'down',
  dockSizeMode: 'auto',
  dockSize: 560,
  dockWidthMode: 'auto',
  dockWidth: 64,
  dockPush: 'dock',
  hidden: false,
};

const PANEL_DEFAULTS = {
  sidePanelPush: false,
  sidePanelResizable: true,
  sidePanelWidth: 600,
};

const DEFAULTS = {
  ...DOCK_DEFAULTS,
  ...PANEL_DEFAULTS,
  theme: 'dark',
  scheme: 'crimson',
  shortcutsOn: true,
  shortcuts: {}, // переназначения клавиш { действие: 'Ctrl+KeyS' | ['KeyS', 'ArrowDown'] | '' (отключено) }
};

const CHOICES = {
  dock: [...DOCK_POSITIONS, null], dockSubMode: DOCK_SUB_MODES, dockGrow: DOCK_GROW, dockSizeMode: DOCK_SIZE_MODES,
  dockWidthMode: DOCK_SIZE_MODES, dockPush: DOCK_PUSH, theme: THEMES, dockFill: [true, false, null],
};
const RANGES = {
  dockSize: [DOCK_SIZE_MIN, DOCK_SIZE_MAX], dockWidth: [DOCK_WIDTH_MIN, DOCK_WIDTH_MAX], sidePanelWidth: [SIDE_PANEL_MIN, SIDE_PANEL_MAX],
};
const FLAGS = ['expanded', 'autoHide', 'hidden', 'shortcutsOn', 'sidePanelPush', 'sidePanelResizable'];

/** Цветовые схемы: акцент для светлой и тёмной темы (как в pdd-trainer). */
export const SCHEMES = {
  crimson: { name: 'Багрянец', light: '#a3122a', dark: '#ef5b6b' },
  indigo: { name: 'Индиго', light: '#3949ab', dark: '#8c9eff' },
  lavender: { name: 'Lavender', light: '#7b4fd6', dark: '#c7a6ff' },
  ocean: { name: 'Океан', light: '#00796b', dark: '#4dd0c4' },
  lime: { name: 'Lime', light: '#5b8c00', dark: '#c6f432' },
  sunset: { name: 'Закат', light: '#d84315', dark: '#ffab76' },
  road: { name: 'Янтарь', light: '#b26a00', dark: '#ffc93c' },
};

/** Проверить значения: чужое — убрать (возьмётся умолчание), числа — в допустимые пределы. */
export function cleanPrefs(p) {
  const out = { ...p };
  for (const [k, list] of Object.entries(CHOICES)) if (k in out && !list.includes(out[k])) delete out[k];
  for (const [k, [lo, hi]] of Object.entries(RANGES)) {
    if (!(k in out)) continue;
    const v = out[k];
    if (typeof v === 'number' && Number.isFinite(v)) out[k] = Math.round(Math.min(hi, Math.max(lo, v)));
    else delete out[k];
  }
  for (const k of FLAGS) if (k in out && typeof out[k] !== 'boolean') delete out[k];
  if ('scheme' in out && !SCHEMES[out.scheme]) delete out.scheme;
  if ('shortcuts' in out && (!out.shortcuts || typeof out.shortcuts !== 'object' || Array.isArray(out.shortcuts))) delete out.shortcuts;
  return out;
}

function load() {
  try {
    return { ...DEFAULTS, ...cleanPrefs(JSON.parse(localStorage.getItem(KEY) || '{}')) };
  } catch {
    return { ...DEFAULTS };
  }
}

let prefs = load();

export const getPrefs = () => prefs;

function save() {
  try {
    localStorage.setItem(KEY, JSON.stringify(prefs));
  } catch {
    // без localStorage настройка просто не запомнится
  }
}

export function setPrefs(patch) {
  prefs = { ...prefs, ...cleanPrefs(patch) };
  save();
  applyLook();
  notify();
}

// Настройки поменяли в другой вкладке — подхватываем без перезагрузки.
window.addEventListener('storage', (e) => {
  if (e.key !== KEY) return;
  prefs = load();
  applyLook();
  notify();
});

export const dockPosition = (phone) => prefs.dock || (phone ? 'bottom' : 'left');
export const dockFill = (phone) => (prefs.dockFill === null ? phone : prefs.dockFill);

/** «Сбросить всё»: док и панель справа — по умолчанию. Тема, цвет и клавиши не трогаются. */
export function resetLook() {
  setPrefs({ ...DOCK_DEFAULTS, ...PANEL_DEFAULTS });
}

/** Тема и цветовая схема — атрибутами на <html> (стили в styles/app.css). */
export function applyLook() {
  const root = document.documentElement;
  if (prefs.theme === 'light' || prefs.theme === 'dark') root.dataset.theme = prefs.theme;
  else delete root.dataset.theme;
  if (prefs.scheme && prefs.scheme !== 'indigo') root.dataset.scheme = prefs.scheme;
  else delete root.dataset.scheme;
}
