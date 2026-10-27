// Обозреватель списка в духе SIEM — перенос components/Explorer.js из Kingdom (а туда — log-explorer license-store):
// строка «поле:значение» с кнопками полей и подсказкой синтаксиса, период, гистограмма по времени, таблица, где
// любое значение — ссылка «добавить условие в строку», справа — топ значений по полям (+ добавить, − исключить),
// страницы по 100. Строка открывает карточку справа (SidePanel), W/S ходят по строкам, как раньше.
//
// Данные экран загружает сам (последние N записей), фильтрует — браузер: так любое поле и любая связка условий
// работают одинаково, а клик по значению сразу сужает выборку без запроса к серверу.
//
// fields — [{ name, aliases?, get(row) → значение или массив, numeric?, facet?(row) — что считать в топе, если не get }];
// text(row) — строки для поиска без поля;
// time(row) — момент строки (ISO) для периода и гистограммы; facets — поля для топов; columns — [{ title, render }].

import { html, useState, useEffect, useMemo } from '../html.js';
import { Icon } from '../icons.js';
import { parseQuery, evaluate, textMatch, textContains, numberMatch, withTerm } from '../query.js';
import { useListKeys } from '../pageKeys.js';
import { ErrorText, Loading } from './ui.js';

const PAGE = 100;
const HOUR = 3600000;
const SYNTAX = '· * — подстановка · пробел или AND — оба условия · OR или | — любое · NOT или - — исключить · ( ) — группировка · "фраза" · числа: >5, <=10';

export const RANGES = [
  ['all', 'За всё время'],
  ['1h', 'Последний час'],
  ['24h', 'Последние 24 часа'],
  ['7d', 'Последние 7 дней'],
  ['30d', 'Последние 30 дней'],
];
const RANGE_MS = { '1h': HOUR, '24h': 24 * HOUR, '7d': 7 * 24 * HOUR, '30d': 30 * 24 * HOUR };

const WEEKDAYS = ['Вс', 'Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб'];

