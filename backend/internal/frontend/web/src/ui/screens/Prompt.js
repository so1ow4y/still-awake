// Системный промпт для отладки модели: редактор, версии (кто и зачем менял), сравнение с текущим, откат.
// Сохранение создаёт новую версию и сразу делает её активной — её получает и игра, и отладочный чат.

import { html, useState, useEffect, useMemo } from '../html.js';
import { api } from '../api.js';
import { useLoad } from '../hooks.js';
import { can } from '../session.js';
import { navigate } from '../router.js';
import { fmtDate, fmtNum } from '../format.js';
import { usePageKeys, useListKeys } from '../pageKeys.js';
import { Field, Input, Textarea, ActionButton, ErrorText, Loading, Badge, Empty, Dialog } from '../components/ui.js';
import { SidePanel, PanelSection, Facts } from '../components/SidePanel.js';
import { toast } from '../components/Toast.js';
import { ScreenHead } from './common.js';

export const DRAFT_KEY = 'sa.promptDraft';

/** Построчное сравнение (LCS): [{ type: 'same' | 'add' | 'del', text }]. Промпты короткие — квадратичного хватает. */
export function diffLines(a, b) {
  const x = a.split('\n');
  const y = b.split('\n');
  const n = x.length;
  const m = y.length;
  const lcs = Array.from({ length: n + 1 }, () => new Array(m + 1).fill(0));
  for (let i = n - 1; i >= 0; i--) for (let j = m - 1; j >= 0; j--) lcs[i][j] = x[i] === y[j] ? lcs[i + 1][j + 1] + 1 : Math.max(lcs[i + 1][j], lcs[i][j + 1]);
  const out = [];
  let i = 0;
  let j = 0;
  while (i < n && j < m) {
    if (x[i] === y[j]) { out.push({ type: 'same', text: x[i] }); i++; j++; }
    else if (lcs[i + 1][j] >= lcs[i][j + 1]) out.push({ type: 'del', text: x[i++] });
    else out.push({ type: 'add', text: y[j++] });
  }
  while (i < n) out.push({ type: 'del', text: x[i++] });
  while (j < m) out.push({ type: 'add', text: y[j++] });
  return out;
}

export function Diff({ from, to }) {
  const rows = useMemo(() => diffLines(from, to), [from, to]);
  if (from === to) return html`<p class="muted small">Совпадает.</p>`;
  return html`<pre class="diff">${rows.map((r, i) => html`<span key=${i} class=${'diff-' + r.type}>${r.type === 'add' ? '+ ' : r.type === 'del' ? '− ' : '  '}${r.text}\n</span>`)}</pre>`;
}

const tokensApprox = (text) => Math.ceil(text.length / 3.2);

