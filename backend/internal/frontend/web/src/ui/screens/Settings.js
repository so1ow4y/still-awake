// Настройки — как в pdd-trainer (Kingdom): дерево разделов с поиском, у раздела свой адрес ?section=….
// «Аккаунт» — на сервере (логин, имя, пароль, сессии); «Интерфейс» — на этом устройстве (док, панель справа,
// тема, горячие клавиши), применяется сразу.

import { html, useState, useEffect, useRef } from '../html.js';
import { api } from '../api.js';
import { Icon } from '../icons.js';
import { Link, navigate } from '../router.js';
import { useMedia, useLoad, readLocal, writeLocal } from '../hooks.js';
import {
  getPrefs, setPrefs, resetLook, DOCK_POSITIONS, DOCK_SUB_MODES, DOCK_GROW, DOCK_SIZE_MODES, DOCK_PUSH,
  DOCK_SIZE_MIN, DOCK_SIZE_MAX, DOCK_WIDTH_MIN, DOCK_WIDTH_MAX, SIDE_PANEL_MIN, SIDE_PANEL_MAX, SCHEMES,
} from '../prefs.js';
import { me, updateMe, logout } from '../session.js';
import { ShortcutsSection, Keys } from '../components/Shortcuts.js';
import { bindingOf } from '../keys.js';
import { toast } from '../components/Toast.js';
import { AppLetter } from '../components/Dock.js';
import { Field, Input, Choice, Check, ActionButton, ErrorText, Badge, Loading } from '../components/ui.js';
import { fmtDate, fmtAgo, fmtAgent } from '../format.js';
import { PasswordInput } from './Login.js';
import { LoginRows } from './Logins.js';
import { RoleBadges } from './common.js';

const POS_LABEL = { left: 'Слева', bottom: 'Снизу', right: 'Справа', top: 'Сверху' };
const SUB_LABEL = {
  side: ['Колонкой сбоку', 'Отдельная колонка квадратиков рядом с панелью, вниз от значка ветки.'],
  inline: ['Внутри панели', 'Квадратики раскрываются прямо под значком ветки. Удобно на маленьком экране.'],
};
const GROW_LABEL = {
  down: ['Только вниз', 'Верх стоит на месте, пункт под мышью не уезжает. Что не влезло — прокручивается внутри панели.'],
  up: ['Только вверх', 'Низ стоит на месте, панель растёт вверх и прокручивается внутри, если не влезает.'],
  both: ['В обе стороны', 'Панель остаётся по центру и растёт в обе стороны.'],
};
const SIZE_LABEL = { auto: 'По содержимому', fixed: 'Своя' };
const LENGTH_HINT = { auto: 'Такой длины, сколько пунктов, но не больше экрана.', fixed: 'Всегда выбранной длины, лишние пункты прокручиваются внутри.' };
const WIDTH_HINT = { auto: 'По ширине значков. Раскрытая внутри ветка её не меняет.', fixed: 'Всегда выбранной ширины (у горизонтальной — высоты), значки по центру.' };
const PUSH_LABEL = {
  none: ['Не сдвигать', 'Панель лежит поверх страницы, страница не ужимается.'],
  dock: ['Под панель', 'Страница отодвигается на ширину панели.'],
  all: ['Под панель и подпункты', 'Страница отодвигается ещё и под открытую колонку подпунктов.'],
};
const FILL_LABEL = {
  auto: ['По устройству', 'На телефоне — во всю ширину, на компьютере — плавающей плашкой.'],
  on: ['Всё пространство', 'Панель во всю длину края, без отступа и скруглений.'],
  off: ['Плашкой', 'Плавающая плашка по центру края с отступом от него.'],
};

function SetField({ label, hint, children }) {
  return html`<div class="set-field"><p class="set-field-label">${label}</p>${children}${hint ? html`<p class="check-hint">${hint}</p>` : null}</div>`;
}

function Range({ label, value, min, max, step, unit = 'px', onChange }) {
  return html`
    <label class="range-row">
      <span class="range-head"><span>${label}</span><span class="range-value">${value} ${unit}</span></span>
      <input type="range" min=${min} max=${max} step=${step} value=${value} onInput=${(e) => onChange(Number(e.target.value))}/>
    </label>`;
}