/** Местная дата «2026-10-27» — для полей «дата:» (маска «2026-10*» — месяц). */
export function localDate(iso) {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  const p = (n) => String(n).padStart(2, '0');
  return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`;
}

/** День недели «Пн» — для полей «день:». */
export function weekday(iso) {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? '' : WEEKDAYS[d.getDay()];
}

const valuesOf = (field, row) => {
  const v = field.get(row);
  return (Array.isArray(v) ? v : [v]).filter((x) => x != null && x !== '');
};

function interval(fromMs, toMs) {
  const span = toMs - fromMs;
  if (span <= 3 * HOUR) return { ms: 5 * 60000, label: '5 минут' };
  if (span <= 2 * 24 * HOUR) return { ms: HOUR, label: '1 час' };
  if (span <= 14 * 24 * HOUR) return { ms: 6 * HOUR, label: '6 часов' };
  if (span <= 90 * 24 * HOUR) return { ms: 24 * HOUR, label: '1 день' };
  return { ms: 7 * 24 * HOUR, label: '7 дней' };
}

/** Столбики по времени строк; шаги — по местному времени (день — с полуночи). */
function histogram(times, fromMs, nowMs = Date.now()) {
  let start = fromMs ?? (times.length ? Math.min(...times) : nowMs - 24 * HOUR);
  if (nowMs - start < HOUR) start = nowMs - HOUR;
  const step = interval(start, nowMs);
  const offset = -new Date(start).getTimezoneOffset() * 60000;
  const align = (ms) => Math.floor((ms + offset) / step.ms) * step.ms - offset;
  const first = align(start);
  const n = Math.min(400, Math.floor((nowMs - first) / step.ms) + 1);
  const buckets = Array.from({ length: n }, (_, i) => ({ start: first + i * step.ms, count: 0 }));
  for (const ms of times) {
    const i = Math.floor((align(ms) - first) / step.ms);
    if (i >= 0 && i < n) buckets[i].count++;
  }
  return { label: step.label, ms: step.ms, buckets };
}

function Histogram({ hist, title }) {
  const max = Math.max(1, ...hist.buckets.map((b) => b.count));
  const total = hist.buckets.reduce((s, b) => s + b.count, 0);
  const label = (ms) => new Date(ms).toLocaleString('ru-RU', hist.ms < 24 * HOUR
    ? { day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit' } : { day: 'numeric', month: 'short', year: 'numeric' });
  return html`
    <div class="ex-card ex-hist">
      <div class="ex-card-head"><span>${title}</span><span>шаг ${hist.label}</span></div>
      ${!total ? html`<p class="ex-empty">Ничего не найдено.</p>` : html`
        <div class="ex-bars">
          ${hist.buckets.map((b) => html`
            <div class="ex-bar" key=${b.start} title=${`${label(b.start)} — ${b.count}`}>
              <i style=${{ height: (b.count / max) * 100 + '%' }}></i>
            </div>`)}
        </div>`}
    </div>`;
}

function Facets({ list, onFilter }) {
  return html`
    <aside class="ex-facets" aria-label="Топ значений">
      ${list.map((f) => {
        const max = Math.max(1, ...f.values.map((v) => v.count));
        return html`
          <div class="ex-card" key=${f.field}>
            <p class="ex-facet-title">Топ: ${f.field}</p>
            ${!f.values.length ? html`<p class="ex-empty small">—</p>` : null}
            <ul class="ex-facet-list">
              ${f.values.map((v) => html`
                <li key=${v.value} class="ex-facet">
                  <span class="ex-facet-bar" style=${{ width: (v.count / max) * 100 + '%' }}></span>
                  <button type="button" class="ex-facet-value" title=${v.value} onClick=${() => onFilter(f.field, v.value)}>${v.value}</button>
                  <span class="ex-facet-count">${v.count}</span>
                  <button type="button" class="ex-facet-op" aria-label="Фильтровать по значению" title="Фильтровать по значению"
                    onClick=${() => onFilter(f.field, v.value)}><${Icon} name="plus" size=${14}/></button>
                  <button type="button" class="ex-facet-op" aria-label="Исключить значение" title="Исключить значение"
                    onClick=${() => onFilter(f.field, v.value, true)}><${Icon} name="minus" size=${14}/></button>
                </li>`)}
            </ul>
          </div>`;
      })}
    </aside>`;
}

/**
 * Значение-ссылка: клик добавляет «поле:значение» в строку поиска (в таблице, в карточке справа, где угодно).
 * children — как показать (по умолчанию само значение).
 */
export function Value({ field, value, onFilter, children = null, mono = false }) {
  if (value == null || value === '') return html`<span class="muted">—</span>`;
  return html`<button type="button" class=${'ex-value' + (mono ? ' mono' : '')} title=${`Добавить в фильтр: ${field}:${value}`}
    onClick=${(e) => { e.stopPropagation(); onFilter?.(field, String(value)); }}>${children ?? String(value)}</button>`;
}

/**
 * q/onQuery — строка поиска снаружи (в адресе страницы), чтобы ссылка на выборку открывалась так же.
 * opened/onOpen/onClose — какая строка открыта в карточке справа. renderPanel — сама карточка.
 */
export function Explorer({
  rows, loading, error, fields, text, time, facets = [], columns, q = '', onQuery, placeholder, histogramTitle = 'Записи',
  opened = null, onOpen, onClose, rowId = (r) => r.id, note = null, emptyText = 'Ничего не найдено.',
}) {
  const [draft, setDraft] = useState(q);
  const [range, setRange] = useState('all');
  const [skip, setSkip] = useState(0);
  useEffect(() => setDraft(q), [q]);
  useEffect(() => setSkip(0), [q, range]);

  const byName = useMemo(() => new Map(fields.map((f) => [f.name, f])), [fields]);
  const aliases = useMemo(() => Object.fromEntries(fields.map((f) => [f.name, f.aliases || []])), [fields]);

  const res = useMemo(() => {
    let tree = null;
    let queryError = null;
    try {
      tree = parseQuery(q, aliases);
    } catch (e) {
      queryError = e.message;
    }
    const fromMs = RANGE_MS[range] ? Date.now() - RANGE_MS[range] : null;
    const match = (row, term) => {
      if (!term.field) return text(row).some((t) => textContains(t, term.value));
      const field = byName.get(term.field);
      return valuesOf(field, row).some((v) => (field.numeric && numberMatch(v, term.value)) || textMatch(v, term.value));
    };
    const found = queryError ? [] : (rows || []).filter((row) => {
      if (fromMs != null && !(Date.parse(time(row)) >= fromMs)) return false;
      return evaluate(tree, (term) => match(row, term));
    });
    const tops = facets.map((name) => {
      const field = byName.get(name);
      const counts = new Map();
      const show = field.facet ? { get: field.facet } : field;
      for (const row of found) for (const v of new Set(valuesOf(show, row).map(String))) counts.set(v, (counts.get(v) || 0) + 1);
      const values = [...counts].map(([value, count]) => ({ value, count }))
        .sort((a, b) => b.count - a.count || (a.value < b.value ? -1 : 1)).slice(0, 8);
      return { field: name, values };
    });
    const hist = histogram(found.map((r) => Date.parse(time(r))).filter(Number.isFinite), fromMs);
    return { found, tops, hist, queryError };
  }, [rows, q, range, aliases, byName]);

  const total = res.found.length;
  const page = res.found.slice(skip, skip + PAGE);
  const ids = page.map(rowId);
  const cursor = useListKeys({ ids, openedId: opened, onOpen, onClose });

  const apply = (next) => onQuery(next.trim());
  const addTerm = (field, value, negate = false) => onQuery(withTerm(q, field, value, negate));

  return html`
    <div class="explorer">
      <form class="ex-query" onSubmit=${(e) => { e.preventDefault(); apply(draft); }}>
        <div class="ex-query-row">
          <div class="search-field ex-search">
            <${Icon} name="search" size=${16}/>
            <input value=${draft} data-search-input placeholder=${placeholder} spellcheck="false" autocomplete="off"
              aria-label="Строка поиска" onInput=${(e) => setDraft(e.target.value)}/>
          </div>
          <select class="input ex-range" value=${range} aria-label="Период" onChange=${(e) => setRange(e.target.value)}>
            ${RANGES.map(([v, label]) => html`<option value=${v}>${label}</option>`)}
          </select>
          <button type="submit" class="btn primary">Найти</button>
          ${q || range !== 'all' ? html`<button type="button" class="btn ghost" onClick=${() => { setRange('all'); apply(''); }}>
            <${Icon} name="close" size=${16}/> Сбросить фильтры</button>` : null}
        </div>
        <div class="ex-fields">
          <span>Поля:</span>
          ${fields.map((f) => html`<button type="button" class="ex-field" key=${f.name} title=${f.hint}
            onClick=${() => setDraft((d) => `${d.trim()} ${f.name}:`.trim())}>${f.name}:</button>`)}
          <span class="ex-syntax">${SYNTAX}</span>
        </div>
      </form>

      ${res.queryError ? html`<p class="ex-error" role="alert">${res.queryError}</p>` : null}
      <${ErrorText} error=${error}/>

      <div class="ex-grid">
        <div class="ex-main">
          <${Histogram} hist=${res.hist} title=${histogramTitle + ' во времени'}/>
          ${loading && !rows ? html`<${Loading}/>` : html`
            <div class="ex-table-wrap">
              <table class="ex-table">
                <thead><tr>${columns.map((c) => html`<th key=${c.title} class=${c.className || ''}>${c.title}</th>`)}</tr></thead>
                <tbody>
                  ${!page.length ? html`<tr><td colSpan=${columns.length} class="ex-empty-row">${emptyText}</td></tr>` : null}
                  ${page.map((row) => {
                    const id = rowId(row);
                    return html`
                      <tr key=${id} data-row-id=${id} onClick=${() => (opened === id ? onClose() : onOpen(id))}
                        class=${'ex-row' + (opened === id ? ' active' : '') + (cursor === id ? ' cursor' : '')}>
                        ${columns.map((c) => html`<td key=${c.title} class=${c.className || ''}>${c.render(row, addTerm)}</td>`)}
                      </tr>`;
                  })}
                </tbody>
              </table>
            </div>`}
          <div class="ex-pager">
            <span>Найдено: ${total}${total > PAGE ? ` · ${skip + 1}–${Math.min(skip + PAGE, total)}` : ''}${note ? html` · <span class="muted">${note}</span>` : null}</span>
            <div class="ex-pager-buttons">
              <button type="button" class="btn small" disabled=${skip === 0} onClick=${() => setSkip(Math.max(0, skip - PAGE))}>Назад</button>
              <button type="button" class="btn small" disabled=${skip + PAGE >= total} onClick=${() => setSkip(skip + PAGE)}>Вперёд</button>
            </div>
          </div>
        </div>
        <${Facets} list=${res.tops} onFilter=${addTerm}/>
      </div>
    </div>`;
}
