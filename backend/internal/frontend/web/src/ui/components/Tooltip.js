// Подсказки дока (TooltipLayer из pdd-trainer / Kingdom, components/Popup.js): любому элементу достаточно атрибутов
// data-hint="подпись" (+ data-hint-side, data-hint-kbd). Слой один на приложение; на сенсорном экране подсказок нет.

import { html, useState, useEffect, useLayoutEffect, useRef } from '../html.js';

const POPUP_OFFSET = 14;
const EDGE = 8;

function place(side, r, w, h) {
  let left;
  let top;
  if (side === 'right' || side === 'left') {
    left = side === 'right' ? r.right + POPUP_OFFSET : r.left - POPUP_OFFSET - w;
    top = r.top + r.height / 2 - h / 2;
  } else {
    top = side === 'bottom' ? r.bottom + POPUP_OFFSET : r.top - POPUP_OFFSET - h;
    left = r.left + r.width / 2 - w / 2;
  }
  left = Math.max(EDGE, Math.min(left, innerWidth - w - EDGE));
  top = Math.max(EDGE, Math.min(top, innerHeight - h - EDGE));
  return { left, top };
}

export function TooltipLayer() {
  const [tip, setTip] = useState(null);
  const box = useRef(null);
  const [pos, setPos] = useState(null);

  useEffect(() => {
    let current = null;
    const show = (el) => {
      current = el;
      setPos(null);
      setTip({ label: el.dataset.hint, kbd: el.dataset.hintKbd || '', side: el.dataset.hintSide || 'right', r: el.getBoundingClientRect() });
    };
    const hide = () => {
      if (!current) return;
      current = null;
      setTip(null);
    };
    const over = (e) => {
      if (e.pointerType === 'touch') return;
      const el = e.target.closest?.('[data-hint]');
      if (el && el !== current) show(el);
      else if (!el && current) hide();
    };
    const out = (e) => {
      if (current && !(e.relatedTarget && current.contains(e.relatedTarget))) hide();
    };
    const focusIn = (e) => {
      const el = e.target.closest?.('[data-hint]');
      if (el && el.matches(':focus-visible')) show(el);
    };
    const key = (e) => e.key === 'Escape' && hide();
    document.addEventListener('pointerover', over);
    document.addEventListener('pointerout', out);
    document.addEventListener('focusin', focusIn);
    document.addEventListener('focusout', hide);
    document.addEventListener('pointerdown', hide, true);
    document.addEventListener('keydown', key, true);
    window.addEventListener('scroll', hide, true);
    window.addEventListener('hashchange', hide);
    return () => {
      document.removeEventListener('pointerover', over);
      document.removeEventListener('pointerout', out);
      document.removeEventListener('focusin', focusIn);
      document.removeEventListener('focusout', hide);
      document.removeEventListener('pointerdown', hide, true);
      document.removeEventListener('keydown', key, true);
      window.removeEventListener('scroll', hide, true);
      window.removeEventListener('hashchange', hide);
    };
  }, []);

  useLayoutEffect(() => {
    if (!tip || !box.current) return;
    setPos(place(tip.side, tip.r, box.current.offsetWidth, box.current.offsetHeight));
  }, [tip]);

  if (!tip) return null;
  const arrow = pos && (tip.side === 'left' || tip.side === 'right'
    ? { top: tip.r.top + tip.r.height / 2 - pos.top + 'px' }
    : { left: tip.r.left + tip.r.width / 2 - pos.left + 'px' });
  return html`
    <div ref=${box} class=${'tooltip side-' + tip.side} role="tooltip"
      style=${pos ? { left: pos.left + 'px', top: pos.top + 'px' } : { left: '-9999px', top: '0px', visibility: 'hidden' }}>
      <span>${tip.label}</span>${tip.kbd ? html`<kbd class="tooltip-kbd">${tip.kbd}</kbd>` : null}
      <i class="tooltip-arrow" style=${arrow || {}}></i>
    </div>`;
}
