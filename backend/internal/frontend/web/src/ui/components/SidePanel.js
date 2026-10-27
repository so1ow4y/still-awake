// Панель справа в духе SIEM — перенос components/side-panel.tsx из license-store: клик по строке списка открывает
// карточку записи, а сам список остаётся живым — можно щёлкать соседние строки (или W/S), панель переключается.
//
// Не модальная: фон не затемняется. По умолчанию ложится поверх списка; в настройках можно включить «сдвигать
// страницу» — тогда на широком экране страница отодвигается на ширину панели (--side-panel-width читает каркас).
// На узком экране панель всегда поверх во всю ширину. Ширина задаётся в настройках и тянется мышью за левый край.

import { html, useEffect, useRef, useState } from '../html.js';
import { Icon } from '../icons.js';
import { getPrefs, setPrefs, SIDE_PANEL_MIN } from '../prefs.js';
import { useStore } from '../hooks.js';
import { Keys } from './Shortcuts.js';
import { bindingOf, shortcutsOn } from '../keys.js';

const maxWidth = () => Math.max(SIDE_PANEL_MIN, Math.round(innerWidth * 0.85));

/** Открыто ли окно поверх — тогда Esc его, а не панели. */
const modalOpen = () => !!document.querySelector('.modal-shade');

export function SidePanel({ open = true, onClose, title, subtitle, badges, actions, children }) {
  useStore();
  const { sidePanelWidth, sidePanelResizable } = getPrefs();
  const [dragWidth, setDragWidth] = useState(null);
  const width = Math.min(dragWidth ?? sidePanelWidth, maxWidth());
  const live = useRef(onClose);
  live.current = onClose;

  // Каркас отодвигает страницу на ширину панели (если включено и экран широкий).
  useEffect(() => {
    if (!open) return undefined;
    const root = document.documentElement.style;
    root.setProperty('--side-panel-width', width + 'px');
    return () => root.removeProperty('--side-panel-width');
  }, [open, width]);

  useEffect(() => {
    if (!open) return undefined;
    const onKey = (e) => {
      if (e.key === 'Escape' && !e.defaultPrevented && !modalOpen()) {
        e.preventDefault();
        live.current();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open]);

  const startDrag = (e) => {
    e.preventDefault();
    let last = width;
    const onMove = (ev) => {
      last = Math.min(maxWidth(), Math.max(SIDE_PANEL_MIN, innerWidth - ev.clientX));
      setDragWidth(last);
    };
    const onUp = () => {
      window.removeEventListener('pointermove', onMove);
      window.removeEventListener('pointerup', onUp);
      setPrefs({ sidePanelWidth: Math.round(last) });
      setDragWidth(null);
    };
    window.addEventListener('pointermove', onMove);
    window.addEventListener('pointerup', onUp);
  };

  if (!open) return null;
  const hints = shortcutsOn();
  return html`
    <aside class="side-panel" style=${{ '--panel-w': width + 'px' }} aria-label=${typeof title === 'string' ? title : undefined}>
      ${sidePanelResizable ? html`<div class="side-panel-grip" role="separator" aria-orientation="vertical" aria-label="Изменить ширину панели" onPointerDown=${startDrag}></div>` : null}
      <header class="side-panel-head">
        <div class="side-panel-titles">
          <div class="side-panel-title-row"><h2>${title}</h2>${badges}</div>
          ${subtitle ? html`<div class="muted small">${subtitle}</div>` : null}
        </div>
        <div class="side-panel-actions">
          ${actions}
          <button type="button" class="icon-btn" onClick=${onClose} title="Закрыть (Esc)" aria-label="Закрыть"><${Icon} name="close" size=${20}/></button>
        </div>
      </header>
      <div class="side-panel-body">${children}</div>
      ${hints ? html`
        <footer class="side-panel-foot">
          <span><${Keys} combo=${bindingOf('listUp')} ru=${false} empty="—"/> <${Keys} combo=${bindingOf('listDown')} ru=${false} empty="—"/> соседняя строка</span>
          <span><${Keys} combo=${bindingOf('listClose')} ru=${false} empty="—"/> / <${Keys} combo="Escape" ru=${false}/> закрыть</span>
        </footer>` : null}
    </aside>`;
}

/** Раздел внутри панели: заголовок, подсказка, содержимое. */
export function PanelSection({ title, description, actions, children }) {
  return html`
    <section class="panel-section">
      <div class="panel-section-head">
        <div><h3>${title}</h3>${description ? html`<p class="muted small">${description}</p>` : null}</div>
        ${actions}
      </div>
      ${children}
    </section>`;
}

/** Сетка «поле — значение». */
export function Facts({ rows }) {
  return html`<dl class="facts">${rows.filter(Boolean).map(([k, v]) => html`<dt>${k}</dt><dd>${v ?? '—'}</dd>`)}</dl>`;
}
