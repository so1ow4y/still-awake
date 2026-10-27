// Док-панель — перенос дока из pdd-trainer (Kingdom 0.16): три вида — icon (свёрнут, значки с подсказками),
// row (развёрнут у бокового края: подписи, группы, ветки раскрываются на месте), tile (развёрнут сверху или снизу).
// Квадрат с буквой «S» прячет док целиком, автоскрытие убирает его за край до наведения мыши; ветки в свёрнутом
// доке раскрываются колонкой сбоку (side) или внутри (inline); рост, длина, ширина и сдвиг страницы — настройки.
//
// Телефон (свёрнутый док сверху или снизу) — нижняя панель: четыре раздела и «Ещё» (развёрнутый док шторкой поверх).
// fill — «Занимать всё пространство»: док во всю длину края, без отступа и скруглений (на телефоне по умолчанию).
// Пункты — только те, на которые у пользователя есть права (nav.js).

import { html, useState, useRef, useEffect, useLayoutEffect } from '../html.js';
import { Icon } from '../icons.js';
import { navigate } from '../router.js';
import { buildMenu, compactMenu, isTargetActive, isBranchActive } from '../nav.js';
import { shortcutText } from '../keys.js';

/** Отступ плашки от края экрана — padding обёртки (у дока «во всё пространство» — 0). */
export const DOCK_EDGE_GAP = 12;
const STRIP_GAP = 8;
const INWARD = { left: 'right', right: 'left', top: 'bottom', bottom: 'top' };
const SIDES = ['left', 'right', 'top', 'bottom'];
const AUTO_HIDE_DELAY_MS = 400;
const ICON_SIZE = { icon: 22, row: 20, tile: 22 };

/** Клавиши дока для app.js: пока док виден, он подставляет сюда свои обработчики. */
export const dockKeys = { move: () => false, activate: () => false };

export function AppLetter({ size = 36 }) {
  return html`<span class="app-letter" style=${{ width: size + 'px', height: size + 'px', fontSize: size * 0.5 + 'px' }} aria-hidden="true">S</span>`;
}

const hintOf = (ctx, label, key = null, show = ctx.mode === 'icon' && !ctx.compact) => (show
  ? { 'data-hint': label, 'data-hint-side': ctx.side, 'data-hint-kbd': key ? shortcutText(key) : '' }
  : {});

const plainClick = (e) => !(e.ctrlKey || e.metaKey || e.shiftKey || e.button === 1);

const linkKey = (item) => 'l:' + item.to;
const branchKey = (item) => 'b:' + item.id;
const subKey = (branchId, sub) => 's:' + branchId + ':' + sub.to;

const Divider = () => html`<span class="dock-sep" aria-hidden="true"></span>`;
const Indicator = () => html`<i class="dock-indicator" aria-hidden="true"></i>`;

function Mark({ item, size }) {
  return item.icon ? html`<${Icon} name=${item.icon} size=${size}/>` : null;
}

function DockLink({ item, ctx }) {
  const active = isTargetActive(ctx.route, item);
  return html`
    <a href=${'#' + item.to} class="dock-item" data-dock-key=${linkKey(item)} data-active=${active ? 'true' : 'false'}
      aria-current=${active ? 'page' : undefined}
      onClick=${(e) => { if (!plainClick(e)) return; e.preventDefault(); ctx.setOpenBranch(null); ctx.onNavigate(); navigate(item.to); }}
      ...${hintOf(ctx, item.title, item.key)}>
      ${active ? html`<${Indicator}/>` : null}
      <${Mark} item=${item} size=${ICON_SIZE[ctx.mode]}/>
      <span class="dock-label">${item.title}</span>
    </a>`;
}

function SubLink({ item, branch, ctx, compact = false }) {
  const active = isTargetActive(ctx.route, item);
  const tile = ctx.mode === 'tile' && !compact;
  return html`
    <a href=${'#' + item.to} class=${'dock-item dock-sq' + (compact ? ' compact' : '')} data-dock-key=${subKey(branch, item)}
      data-active=${active ? 'true' : 'false'} aria-current=${active ? 'page' : undefined}
      onClick=${(e) => { if (!plainClick(e)) return; e.preventDefault(); ctx.onNavigate(); navigate(item.to); }}
      ...${hintOf(ctx, item.title, item.key, !tile)}>
      <${Mark} item=${item} size=${compact ? 18 : tile ? ICON_SIZE.tile : ICON_SIZE.icon}/>
      <span class="dock-label">${item.title}</span>
    </a>`;
}

