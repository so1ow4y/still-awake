// Элементы форм и мелочи интерфейса в стиле pdd-trainer: поле с подписью, переключатель-сегмент, флажок,
// значок-метка, ошибка, пустой список, окно подтверждения, кнопка «скопировать».

import { html, useState, useEffect, useRef } from '../html.js';
import { Icon } from '../icons.js';
import { toast } from './Toast.js';

export function Field({ label, hint, error, children, wide = false }) {
  return html`
    <label class=${'field' + (wide ? ' wide' : '')}>
      ${label ? html`<span class="field-label">${label}</span>` : null}
      ${children}
      ${error ? html`<span class="field-error">${error}</span>` : hint ? html`<span class="field-hint">${hint}</span>` : null}
    </label>`;
}

export function Input({ value, onInput, type = 'text', ...rest }) {
  return html`<input class="input" type=${type} value=${value ?? ''} onInput=${(e) => onInput?.(e.target.value)} ...${rest}/>`;
}

export function Textarea({ value, onInput, rows = 4, mono = false, ...rest }) {
  return html`<textarea class=${'input textarea' + (mono ? ' mono' : '')} rows=${rows} value=${value ?? ''}
    onInput=${(e) => onInput?.(e.target.value)} ...${rest}></textarea>`;
}

export function Select({ value, onChange, options, ...rest }) {
  return html`<select class="input" value=${value} onChange=${(e) => onChange(e.target.value)} ...${rest}>
    ${options.map(([v, label]) => html`<option key=${v} value=${v}>${label}</option>`)}
  </select>`;
}

export function Choice({ value, options, onChange, label }) {
  return html`
    <div class="seg" role="radiogroup" aria-label=${label}>
      ${options.map(([v, text]) => html`<button type="button" key=${String(v)} role="radio" aria-checked=${value === v}
        class=${'seg-btn' + (value === v ? ' selected' : '')} onClick=${() => onChange(v)}>${text}</button>`)}
    </div>`;
}

export function Check({ label, hint, checked, onChange, disabled = false }) {
  return html`
    <label class=${'check-row' + (disabled ? ' disabled' : '')}>
      <input type="checkbox" checked=${checked} disabled=${disabled} onChange=${(e) => onChange(e.target.checked)}/>
      <span><span class="check-label">${label}</span>${hint ? html`<span class="check-hint">${hint}</span>` : null}</span>
    </label>`;
}

export function Badge({ children, tone = '', title }) {
  return html`<span class=${'badge' + (tone ? ' ' + tone : '')} title=${title}>${children}</span>`;
}

export function ErrorText({ error }) {
  if (!error) return null;
  return html`<p class="error-text" role="alert"><${Icon} name="warn" size=${16}/> ${error.message || String(error)}</p>`;
}

export function Empty({ children = 'Пусто.' }) {
  return html`<div class="empty">${children}</div>`;
}

export function Loading() {
  return html`<div class="loading" aria-busy="true"><span></span><span></span><span></span></div>`;
}

export function SearchBox({ value, onInput, placeholder = 'Поиск', onEnter }) {
  return html`
    <div class="search-field">
      <${Icon} name="search" size=${16}/>
      <input type="search" data-search-input value=${value} placeholder=${placeholder} aria-label=${placeholder}
        onInput=${(e) => onInput(e.target.value)} onKeyDown=${(e) => { if (e.key === 'Enter') onEnter?.(); }}/>
    </div>`;
}

/** Кнопка с ожиданием: пока действие идёт — неактивна, ошибка уходит в тост. */
export function ActionButton({ onClick, children, className = 'btn', disabled = false, title, type = 'button' }) {
  const [busy, setBusy] = useState(false);
  const run = async (e) => {
    if (busy) return;
    setBusy(true);
    try {
      await onClick?.(e);
    } catch (err) {
      toast.error(err);
    } finally {
      setBusy(false);
    }
  };
  return html`<button type=${type} class=${className + (busy ? ' busy' : '')} disabled=${disabled || busy} title=${title} onClick=${run}>${children}</button>`;
}

export function CopyButton({ text, label = 'Скопировать' }) {
  return html`<button type="button" class="btn small" onClick=${async () => {
    try {
      await navigator.clipboard.writeText(text);
      toast('Скопировано');
    } catch {
      toast('Не удалось скопировать — выделите вручную');
    }
  }}><${Icon} name="copy" size=${15}/> ${label}</button>`;
}

/**
 * Окно поверх страницы: подтверждение опасного действия, ввод причины. Esc и клик мимо закрывают.
 * onConfirm может вернуть промис — пока он идёт, кнопка неактивна; ошибка остаётся в окне.
 */
export function Dialog({ title, children, confirmLabel = 'Подтвердить', danger = false, onConfirm, onClose, disabled = false }) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState(null);
  const box = useRef(null);
  useEffect(() => {
    const onKey = (e) => {
      if (e.key === 'Escape') {
        e.preventDefault();
        onClose();
      }
    };
    window.addEventListener('keydown', onKey, true);
    box.current?.querySelector('input, textarea, button.primary, button.danger')?.focus();
    return () => window.removeEventListener('keydown', onKey, true);
  }, []);
  const confirm = async () => {
    setBusy(true);
    setError(null);
    try {
      await onConfirm();
      onClose();
    } catch (err) {
      setError(err);
    } finally {
      setBusy(false);
    }
  };
  return html`
    <div class="modal-shade" onClick=${onClose}>
      <div ref=${box} class="modal dialog" role="dialog" aria-label=${title} onClick=${(e) => e.stopPropagation()}>
        <header class="modal-head"><h2>${title}</h2>
          <button type="button" class="icon-btn" onClick=${onClose} aria-label="Закрыть (Esc)"><${Icon} name="close"/></button></header>
        <div class="dialog-body">${children}</div>
        <${ErrorText} error=${error}/>
        <div class="btn-row end">
          <button type="button" class="btn" onClick=${onClose}>Отмена</button>
          <button type="button" class=${'btn ' + (danger ? 'danger' : 'primary')} disabled=${busy || disabled} onClick=${confirm}>${confirmLabel}</button>
        </div>
      </div>
    </div>`;
}

/** Блок с кодом/JSON, сворачивается. */
export function CodeBlock({ title, value, open = false }) {
  const text = typeof value === 'string' ? value : JSON.stringify(value, null, 2);
  return html`<details class="code-block" open=${open}><summary>${title}</summary><pre>${text}</pre></details>`;
}
