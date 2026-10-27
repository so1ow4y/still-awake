// Обзор: состояние backend и модели, счётчики, быстрые переходы в доступные разделы.

import { html } from '../html.js';
import { api } from '../api.js';
import { useLoad } from '../hooks.js';
import { canAny, me } from '../session.js';
import { allTargets } from '../nav.js';
import { Link } from '../router.js';
import { Icon } from '../icons.js';
import { fmtNum } from '../format.js';
import { usePageKeys } from '../pageKeys.js';
import { ErrorText } from '../components/ui.js';
import { ScreenHead, StatusTile, Stat } from './common.js';

const LLM_VIEW = ['llm.settings', 'llm.debug', 'llm.prompt'];

export function modelTone(st) {
  if (!st) return { value: 'проверка…', tone: '' };
  if (!st.online) return { value: 'НЕДОСТУПНА', tone: 'bad' };
  if (!st.selected_model) return { value: 'модель не выбрана', tone: 'warn' };
  if (!st.model_found) return { value: 'НЕ НАЙДЕНА', tone: 'bad' };
  if (st.model_loaded === false) return { value: 'НЕ ЗАГРУЖЕНА', tone: 'warn' };
  return { value: st.model_loaded ? 'ЗАГРУЖЕНА' : 'ДОСТУПНА', tone: 'ok' };
}

export function modelMeta(st) {
  if (!st) return '';
  if (st.error) return st.error;
  return [st.selected_model, st.context_length && `контекст ${st.context_length}`, st.max_context_length && `макс. ${st.max_context_length}`, st.note]
    .filter(Boolean).join(' · ');
}

export function OverviewScreen() {
  const llm = canAny(LLM_VIEW);
  const health = useLoad(() => api('/healthz'));
  const overview = useLoad(() => api('/v1/admin/overview'));
  const status = useLoad(() => (llm ? api('/v1/llm/provider/status', { method: 'POST', body: {} }) : Promise.resolve(null)));
  usePageKeys({ refresh: () => { health.reload(); overview.reload(); status.reload(); } });

  const c = overview.data?.counts || {};
  const u = overview.data?.users || {};
  const m = modelTone(status.data);
  const targets = allTargets().filter((t) => canAny(t.perm) && t.to !== '/overview');

  return html`
    <div class="screen">
      <${ScreenHead} title=${'Здравствуйте, ' + (me()?.user?.display_name || me()?.user?.username || '')}
        desc="Состояние сервера и модели, данные игры и разделы, доступные вашим ролям."/>
      <div class="status-grid">
        <${StatusTile} label="Backend" value=${health.error ? 'НЕДОСТУПЕН' : health.data ? 'В СЕТИ' : 'проверка…'}
          tone=${health.error ? 'bad' : health.data ? 'ok' : ''} meta=${location.origin}/>
        ${llm ? html`<${StatusTile} label="Модель" value=${m.value} tone=${m.tone} meta=${modelMeta(status.data)}/>` : null}
        <${StatusTile} label="Игроки" value=${fmtNum(u.players)} meta=${'сотрудников: ' + fmtNum(u.staff)}/>
      </div>
      <${ErrorText} error=${overview.error}/>
      <div class="stats-grid five">
        <${Stat} label="установок игры" value=${fmtNum(c.installations)}/>
        <${Stat} label="игровых сессий" value=${fmtNum(c.sessions)}/>
        <${Stat} label="сообщений" value=${fmtNum(c.messages)}/>
        <${Stat} label="событий" value=${fmtNum(c.events)}/>
        <${Stat} label="запросов к модели" value=${fmtNum(c.llm_requests)}/>
      </div>
      <h3 class="part-title">Разделы</h3>
      <div class="tile-grid">
        ${targets.map((t) => html`
          <${Link} key=${t.to} to=${t.to} className="card tile-link">
            <${Icon} name=${t.icon} size=${22}/><span>${t.title}</span>
          <//>`)}
      </div>
    </div>`;
}