function DockBranch({ item, ctx }) {
  const active = isBranchActive(ctx.route, item);
  const open = ctx.openBranch === item.id;
  const content = html`
    ${active ? html`<${Indicator}/>` : null}
    <${Icon} name=${item.icon} size=${ICON_SIZE[ctx.mode]}/>
    <span class="dock-label">${item.title}</span>`;

  if (ctx.mode === 'row') {
    return html`
      <div class=${'dock-branch' + (open ? ' open' : '')}>
        <button type="button" class="dock-item" data-dock-key=${branchKey(item)} data-active=${active ? 'true' : 'false'}
          aria-expanded=${open} onClick=${() => ctx.toggleBranch(item.id)}>
          ${content}<${Icon} name="chevron" size=${16} className="dock-chevron"/>
        </button>
        <div class="dock-collapse" inert=${!open} data-dock-inline><div class="dock-collapse-inner"><div class="dock-sub">
          ${item.items.map((sub) => {
            const subActive = isTargetActive(ctx.route, sub);
            return html`
              <a key=${sub.to} href=${'#' + sub.to} class="dock-item dock-subitem" data-dock-key=${subKey(item.id, sub)}
                data-active=${subActive ? 'true' : 'false'} aria-current=${subActive ? 'page' : undefined}
                onClick=${(e) => { if (!plainClick(e)) return; e.preventDefault(); ctx.onNavigate(); navigate(sub.to); }}>
                <${Mark} item=${sub} size=${16}/>
                <span class="dock-label">${sub.title}</span>
              </a>`;
          })}
        </div></div></div>
      </div>`;
  }

  // В шторке на телефоне подветка тоже внутри: колонка рядом со шторкой ушла бы за экран.
  const inline = open && ((ctx.mode === 'icon' && ctx.subMode === 'inline') || ctx.overlay);
  const button = html`
    <button type="button" ref=${(el) => ctx.registerBranch(item.id, el)} class="dock-item" data-dock-key=${branchKey(item)}
      data-active=${active || open ? 'true' : 'false'} aria-expanded=${open} onClick=${() => ctx.toggleBranch(item.id)}
      ...${hintOf(ctx, item.title)}>
      ${content}
    </button>`;
  if (!inline) return button;
  return html`
    ${button}
    <div class="dock-inline" role="group" aria-label=${item.title} data-dock-inline>
      ${item.items.map((sub) => html`<${SubLink} key=${sub.to} item=${sub} branch=${item.id} ctx=${ctx} compact=${ctx.mode === 'icon'}/>`)}
    </div>`;
}

/** Нижняя панель телефона: кнопки делят ширину поровну, подпись под значком. */
function CompactBar({ ctx, onToggle }) {
  return html`
    ${compactMenu().map((it) => {
      const active = isTargetActive(ctx.route, it);
      return html`
        <a key=${it.to} href=${'#' + it.to} class="dock-item nav-btn" data-active=${active ? 'true' : 'false'} aria-current=${active ? 'page' : undefined}
          onClick=${(e) => { if (!plainClick(e)) return; e.preventDefault(); ctx.onNavigate(); navigate(it.to); }}>
          <${Icon} name=${it.icon} size=${21}/><span class="nav-label">${it.title}</span>
        </a>`;
    })}
    <button type="button" class="dock-item nav-btn" onClick=${onToggle} aria-expanded="false" aria-label="Все разделы">
      <${Icon} name="grip" size=${21}/><span class="nav-label">Ещё</span>
    </button>`;
}

