// SQL-консоль SQLite: схема и запросы на чтение (db.read). Изменяющий SQL — только с правом db.write,
// при ADMIN_ALLOW_SQL_WRITES=true на сервере и с флажком на конкретный запрос.

import { html, useState } from '../html.js';
import { api } from '../api.js';
import { useLoad } from '../hooks.js';
import { can } from '../session.js';
import { usePageKeys } from '../pageKeys.js';
import { bindingOf } from '../keys.js';
import { Textarea, Check, ActionButton, ErrorText, Loading, Empty } from '../components/ui.js';
import { Keys } from '../components/Shortcuts.js';
import { ScreenHead } from './common.js';

const SAMPLE = `SELECT id, installation_id, session_id, model, prompt_tokens, completion_tokens, latency_ms, status, created_at
FROM llm_requests
ORDER BY id DESC
LIMIT 50;`;

export function DatabaseScreen() {
  const schema = useLoad(() => api('/v1/admin/db/schema'));
  const [sql, setSql] = useState(SAMPLE);
  const [write, setWrite] = useState(false);
  const [result, setResult] = useState(null);
  const [error, setError] = useState(null);
  const writesOn = !!schema.data?.sql_writes_enabled && can('db.write');

  const run = async () => {
    setError(null);
    try {
      setResult(await api('/v1/admin/db/query', { method: 'POST', body: { sql, allow_write: write && writesOn } }));
      if (write) schema.reload();
    } catch (err) {
      setError(err);
      setResult(null);
    }
  };
  usePageKeys({ send: () => { run(); return true; }, refresh: () => schema.reload() });

  const tables = (schema.data?.objects || []).filter((o) => o.type === 'table');
  return html`
    <div class="screen wide-screen">
      <${ScreenHead} title="База данных" desc="SQLite напрямую: удобно для отладки, осторожно с изменениями."/>
      <div class="db-layout">
        <section class="card">
          <${Textarea} value=${sql} onInput=${setSql} rows=${10} mono=${true} spellcheck="false"/>
          <${Check} label="Разрешить изменяющий SQL для этого запроса" checked=${write && writesOn} onChange=${setWrite} disabled=${!writesOn}
            hint=${writesOn ? 'INSERT, UPDATE, DELETE и DDL уйдут в базу как есть.' : 'Нужны право db.write и ADMIN_ALLOW_SQL_WRITES=true на сервере.'}/>
          <div class="btn-row">
            <${ActionButton} className="btn primary" onClick=${run}>Выполнить<//>
            <span class="muted small"><${Keys} combo=${bindingOf('send')} ru=${false}/></span>
          </div>
          <${ErrorText} error=${error}/>
          ${result ? html`<${SqlResult} r=${result}/>` : null}
        </section>
        <section class="card">
          <h3>Таблицы</h3>
          ${schema.loading && !schema.data ? html`<${Loading}/>` : tables.length === 0 ? html`<${Empty}/>` : tables.map((t) => html`
            <details class="schema-item" key=${t.name}>
              <summary><button type="button" class="link-btn" onClick=${(e) => { e.preventDefault(); setSql(`SELECT * FROM ${t.name} ORDER BY rowid DESC LIMIT 50;`); }}>${t.name}</button></summary>
              <pre>${t.sql}</pre>
            </details>`)}
        </section>
      </div>
    </div>`;
}

function SqlResult({ r }) {
  if (r.kind === 'exec') return html`<p class="notice">Изменено строк: <b>${r.rows_affected || 0}</b> · последний id: <b>${r.last_insert_id || 0}</b></p>`;
  const cols = r.columns || [];
  const rows = r.rows || [];
  if (!cols.length) return html`<${Empty}>Пустой результат.<//>`;
  const cell = (v) => (v === null ? html`<span class="muted">NULL</span>` : typeof v === 'object' ? JSON.stringify(v) : String(v));
  return html`
    <p class="muted small">Строк: ${rows.length}</p>
    <div class="table-wrap sql-result"><table class="tbl">
      <thead><tr>${cols.map((c) => html`<th key=${c}>${c}</th>`)}</tr></thead>
      <tbody>${rows.map((row, i) => html`<tr key=${i}>${cols.map((c) => html`<td key=${c}>${cell(row[c])}</td>`)}</tr>`)}</tbody>
    </table></div>`;
}