function Option({ selected, onSelect, label, hint = null, children }) {
  return html`
    <button type="button" role="radio" aria-checked=${selected} class=${'opt' + (selected ? ' selected' : '')} onClick=${onSelect}>
      ${children}
      <span class="opt-label"><span>${label}</span>${selected ? html`<${Icon} name="check" size=${16}/>` : null}</span>
      ${hint ? html`<span class="opt-hint">${hint}</span>` : null}
    </button>`;
}

const DockPreview = ({ position }) => html`<span class="dock-preview" aria-hidden="true"><span class=${'dp-bar ' + position}></span></span>`;

function SubModePreview({ mode }) {
  const sq = (accent = false) => html`<span class=${'smp-sq' + (accent ? ' accent' : '')}></span>`;
  return html`
    <span class="sub-preview" aria-hidden="true">
      <span class="smp-dock">
        ${sq()}<span class="smp-ring">${sq(true)}</span>
        ${mode === 'inline' ? html`<span class="smp-inline">${sq()}${sq()}${sq()}</span>` : null}
        ${sq()}${sq()}
      </span>
      ${mode === 'side' ? html`<span class="smp-dock smp-strip">${sq(true)}${sq()}${sq()}${sq()}</span>` : null}
      <span class="smp-page"><span class="tp-line smp-l1"></span><span class="tp-line smp-l2"></span></span>
    </span>`;
}

const PALETTE = {
  light: { bg: '#f5f6fa', card: '#ffffff', fg: '#1b1d27', muted: '#676b7e', border: '#e1e4ee' },
  dark: { bg: '#111218', card: '#1b1d26', fg: '#e8e9f0', muted: '#9a9eb3', border: '#2c2f3d' },
};

function ThemePreview({ mode, accent }) {
  const pane = (m) => {
    const c = PALETTE[m];
    return html`<span class="tp-pane" style=${{ background: c.bg, borderColor: c.border }}>
      <span class="tp-dock" style=${{ background: c.card, borderColor: c.border }}>
        <span class="tp-letter" style=${{ background: accent[m], color: m === 'dark' ? '#0f1220' : '#fff' }}>S</span>
        <span class="tp-dot" style=${{ background: accent[m], opacity: 0.45 }}></span>
        <span class="tp-dot" style=${{ background: c.muted, opacity: 0.4 }}></span>
      </span>
      <span class="tp-card" style=${{ background: c.card, borderColor: c.border }}>
        <span class="tp-line" style=${{ background: c.fg, opacity: 0.8, width: '66%' }}></span>
        <span class="tp-line" style=${{ background: c.muted, opacity: 0.5, width: '50%' }}></span>
        <span class="tp-btn" style=${{ background: accent[m] }}></span>
      </span>
    </span>`;
  };
  if (mode === 'system') return html`<span class="theme-preview split" aria-hidden="true">${pane('light')}${pane('dark')}</span>`;
  return html`<span class="theme-preview" aria-hidden="true">${pane(mode)}</span>`;
}

