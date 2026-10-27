// Провайдер модели: куда ходит backend (LM Studio / OpenAI-совместимый или mock), какая модель, проверка статуса.
// Сохранённые здесь настройки использует игра; ключ API не сохраняется — он в .env (LLM_API_KEY) или вводится
// только для проверки в этой вкладке.

import { html, useState, useEffect } from '../html.js';
import { api } from '../api.js';
import { useLoad } from '../hooks.js';
import { can } from '../session.js';
import { usePageKeys } from '../pageKeys.js';
import { Field, Input, Select, ActionButton, ErrorText, Loading, Badge } from '../components/ui.js';
import { toast } from '../components/Toast.js';
import { PasswordInput } from './Login.js';
import { ScreenHead, StatusTile } from './common.js';
import { modelTone, modelMeta } from './Overview.js';

const MODES = [['openai_compatible', 'OpenAI-совместимый (LM Studio)'], ['mock', 'Mock — без модели']];

export function ModelScreen() {
  const canSave = can('llm.settings');
  const info = useLoad(() => api('/v1/llm/provider'));
  const [form, setForm] = useState(null);
  const [apiKey, setApiKey] = useState('');
  const [status, setStatus] = useState(null);
  const [models, setModels] = useState([]);

  useEffect(() => {
    if (info.data && !form) setForm({ ...info.data.provider });
  }, [info.data]);

  const probe = () => ({
    mode: form.mode, base_url: form.base_url, path: form.path, model: form.model, temperature: Number(form.temperature) || 0,
    ...(apiKey ? { api_key: apiKey } : {}),
  });
  const check = async () => {
    setStatus(null);
    setStatus(await api('/v1/llm/provider/status', { method: 'POST', body: probe() }));
  };
  const loadModels = async () => {
    const r = await api('/v1/llm/provider/models', { method: 'POST', body: probe() });
    setModels(r.models || []);
    if (!form.model && r.models?.length) setForm({ ...form, model: r.models[0].id });
    toast(`Моделей: ${r.models?.length || 0}`);
  };
  const save = async () => {
    const body = { mode: form.mode, base_url: form.base_url, path: form.path, model: form.model, temperature: Number(form.temperature) || 0 };
    const r = await api('/v1/llm/provider', { method: 'PUT', body });
    info.setData(r);
    toast('Сохранено: игра и отладочный чат используют эти настройки');
    await check();
  };

  useEffect(() => {
    if (form) check().catch(() => {});
  }, [!!form]);
  usePageKeys({ refresh: () => check().catch(toast.error), send: () => (canSave && form ? (save().catch(toast.error), true) : false) });

  if (info.error) return html`<div class="screen"><${ErrorText} error=${info.error}/></div>`;
  if (!form) return html`<div class="screen"><${Loading}/></div>`;
  const set = (k) => (v) => setForm({ ...form, [k]: v });
  const m = modelTone(status);
  const d = info.data;

  return html`
    <div class="screen">
      <${ScreenHead} title="Провайдер и статус модели" desc="Сохранённые здесь настройки использует игра и отладочный чат."
        actions=${html`<${ActionButton} className="btn" onClick=${check}>Проверить<//>`}/>
      <div class="status-grid">
        <${StatusTile} label="Сервер модели" value=${status ? (status.online ? 'В СЕТИ' : 'НЕДОСТУПЕН') : 'проверка…'}
          tone=${status ? (status.online ? 'ok' : 'bad') : ''} meta=${form.mode === 'mock' ? 'mock — ответы без модели' : form.base_url}/>
        <${StatusTile} label="Выбранная модель" value=${m.value} tone=${m.tone} meta=${modelMeta(status)}/>
        <${StatusTile} label="Источник настроек" value=${d.saved_in_panel ? 'ПАНЕЛЬ' : '.ENV'}
          meta=${d.api_key_from_env ? 'ключ API — из .env' : 'ключ API не задан'}/>
      </div>

      <section class="card">
        <div class="form-grid">
          <${Field} label="Режим"><${Select} value=${form.mode} onChange=${set('mode')} options=${MODES}/><//>
          <${Field} label="Temperature" hint="0 — строго, 2 — максимально свободно">
            <${Input} type="number" min="0" max="2" step="0.1" value=${form.temperature} onInput=${set('temperature')}/><//>
          <${Field} label="Base URL" hint=${'Разрешённые адреса: ' + ((d.provider_allowlist || []).join(', ') || 'любые (ALLOW_UNSAFE_PROVIDER_URLS)')}>
            <${Input} value=${form.base_url} onInput=${set('base_url')} inputmode="url" disabled=${form.mode === 'mock'}/><//>
          <${Field} label="Путь чата"><${Input} value=${form.path} onInput=${set('path')} disabled=${form.mode === 'mock'}/><//>
          <${Field} label="Модель" hint="Кнопка «Получить модели» подставит список с сервера">
            <${Input} value=${form.model} onInput=${set('model')} list="model-options" disabled=${form.mode === 'mock'}/>
            <datalist id="model-options">${models.map((x) => html`<option key=${x.id} value=${x.id}/>`)}</datalist><//>
          <${Field} label="Ключ API для проверки" hint="Не сохраняется: в игре используется LLM_API_KEY из .env">
            <${PasswordInput} value=${apiKey} onInput=${setApiKey} autocomplete="off" placeholder=${d.api_key_from_env ? 'задан в .env' : 'sk-… (если сервер требует)'}/><//>
        </div>
        <div class="btn-row">
          ${canSave ? html`<${ActionButton} className="btn primary" onClick=${save}>Сохранить для игры<//>` : html`<${Badge}>только просмотр: нужно право llm.settings<//>`}
          <${ActionButton} className="btn" onClick=${loadModels} disabled=${form.mode === 'mock'}>Получить модели<//>
          <button type="button" class="btn" onClick=${() => setForm({ ...d.env_default })}>Как в .env</button>
        </div>
      </section>
      ${status?.models?.length ? html`
        <section class="card">
          <h3>Модели на сервере</h3>
          <div class="table-wrap"><table class="tbl">
            <thead><tr><th>Модель</th><th>Тип</th><th>Загружена</th><th>Контекст</th></tr></thead>
            <tbody>${status.models.map((x) => html`<tr key=${x.key}><td><code>${x.key}</code></td><td>${x.type || '—'}</td>
              <td>${x.loaded ? html`<${Badge} tone="ok">да<//>` : 'нет'}</td><td>${x.context_length || x.max_context_length || '—'}</td></tr>`)}</tbody>
          </table></div>
        </section>` : null}
    </div>`;
}