export function Dock({ route, phone, position, expanded, hidden, autoHide, overlay, fill, onToggle, onToggleHidden, onNavigate,
  username = '', onLogout, subMode = 'side', grow = 'down', sizeMode = 'auto', size = 560, thickness = null, pagePush = 'dock' }) {
  const panel = useRef(null);
  const inner = useRef(null);
  const strip = useRef(null);
  const branchEls = useRef(new Map());
  const hideTimer = useRef(null);
  const [revealed, setRevealed] = useState(false);
  const [stripOffset, setStripOffset] = useState(0);
  const [anchorOffset, setAnchorOffset] = useState(0);
  const vertical = position === 'left' || position === 'right';
  const mode = hidden || !expanded ? 'icon' : vertical ? 'row' : 'tile';
  const compact = phone && !vertical && mode === 'icon';
  // Док во всё пространство, шторка и нижняя панель телефона стоят по центру — закреплять край незачем.
  const growMode = overlay || compact || fill ? 'both' : grow;
  const slidOut = autoHide && !hidden && !revealed;
  const gap = fill && !overlay ? 0 : DOCK_EDGE_GAP;

  const groups = buildMenu();
  const branches = groups.flatMap((g) => g.items.filter((it) => it.items));

  let routeBranch = null;
  for (const b of branches) if (isBranchActive(route, b)) { routeBranch = b.id; break; }
  const [openBranch, setOpenBranch] = useState(routeBranch);
  // Подветка следует за экраном: перешли в другую ветку — она и раскрыта, на экран вне веток — свёрнута.
  useEffect(() => {
    setOpenBranch(routeBranch);
  }, [routeBranch, route.path]);

  const openItem = branches.find((b) => b.id === openBranch) || null;
  const showStrip = !hidden && !overlay && !compact && !!openItem && (mode === 'tile' || (mode === 'icon' && subMode === 'side'));
  const toggleBranch = (id) => setOpenBranch((cur) => (cur === id ? null : id));

  const ctx = {
    mode, side: INWARD[position], position, vertical, route, subMode, overlay, compact, openBranch, onNavigate, toggleBranch, setOpenBranch,
    registerBranch: (id, el) => {
      if (el) branchEls.current.set(id, el);
      else branchEls.current.delete(id);
    },
  };

  // --dock-<край> — сколько места отдаёт страница (по pagePush), --dock-size-<край> — сколько док занимает на самом деле.
  useLayoutEffect(() => {
    const root = document.documentElement.style;
    const report = () => {
      if (overlay) return;
      for (const s of SIDES) {
        root.setProperty(`--dock-${s}`, '0px');
        root.setProperty(`--dock-size-${s}`, '0px');
      }
      if (autoHide || hidden || !panel.current) return;
      const own = (vertical ? panel.current.offsetWidth : panel.current.offsetHeight) + gap;
      const st = showStrip && strip.current ? (vertical ? strip.current.offsetWidth : strip.current.offsetHeight) + STRIP_GAP : 0;
      root.setProperty(`--dock-size-${position}`, own + 'px');
      root.setProperty(`--dock-${position}`, (pagePush === 'none' ? 0 : own + (pagePush === 'all' ? st : 0)) + 'px');
    };
    report();
    if (!panel.current || typeof ResizeObserver === 'undefined') return undefined;
    const ro = new ResizeObserver(report);
    ro.observe(panel.current);
    if (showStrip && strip.current) ro.observe(strip.current);
    return () => ro.disconnect();
  }, [position, hidden, autoHide, mode, overlay, compact, pagePush, showStrip, openBranch, fill]);

  // Закреплённый край: док ставится так, будто ни одна ветка внутри не раскрыта, и растёт только в сторону grow.
  useLayoutEffect(() => {
    if (growMode === 'both' || hidden) {
      setAnchorOffset(0);
      return undefined;
    }
    const compute = () => {
      const nav = panel.current;
      if (!nav) return;
      const available = (vertical ? innerHeight : innerWidth) - 2 * DOCK_EDGE_GAP;
      let base;
      if (sizeMode === 'fixed') base = Math.min(size, available);
      else {
        const full = (vertical ? nav.scrollHeight : nav.scrollWidth) + 2;
        let extra = 0;
        for (const el of nav.querySelectorAll('[data-dock-inline]')) {
          extra += (vertical ? el.offsetHeight : el.offsetWidth) + (el.classList.contains('dock-inline') ? 2 : 0);
        }
        base = Math.min(full - extra, available);
      }
      setAnchorOffset(Math.max(0, Math.round((available - base) / 2)));
    };
    compute();
    const ro = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(compute) : null;
    if (panel.current) ro?.observe(panel.current);
    window.addEventListener('resize', compute);
    return () => {
      ro?.disconnect();
      window.removeEventListener('resize', compute);
    };
  }, [growMode, hidden, vertical, sizeMode, size, mode, subMode, openBranch]);

  // Колонка подветки начинается вровень со значком ветки и не вылезает за экран.
  useLayoutEffect(() => {
    if (!showStrip || !openBranch) return undefined;
    const place = () => {
      const box = inner.current?.getBoundingClientRect();
      const st = strip.current;
      const anchor = branchEls.current.get(openBranch);
      if (!box || !st || !anchor) return;
      const at = anchor.getBoundingClientRect();
      if (vertical) {
        const max = innerHeight - DOCK_EDGE_GAP - box.top - st.offsetHeight;
        setStripOffset(Math.max(DOCK_EDGE_GAP - box.top, Math.min(at.top - box.top - 6, max)));
      } else {
        const max = innerWidth - DOCK_EDGE_GAP - box.left - st.offsetWidth;
        setStripOffset(Math.max(DOCK_EDGE_GAP - box.left, Math.min(at.left - box.left - 6, max)));
      }
    };
    place();
    const ro = typeof ResizeObserver !== 'undefined' ? new ResizeObserver(place) : null;
    if (strip.current) ro?.observe(strip.current);
    if (inner.current) ro?.observe(inner.current);
    const nav = panel.current;
    window.addEventListener('resize', place);
    nav?.addEventListener('scroll', place);
    return () => {
      ro?.disconnect();
      window.removeEventListener('resize', place);
      nav?.removeEventListener('scroll', place);
    };
  }, [showStrip, openBranch, vertical, position, mode]);

  useEffect(() => () => clearTimeout(hideTimer.current), []);

  const reveal = () => {
    clearTimeout(hideTimer.current);
    setRevealed(true);
  };
  const scheduleHide = () => {
    if (!autoHide) return;
    clearTimeout(hideTimer.current);
    hideTimer.current = setTimeout(() => setRevealed(false), AUTO_HIDE_DELAY_MS);
  };

  // Пункты в порядке показа — для клавиш Q/E и Enter.
  const entries = [];
  for (const g of groups) {
    for (const it of g.items) {
      if (it.items) {
        entries.push({ key: branchKey(it), kind: 'branch', id: it.id, active: isBranchActive(route, it) });
        if (openBranch === it.id) {
          for (const sub of it.items) entries.push({ key: subKey(it.id, sub), kind: 'link', to: sub.to, sub: true, active: isTargetActive(route, sub) });
        }
      } else entries.push({ key: linkKey(it), kind: 'link', to: it.to, active: isTargetActive(route, it) });
    }
  }
  entries.push({ key: linkKey({ to: '/settings' }), kind: 'link', to: '/settings', active: route.name === 'settings' });
  const live = useRef({});
  live.current = { entries, autoHide, onNavigate };

  useEffect(() => {
    if (hidden || compact) return undefined;
    const focusEntry = (key) => {
      const apply = () => {
        const el = document.querySelector(`[data-dock-key="${CSS.escape(key)}"]`);
        if (!el) return false;
        el.focus({ preventScroll: true });
        el.scrollIntoView({ block: 'nearest', inline: 'nearest' });
        return true;
      };
      if (!apply()) setTimeout(apply, 0);
    };
    const focusedKey = () => document.activeElement?.closest?.('[data-dock-key]')?.dataset.dockKey ?? null;
    dockKeys.move = (delta) => {
      const list = live.current.entries;
      if (!list.length) return false;
      if (live.current.autoHide) reveal();
      const cur = focusedKey();
      const i = cur ? list.findIndex((e) => e.key === cur) : -1;
      let next;
      if (i < 0) {
        const a = list.findIndex((e) => e.kind === 'link' && e.active);
        next = a >= 0 ? a : delta > 0 ? 0 : list.length - 1;
      } else next = Math.max(0, Math.min(list.length - 1, i + delta));
      focusEntry(list[next].key);
      return true;
    };
    dockKeys.activate = () => {
      const entry = live.current.entries.find((e) => e.key === focusedKey());
      if (!entry) return false;
      if (entry.kind === 'branch') {
        toggleBranch(entry.id);
        focusEntry(entry.key);
      } else {
        if (!entry.sub) setOpenBranch(null);
        live.current.onNavigate();
        navigate(entry.to);
      }
      return true;
    };
    return () => {
      dockKeys.move = () => false;
      dockKeys.activate = () => false;
    };
  }, [hidden, compact]);

  const wrapClass = (extra = '') => 'dock-wrap pos-' + position + ' grow-' + growMode + (fill && !overlay ? ' fill' : '') + extra;
  const letterLabel = hidden ? 'Показать панель' : 'Спрятать панель';
  const letter = html`
    <button type="button" class="dock-item dock-brand" onClick=${onToggleHidden} aria-label=${letterLabel} aria-pressed=${hidden}
      ...${hintOf(ctx, letterLabel, 'dockHide')}>
      <${AppLetter} size=${36}/><span class="dock-label brand-label">Still Awake?</span>
    </button>`;

  if (hidden) {
    return html`<div class=${'dock-wrap pos-' + position + ' grow-both'}>
      <nav ref=${panel} class="dock dock-hidden mode-icon" aria-label="Навигация">${letter}</nav></div>`;
  }

  const toggleLabel = expanded ? 'Свернуть панель' : 'Развернуть панель';
  const hover = autoHide ? { onMouseEnter: reveal, onMouseLeave: scheduleHide, onFocusIn: reveal, onFocusOut: scheduleHide } : {};

  if (compact) {
    return html`
      <div class=${wrapClass()}>
        <div ref=${inner} class="dock-inner compact">
          <nav ref=${panel} aria-label="Навигация" class="dock mode-icon compact">
            <${CompactBar} ctx=${ctx} onToggle=${onToggle}/>
          </nav>
        </div>
      </div>`;
  }

  const anchorStyle = growMode === 'both' ? {} : vertical
    ? { [growMode === 'down' ? 'marginTop' : 'marginBottom']: anchorOffset + 'px', maxHeight: `calc(100% - ${anchorOffset}px)` }
    : { [growMode === 'down' ? 'marginLeft' : 'marginRight']: anchorOffset + 'px', maxWidth: `calc(100% - ${anchorOffset}px)` };
  const sizeStyle = {
    ...(sizeMode === 'fixed' && !overlay && !fill ? (vertical ? { height: size + 'px' } : { width: size + 'px' }) : {}),
    ...(thickness != null && !overlay ? (vertical ? { width: thickness + 'px' } : { height: thickness + 'px' }) : {}),
  };
  const settingsItem = { to: '/settings', title: 'Настройки', icon: 'settings', names: ['settings'], key: 'goSettings' };
  const profileItem = { to: '/settings?section=profile', title: username || 'Профиль', icon: 'user' };

  return html`
    ${autoHide ? html`<div class=${'dock-hot pos-' + position} aria-hidden="true" onMouseEnter=${reveal}></div>` : null}
    <div class=${wrapClass((slidOut ? ' slid' : '') + (overlay ? ' is-overlay' : ''))}>
      <div ref=${inner} class="dock-inner" style=${anchorStyle}>
        <nav ref=${panel} aria-label="Навигация" style=${sizeStyle}
          class=${'dock mode-' + mode + (overlay ? ' is-overlay' : '') + (thickness != null ? ' own-width' : '')} ...${hover}>
          ${letter}

          ${groups.map((g) => html`
            <div class="dock-group" key=${g.title} role="group" aria-label=${g.title}>
              ${mode === 'row' ? html`<div class="dock-group-title">${g.title}</div>` : html`<${Divider}/>`}
              ${g.items.map((it) => (it.items
                ? html`<${DockBranch} key=${it.id} item=${it} ctx=${ctx}/>`
                : html`<${DockLink} key=${it.to} item=${it} ctx=${ctx}/>`))}
            </div>`)}

          <${Divider}/>
          <${DockLink} item=${settingsItem} ctx=${ctx}/>
          <a href=${'#' + profileItem.to} class="dock-item dock-user" data-active="false"
            onClick=${(e) => { if (!plainClick(e)) return; e.preventDefault(); onNavigate(); navigate(profileItem.to); }}
            ...${hintOf(ctx, 'Профиль: ' + (username || ''))}>
            <${Icon} name="user" size=${ICON_SIZE[mode]}/><span class="dock-label">${profileItem.title}</span>
          </a>
          <button type="button" class="dock-item dock-logout" onClick=${onLogout} aria-label="Выйти" ...${hintOf(ctx, 'Выйти')}>
            <${Icon} name="logout" size=${ICON_SIZE[mode]}/><span class="dock-label">Выйти</span>
          </button>
          <button type="button" class="dock-item dock-toggle" onClick=${onToggle} aria-expanded=${expanded}
            aria-label=${toggleLabel} ...${hintOf(ctx, toggleLabel, 'dockExpand')}>
            <${Icon} name="grip" size=${ICON_SIZE[mode] - 2}/><span class="dock-label">${toggleLabel}</span>
          </button>
        </nav>

        ${showStrip && openItem ? html`
          <div ref=${strip} class=${'dock-strip strip-' + position + ' mode-' + (mode === 'tile' ? 'tile' : 'icon')} role="group"
            aria-label=${openItem.title} style=${vertical ? { top: stripOffset + 'px' } : { left: stripOffset + 'px' }} ...${hover}>
            ${openItem.items.map((sub) => html`<${SubLink} key=${sub.to} item=${sub} branch=${openItem.id} ctx=${ctx}/>`)}
          </div>` : null}
      </div>
    </div>`;
}