function DockSection({ phone }) {
  const p = getPrefs();
  const pos = p.dock || (phone ? 'bottom' : 'left');
  const fillKey = p.dockFill === null ? 'auto' : p.dockFill ? 'on' : 'off';
  return html`
    <div class="app-pick">
      <span class="muted">Панель навигации этого устройства</span>
      <button type="button" class="btn small app-pick-reset" title="Док и панель справа — как по умолчанию; тема и цвета не меняются"
        onClick=${() => { resetLook(); toast('Настройки панелей возвращены по умолчанию'); }}>
        <${Icon} name="restore" size=${16}/> Сбросить всё</button>
    </div>
    <section class="set-section">
      <h2>Положение и вид</h2>
      <p class="muted small">${phone
        ? 'На телефоне свёрнутая панель снизу или сверху — пять кнопок с подписями, остальное — в «Ещё» (шторка поверх экрана).'
        : 'Плавающая плашка у выбранного края.'}</p>
      <div class="opt-grid four" role="radiogroup" aria-label="Положение панели">
        ${DOCK_POSITIONS.map((k) => html`
          <${Option} key=${k} selected=${pos === k} onSelect=${() => setPrefs({ dock: k })} label=${POS_LABEL[k]}
            hint=${(phone ? 'bottom' : 'left') === k ? 'по умолчанию' : null}>
            <${DockPreview} position=${k}/>
          <//>`)}
      </div>
      <${SetField} label="Занимать всё пространство" hint=${FILL_LABEL[fillKey][1]}>
        <${Choice} label="Занимать всё пространство" value=${fillKey}
          onChange=${(v) => setPrefs({ dockFill: v === 'auto' ? null : v === 'on' })}
          options=${Object.entries(FILL_LABEL).map(([k, [t]]) => [k, t])}/>
      <//>
      <${Check} label="Развёрнутая" checked=${!!p.expanded} onChange=${(v) => setPrefs({ expanded: v })}
        hint=${phone ? 'На компьютере — значки с подписями и группами. На телефоне панель всегда свёрнута, развёрнутая — шторка «Ещё».' : 'Значки с подписями и названиями групп; свёрнутая — только значки с подсказками.'}/>
      <${Check} label="Автоскрытие" checked=${!!p.autoHide} onChange=${(v) => setPrefs({ autoHide: v })}
        hint="Панель уезжает за край и выезжает, когда подводишь к нему мышь. Только с мышью: на телефоне не действует."/>
      <${Check} label="Спрятать целиком" checked=${!!p.hidden} onChange=${(v) => setPrefs({ hidden: v })}
        hint="У края остаётся только квадрат с буквой «S» — нажатие возвращает панель."/>
      <${SetField} label="Освобождать место на странице под панель" hint=${PUSH_LABEL[p.dockPush]?.[1]}>
        <${Choice} label="Освобождать место" value=${p.dockPush} onChange=${(v) => setPrefs({ dockPush: v })}
          options=${DOCK_PUSH.map((k) => [k, PUSH_LABEL[k][0]])}/>
      <//>
    </section>

    <section class="set-section">
      <h2>Подпункты веток</h2>
      <p class="muted small">Как раскрываются «Модель», «Данные» и «Доступ» в свёрнутой панели. Клик по ветке только раскрывает её.
        С клавиатуры: <${Keys} combo=${bindingOf('menuUp')} ru=${false} empty="—"/> <${Keys} combo=${bindingOf('menuDown')} ru=${false} empty="—"/>
        — по пунктам панели, <${Keys} combo=${bindingOf('menuActivate')} ru=${false} empty="—"/> — открыть пункт или раскрыть ветку.</p>
      <div class="opt-grid" role="radiogroup" aria-label="Подпункты веток">
        ${DOCK_SUB_MODES.map((k) => html`
          <${Option} key=${k} selected=${p.dockSubMode === k} onSelect=${() => setPrefs({ dockSubMode: k })} label=${SUB_LABEL[k][0]} hint=${SUB_LABEL[k][1]}>
            <${SubModePreview} mode=${k}/>
          <//>`)}
      </div>
      <${SetField} label="Куда растёт панель, когда внутри раскрывается ветка" hint=${GROW_LABEL[p.dockGrow]?.[1]}>
        <${Choice} label="Куда растёт панель" value=${p.dockGrow} onChange=${(v) => setPrefs({ dockGrow: v })} options=${DOCK_GROW.map((k) => [k, GROW_LABEL[k][0]])}/>
      <//>
      <${SetField} label="Длина панели" hint=${LENGTH_HINT[p.dockSizeMode]}>
        <${Choice} label="Длина панели" value=${p.dockSizeMode} onChange=${(v) => setPrefs({ dockSizeMode: v })} options=${DOCK_SIZE_MODES.map((k) => [k, SIZE_LABEL[k]])}/>
      <//>
      ${p.dockSizeMode === 'fixed' ? html`<${Range} label="Длина" value=${p.dockSize} min=${DOCK_SIZE_MIN} max=${DOCK_SIZE_MAX} step=${10} onChange=${(v) => setPrefs({ dockSize: v })}/>` : null}
      <${SetField} label="Ширина панели" hint=${WIDTH_HINT[p.dockWidthMode]}>
        <${Choice} label="Ширина панели" value=${p.dockWidthMode} onChange=${(v) => setPrefs({ dockWidthMode: v })} options=${DOCK_SIZE_MODES.map((k) => [k, SIZE_LABEL[k]])}/>
      <//>
      ${p.dockWidthMode === 'fixed' ? html`<${Range} label="Ширина" value=${p.dockWidth} min=${DOCK_WIDTH_MIN} max=${DOCK_WIDTH_MAX} step=${2} onChange=${(v) => setPrefs({ dockWidth: v })}/>` : null}
    </section>`;
}

/** Панель справа (license-store): карточка записи из списка. */
function SidePanelSection() {
  const p = getPrefs();
  return html`
    <section class="set-section">
      <h2>Панель записи справа</h2>
      <p class="muted small">Клик по строке списка (пользователи, роли, сессии) открывает карточку справа, а список остаётся рядом.
        С клавиатуры: <${Keys} combo=${bindingOf('listUp')} ru=${false} empty="—"/> <${Keys} combo=${bindingOf('listDown')} ru=${false} empty="—"/> — по строкам,
        <${Keys} combo=${bindingOf('listOpen')} ru=${false} empty="—"/> — открыть, <${Keys} combo=${bindingOf('listClose')} ru=${false} empty="—"/> или Esc — закрыть.</p>
      <${SetField} label="Страница при открытой панели" hint=${p.sidePanelPush ? 'На широком экране страница отодвигается на ширину панели — список виден целиком.' : 'Панель ложится поверх страницы, как в SIEM.'}>
        <${Choice} label="Страница при открытой панели" value=${p.sidePanelPush ? 'push' : 'over'}
          onChange=${(v) => setPrefs({ sidePanelPush: v === 'push' })} options=${[['over', 'Поверх'], ['push', 'Сдвигать']]}/>
      <//>
      <${Check} label="Менять ширину мышью" checked=${!!p.sidePanelResizable} onChange=${(v) => setPrefs({ sidePanelResizable: v })}
        hint="Потяните за левый край панели — ширина запомнится."/>
      <${Range} label="Ширина" value=${p.sidePanelWidth} min=${SIDE_PANEL_MIN} max=${SIDE_PANEL_MAX} step=${10} onChange=${(v) => setPrefs({ sidePanelWidth: v })}/>
      <p class="check-hint">На узком экране панель всегда во всю ширину.</p>
    </section>`;
}

function ThemeSection() {
  const p = getPrefs();
  const dark = p.theme === 'dark' || (p.theme === 'system' && matchMedia('(prefers-color-scheme: dark)').matches);
  return html`
    <section class="set-section">
      <h2>Тема</h2>
      <div class="opt-grid three" role="radiogroup" aria-label="Тема">
        ${[['system', 'Как в системе'], ['light', 'Светлая'], ['dark', 'Тёмная']].map(([k, label]) => html`
          <${Option} key=${k} selected=${p.theme === k} onSelect=${() => setPrefs({ theme: k })} label=${label}>
            <${ThemePreview} mode=${k} accent=${{ light: (SCHEMES[p.scheme] || SCHEMES.crimson).light, dark: (SCHEMES[p.scheme] || SCHEMES.crimson).dark }}/>
          <//>`)}
      </div>
    </section>
    <section class="set-section">
      <h2>Цветовая схема</h2>
      <p class="muted small">Сейчас: <${AppLetter} size=${20}/> — квадрат с буквой прячет и возвращает панель.</p>
      <div class="opt-grid" role="radiogroup" aria-label="Цветовая схема">
        ${Object.entries(SCHEMES).map(([k, s]) => html`
          <${Option} key=${k} selected=${(p.scheme || 'crimson') === k} onSelect=${() => setPrefs({ scheme: k })} label=${s.name}>
            <${ThemePreview} mode=${dark ? 'dark' : 'light'} accent=${{ light: s.light, dark: s.dark }}/>
          <//>`)}
      </div>
    </section>`;
}

// ---------------------------------------------------------------------------------- аккаунт

function ProfileSection() {
  const user = me()?.user;
  const [name, setName] = useState(user?.display_name || '');
  const [username, setUsername] = useState(user?.username || '');
  const history = useLoad(() => api('/v1/auth/me/usernames'));
  const saveName = async () => {
    updateMe(await api('/v1/auth/me', { method: 'PATCH', body: { display_name: name } }));
    toast('Имя сохранено');
  };
  const saveUsername = async () => {
    updateMe(await api('/v1/auth/me/username', { method: 'POST', body: { username } }));
    history.reload();
    toast('Логин изменён — входите с новым');
  };
  return html`
    <section class="set-section">
      <h2>Кто вы</h2>
      <dl class="facts">
        <dt>Логин</dt><dd><b>${user?.username}</b></dd>
        <dt>Роли</dt><dd><${RoleBadges} roles=${user?.roles || []}/></dd>
        <dt>Уровень</dt><dd>${me()?.priority}</dd>
        <dt>Прав</dt><dd>${me()?.permissions?.length}</dd>
        <dt>Последний вход</dt><dd>${fmtDate(user?.last_login_at)}</dd>
      </dl>
    </section>
    <section class="set-section">
      <h2>Имя</h2>
      <${Field} label="Как показывать вас в панели"><${Input} value=${name} onInput=${setName} maxlength="64"/><//>
      <div class="btn-row"><${ActionButton} className="btn primary" onClick=${saveName} disabled=${name === (user?.display_name || '')}>Сохранить<//></div>
    </section>
    <section class="set-section">
      <h2>Логин</h2>
      <p class="muted small">Логин можно сменить в любой момент. Прежние логины сохраняются в истории — их видят администраторы.</p>
      <${Field} label="Новый логин" hint="Латиница, цифры, точка, дефис, подчёркивание; 3–32 символа"><${Input} value=${username} onInput=${setUsername} autocomplete="username"/><//>
      <div class="btn-row"><${ActionButton} className="btn primary" onClick=${saveUsername} disabled=${!username.trim() || username === user?.username}>Сменить логин<//></div>
      ${history.data?.length ? html`<ul class="history">${history.data.map((h) => html`
        <li key=${h.id}><b>${h.old_username}</b> → <b>${h.new_username}</b> <span class="muted small">${fmtDate(h.created_at)}</span></li>`)}</ul>` : null}
    </section>`;
}

function PasswordSection() {
  const user = me()?.user;
  const [current, setCurrent] = useState('');
  const [next, setNext] = useState('');
  const [repeat, setRepeat] = useState('');
  const [error, setError] = useState(null);
  const mismatch = repeat && next !== repeat;
  const save = async () => {
    setError(null);
    try {
      const r = await api('/v1/auth/me/password', { method: 'POST', body: { current_password: current, new_password: next } });
      setCurrent(''); setNext(''); setRepeat('');
      updateMe(await api('/v1/auth/me'));
      toast(r.revoked_sessions ? `Пароль изменён, на других устройствах выполнен выход (${r.revoked_sessions})` : 'Пароль изменён');
    } catch (err) {
      setError(err);
    }
  };
  return html`
    <section class="set-section">
      <h2>Смена пароля</h2>
      ${user?.must_change_password ? html`<p class="notice warn">Администратор попросил сменить пароль.</p>` : null}
      <${Field} label="Текущий пароль"><${PasswordInput} value=${current} onInput=${setCurrent}/><//>
      <${Field} label="Новый пароль" hint="Не короче 8 символов"><${PasswordInput} value=${next} onInput=${setNext} autocomplete="new-password"/><//>
      <${Field} label="Новый пароль ещё раз" error=${mismatch ? 'Пароли не совпадают' : null}><${PasswordInput} value=${repeat} onInput=${setRepeat} autocomplete="new-password"/><//>
      <${ErrorText} error=${error}/>
      <div class="btn-row"><${ActionButton} className="btn primary" onClick=${save} disabled=${!current || next.length < 8 || next !== repeat}>Сменить пароль<//></div>
      <p class="muted small">После смены пароля остальные устройства выходят из аккаунта.</p>
    </section>`;
}

function SessionsSection() {
  const sessions = useLoad(() => api('/v1/auth/me/sessions'));
  const logins = useLoad(() => api('/v1/auth/me/logins'));
  const revoke = async (id) => {
    await api(`/v1/auth/me/sessions/${id}`, { method: 'DELETE' });
    sessions.reload();
  };
  const active = (sessions.data || []).filter((s) => !s.revoked_at);
  return html`
    <section class="set-section">
      <div class="row-between"><h2>Активные сессии</h2>
        ${active.length > 1 ? html`<${ActionButton} className="btn small" onClick=${async () => { const r = await api('/v1/auth/me/sessions/revoke-others', { method: 'POST' }); toast(`Завершено: ${r.revoked_sessions}`); sessions.reload(); }}>Выйти на остальных<//>` : null}</div>
      ${!sessions.data ? html`<${Loading}/>` : html`<div class="table-wrap"><table class="tbl">
        <thead><tr><th>Устройство</th><th>Адрес</th><th>Активность</th><th></th></tr></thead>
        <tbody>${active.map((s) => html`<tr key=${s.id}>
          <td>${fmtAgent(s.user_agent)} ${s.current ? html`<${Badge} tone="ok">это устройство<//>` : null}</td><td><code>${s.ip || '—'}</code></td>
          <td>${fmtAgo(s.last_seen_at)}</td>
          <td>${s.current ? null : html`<${ActionButton} className="btn small" onClick=${() => revoke(s.id)}>Завершить<//>`}</td></tr>`)}</tbody>
      </table></div>`}
    </section>
    <section class="set-section">
      <h2>Мои входы</h2>
      ${!logins.data ? html`<${Loading}/>` : html`<${LoginRows} rows=${logins.data} showUser=${false}/>`}
    </section>
    <section class="set-section">
      <h2>Выход</h2>
      <div class="btn-row"><button type="button" class="btn danger-outline" onClick=${() => logout()}><${Icon} name="logout" size=${16}/> Выйти из панели</button></div>
    </section>`;
}

function AboutSection() {
  const overview = useLoad(() => api('/v1/admin/overview'));
  const c = overview.data?.config;
  return html`
    <section class="set-section">
      <dl class="facts">
        <dt>Сервер</dt><dd>${location.origin}</dd>
        <dt>Подмена провайдера в отладке</dt><dd>${c ? (c.client_provider_overrides ? 'разрешена' : 'выключена') : '—'}</dd>
        <dt>Изменяющий SQL</dt><dd>${c ? (c.admin_sql_writes ? 'разрешён (ADMIN_ALLOW_SQL_WRITES)' : 'выключен') : '—'}</dd>
      </dl>
      <p class="muted small">Док, настройки, горячие клавиши и стиль — из pdd-trainer; роли, пользователи, панель справа и вход — по образцу license-store.</p>
    </section>`;
}

const TREE = [
  {
    id: 'account', title: 'Аккаунт', icon: 'user',
    items: [
      { key: 'profile', title: 'Профиль', desc: 'Имя и логин; прежние логины остаются в истории.', keywords: 'профиль имя логин username история' },
      { key: 'password', title: 'Пароль', desc: 'Смена пароля; остальные устройства выйдут из аккаунта.', keywords: 'пароль password безопасность' },
      { key: 'sessions', title: 'Сессии и входы', desc: 'Где вы вошли, история входов, выход.', keywords: 'сессии устройства входы выход logout' },
    ],
  },
  {
    id: 'ui', title: 'Интерфейс', icon: 'palette',
    items: [
      { key: 'dock', title: 'Панель навигации', desc: 'Положение, всё пространство, развёрнутость, автоскрытие, подпункты, размер.', keywords: 'док панель положение край слева снизу справа развёрнутая автоскрытие спрятать сдвигать ветки подпункты длина ширина всё пространство' },
      { key: 'panel', title: 'Панель справа', desc: 'Карточка записи справа: поверх или со сдвигом, ширина.', keywords: 'панель справа карточка запись ширина сдвигать поверх side panel' },
      { key: 'theme', title: 'Тема и цвет', desc: 'Светлая, тёмная или как в системе; цветовая схема.', keywords: 'тема светлая тёмная системная цвет схема акцент' },
      { key: 'shortcuts', title: 'Горячие клавиши', desc: 'Назначить на действие сколько угодно сочетаний, отключить, вернуть по умолчанию.', keywords: 'клавиши горячие клавиатура shortcuts назначить сочетание wasd' },
    ],
  },
  {
    id: 'app', title: 'Панель', icon: 'info',
    items: [{ key: 'about', title: 'О панели', desc: 'Сервер и режимы отладки.', keywords: 'версия о панели about сервер' }],
  },
];
const LEAVES = TREE.flatMap((g) => g.items.map((it) => ({ ...it, group: g.id })));

function SettingsNav({ current }) {
  const [search, setSearch] = useState('');
  const [open, setOpen] = useState(() => {
    const v = readLocal('settings.open', ['account', 'ui']);
    return Array.isArray(v) ? v : [];
  });
  const needle = search.trim().toLowerCase();
  const visible = needle
    ? TREE.map((g) => ({ ...g, items: g.items.filter((it) => `${g.title} ${it.title} ${it.keywords}`.toLowerCase().includes(needle)) })).filter((g) => g.items.length)
    : TREE;
  const toggle = (id, value) => {
    const next = value ? [...new Set([...open, id])] : open.filter((g) => g !== id);
    setOpen(next);
    writeLocal('settings.open', next);
  };
  const href = (key) => '/settings?section=' + key;
  return html`
    <nav class="settings-nav" aria-label="Разделы настроек">
      <div class="search-field">
        <${Icon} name="search" size=${16}/>
        <input type="search" data-search-input value=${search} placeholder="Найти настройку" aria-label="Найти настройку"
          onInput=${(e) => setSearch(e.target.value)}
          onKeyDown=${(e) => { if (e.key === 'Enter' && visible[0]?.items[0]) navigate(href(visible[0].items[0].key)); }}/>
      </div>
      ${!visible.length ? html`<p class="muted small sn-empty">Ничего не найдено.</p>` : null}
      <div class="sn-groups">
        ${visible.map((g) => {
          const containsActive = g.items.some((it) => it.key === current);
          const isOpen = !!needle || containsActive || open.includes(g.id);
          return html`
            <div class=${'sn-group' + (isOpen ? ' open' : '')} key=${g.id}>
              <button type="button" class=${'sn-trigger' + (containsActive ? ' active' : '')} aria-expanded=${isOpen} onClick=${() => toggle(g.id, !isOpen)}>
                <${Icon} name=${g.icon} size=${16}/><span>${g.title}</span><${Icon} name="chevron" size=${16} className="sn-chevron"/>
              </button>
              <div class="dock-collapse" inert=${!isOpen}><div class="dock-collapse-inner"><div class="sn-leaves">
                ${g.items.map((it) => html`<${Link} key=${it.key} to=${href(it.key)} className=${'sn-leaf' + (it.key === current ? ' active' : '')}>${it.title}<//>`)}
              </div></div></div>
            </div>`;
        })}
      </div>
    </nav>`;
}

export function SettingsScreen({ query = {} }) {
  const leaf = LEAVES.find((l) => l.key === query.section) || LEAVES[0];
  const phone = !useMedia('(min-width: 900px)');
  const first = useRef(true);
  useEffect(() => {
    if (first.current) {
      first.current = false;
      return;
    }
    if (phone) document.querySelector('.settings-page')?.scrollIntoView({ block: 'start', behavior: 'smooth' });
  }, [leaf.key]);
  const page = {
    profile: () => html`<${ProfileSection}/>`,
    password: () => html`<${PasswordSection}/>`,
    sessions: () => html`<${SessionsSection}/>`,
    dock: () => html`<${DockSection} phone=${phone}/>`,
    panel: () => html`<${SidePanelSection}/>`,
    theme: () => html`<${ThemeSection}/>`,
    shortcuts: () => html`<${ShortcutsSection}/>`,
    about: () => html`<${AboutSection}/>`,
  }[leaf.key]();
  return html`
    <div class="screen settings-layout">
      <${SettingsNav} current=${leaf.key}/>
      <div class="settings-page" key=${leaf.key}>
        <header class="settings-head"><h2>${leaf.title}</h2><p class="muted">${leaf.desc}</p></header>
        ${page}
      </div>
    </div>`;
}