export function PromptScreen() {
  const canEdit = can('llm.prompt');
  const prompt = useLoad(() => api('/v1/llm/prompt'));
  const versions = useLoad(() => api('/v1/llm/prompt/versions'));
  const [text, setText] = useState(null);
  const [comment, setComment] = useState('');
  const [opened, setOpened] = useState(null);
  const [resetAsk, setResetAsk] = useState(false);

  useEffect(() => {
    if (prompt.data && text === null) setText(prompt.data.text);
  }, [prompt.data]);

  const active = prompt.data?.active;
  const dirty = text !== null && prompt.data && text !== prompt.data.text;

  const save = async () => {
    if (!text?.trim()) throw new Error('Промпт пустой');
    const r = await api('/v1/llm/prompt', { method: 'POST', body: { text, comment } });
    prompt.setData(r);
    setText(r.text);
    setComment('');
    versions.reload();
    toast(`Сохранено как версия #${r.active.id} — активна`);
  };
  const activate = async (id) => {
    const r = await api(`/v1/llm/prompt/versions/${id}/activate`, { method: 'POST' });
    prompt.setData(r);
    setText(r.text);
    versions.reload();
    toast(id ? `Активна версия #${id}` : 'Активен промпт по умолчанию');
  };
  const tryInChat = () => {
    sessionStorage.setItem(DRAFT_KEY, text || '');
    navigate('/chat?draft=1');
  };

  const rows = versions.data || [];
  const cursor = useListKeys({
    ids: rows.map((v) => String(v.id)),
    openedId: opened ? String(opened.id) : null,
    onOpen: (id) => setOpened(rows.find((v) => String(v.id) === id) || null),
    onClose: () => setOpened(null),
  });
  usePageKeys({
    send: () => (canEdit && dirty ? (save().catch(toast.error), true) : false),
    refresh: () => { prompt.reload(); versions.reload(); },
  });

  if (prompt.error) return html`<div class="screen"><${ErrorText} error=${prompt.error}/></div>`;
  if (text === null) return html`<div class="screen"><${Loading}/></div>`;

  return html`
    <div class="screen">
      <${ScreenHead} title="Системный промпт"
        desc=${active ? `Активна версия #${active.id} от ${fmtDate(active.created_at)} (${active.author_name || '—'}).` : 'Сейчас действует промпт по умолчанию из кода сервера.'}
        actions=${html`
          <button type="button" class="btn" onClick=${tryInChat}>Проверить в чате</button>
          ${canEdit && active ? html`<button type="button" class="btn" onClick=${() => setResetAsk(true)}>По умолчанию</button>` : null}`}/>

      <section class="card">
        <div class="prompt-meta">
          ${dirty ? html`<${Badge} tone="warn">не сохранено<//>` : html`<${Badge} tone="ok">${active ? 'версия #' + active.id : 'по умолчанию'}<//>`}
          <span class="muted small">${fmtNum(text.length)} символов · ${text.split('\n').length} строк · ≈ ${fmtNum(tokensApprox(text))} токенов</span>
        </div>
        <${Textarea} value=${text} onInput=${setText} rows=${14} mono=${true} disabled=${!canEdit} spellcheck="false"/>
        ${canEdit ? html`
          <div class="prompt-save">
            <${Field} label="Что изменилось" hint="Комментарий к версии — потом проще найти, что сломало ответы"><${Input} value=${comment} onInput=${setComment} maxlength="300"/><//>
            <div class="btn-row">
              <${ActionButton} className="btn primary" onClick=${save} disabled=${!dirty}>Сохранить версию<//>
              <button type="button" class="btn" disabled=${!dirty} onClick=${() => setText(prompt.data.text)}>Отменить правки</button>
            </div>
          </div>` : html`<p class="muted small">Только просмотр: для правки нужно право llm.prompt.</p>`}
        ${dirty ? html`<details class="code-block"><summary>Отличия от активной версии</summary><${Diff} from=${prompt.data.text} to=${text}/></details>` : null}
      </section>

      ${prompt.data.guard ? html`
        <section class="card">
          <h3>Правила сервера</h3>
          <p class="muted small">Дописываются после промпта в каждом запросе и не редактируются: защищают от попыток игрока переписать роль.
            Ситуация ночи и задача хода идут отдельным системным сообщением в конце — их видно в «Проверить в чате» → «Собрать запрос».</p>
          <pre class="prompt-text">${prompt.data.guard}</pre>
        </section>` : null}

      <section class="card">
        <h3>Версии</h3>
        <${ErrorText} error=${versions.error}/>
        ${rows.length === 0 ? html`<${Empty}>Версий ещё нет — первая появится после сохранения.<//>` : html`
          <div class="table-wrap"><table class="tbl rows">
            <thead><tr><th>№</th><th>Комментарий</th><th>Автор</th><th>Когда</th><th></th></tr></thead>
            <tbody>${rows.map((v) => html`
              <tr key=${v.id} data-row-id=${String(v.id)} onClick=${() => setOpened(opened?.id === v.id ? null : v)}
                class=${(opened?.id === v.id ? 'active ' : '') + (cursor === String(v.id) ? 'cursor' : '')}>
                <td>#${v.id}</td><td>${v.comment || html`<span class="muted">—</span>`}</td><td>${v.author_name || '—'}</td>
                <td>${fmtDate(v.created_at)}</td><td>${active?.id === v.id ? html`<${Badge} tone="ok">активна<//>` : null}</td>
              </tr>`)}</tbody>
          </table></div>`}
      </section>

      ${opened ? html`
        <${SidePanel} title=${'Версия #' + opened.id} subtitle=${fmtDate(opened.created_at) + ' · ' + (opened.author_name || '—')}
          badges=${active?.id === opened.id ? html`<${Badge} tone="ok">активна<//>` : null} onClose=${() => setOpened(null)}>
          <${Facts} rows=${[['Комментарий', opened.comment || '—'], ['Длина', `${fmtNum(opened.text.length)} символов`]]}/>
          ${canEdit ? html`<div class="btn-row">
            ${active?.id !== opened.id ? html`<${ActionButton} className="btn primary" onClick=${() => activate(opened.id)}>Сделать активной<//>` : null}
            <button type="button" class="btn" onClick=${() => { setText(opened.text); toast('Текст версии в редакторе'); }}>В редактор</button>
          </div>` : null}
          <${PanelSection} title="Отличия от активной"><${Diff} from=${prompt.data.text} to=${opened.text}/><//>
          <${PanelSection} title="Текст"><pre class="prompt-text">${opened.text}</pre><//>
        <//>` : null}

      ${resetAsk ? html`
        <${Dialog} title="Вернуть промпт по умолчанию?" confirmLabel="Вернуть" onClose=${() => setResetAsk(false)} onConfirm=${() => activate(0)}>
          <p>Игра и чат начнут получать промпт из кода сервера. Версии останутся в истории — любую можно снова сделать активной.</p>
        <//>` : null}
    </div>`;
}
