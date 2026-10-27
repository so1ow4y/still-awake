// Пользователи — поведение license-store (admin.users + user-details): обозреватель «поле:значение» с топами
// и гистограммой (components/Explorer.js, клик по значению — условие в строку), карточка справа,
// правка, пароль, роли, блокировка, удаление. Сотрудников создаёт администратор — логин и пароль, без почты;
// игроки заводят аккаунт сами — в игре, своим логином и паролем.
//
// Правило иерархии одно на всё: менять можно только тех, чья старшая роль ниже вашей, и назначать только роли
// ниже своей. Сервер проверяет то же самое — интерфейс лишь заранее выключает недоступное.

import { html, useState, useEffect } from '../html.js';
import { api } from '../api.js';
import { useLoad } from '../hooks.js';
import { setQuery } from '../router.js';
import { can, canAny, me, myPriority } from '../session.js';
import { fmtDate, fmtAgo, fmtAgent } from '../format.js';
import { usePageKeys } from '../pageKeys.js';
import { withTerm } from '../query.js';
import { Explorer, Value, localDate } from '../components/Explorer.js';
import { Field, Input, Check, ActionButton, ErrorText, Loading, Empty, Badge, Dialog, CopyButton, Textarea } from '../components/ui.js';
import { SidePanel, PanelSection, Facts } from '../components/SidePanel.js';
import { toast } from '../components/Toast.js';
import { Icon } from '../icons.js';
import { ScreenHead, RoleBadges, StatusBadge, KindBadge } from './common.js';
import { LoginRows } from './Logins.js';
import { PasswordInput } from './Login.js';

/** Сколько пользователей грузить: фильтрует браузер (как сессии). */
const LIMIT = 2000;

const statusOf = (u) => (u.status === 'blocked' ? 'заблокирован' : u.must_change_password ? 'сменить пароль' : 'активен');
const kindOf = (u) => (u.kind === 'player' ? 'игрок' : 'сотрудник');

const USER_FIELDS = [
  { name: 'логин', aliases: ['username', 'login'], get: (u) => u.username },
  { name: 'имя', aliases: ['name'], get: (u) => u.display_name },
  { name: 'тип', aliases: ['kind'], get: kindOf, hint: 'сотрудник, игрок' },
  { name: 'роль', aliases: ['role'], get: (u) => (u.roles || []).flatMap((r) => [r.name, r.slug]), facet: (u) => (u.roles || []).map((r) => r.slug),
    hint: 'название или slug роли' },
  { name: 'статус', aliases: ['status'], get: statusOf, hint: 'активен, заблокирован, сменить пароль' },
  { name: 'установка', aliases: ['installation', 'inst'], get: (u) => u.installation_id },
  { name: 'ip', aliases: [], get: (u) => u.last_login_ip },
  { name: 'вход', aliases: ['login_date'], get: (u) => (u.last_login_at ? localDate(u.last_login_at) : ''), hint: 'дата последнего входа: 2026-10*' },
  { name: 'создан', aliases: ['created'], get: (u) => localDate(u.created_at) },
  { name: 'id', aliases: [], get: (u) => u.id },
];
const userText = (u) => [u.username, u.display_name, u.id, u.installation_id];
const userTime = (u) => u.created_at;

export function UsersScreen({ query = {} }) {
  const q = query.q || '';
  const [opened, setOpened] = useState(query.id || null);
  const list = useLoad(() => api('/v1/admin/users', { query: { limit: LIMIT } }), []);
  const roles = useLoad(() => (canAny(['roles.read', 'roles.assign', 'roles.manage']) ? api('/v1/admin/roles') : Promise.resolve([])));
  const rows = list.data?.items || null;
  const onQuery = (next) => setQuery({ q: next });
  const addTerm = (field, value, negate) => onQuery(withTerm(q, field, value, negate));
  usePageKeys({ refresh: () => list.reload(), search: () => { document.querySelector('[data-search-input]')?.focus(); return true; } });

  const replaceRow = (user) => list.setData((d) => d && ({ ...d, items: d.items.map((u) => (u.id === user.id ? user : u)) }));
  const columns = [
    { title: 'Пользователь', render: (u, f) => html`<${Value} field="логин" value=${u.username} onFilter=${f}><b>${u.username}</b><//>
      ${u.id === me()?.user?.id ? html` <${Badge} tone="accent">вы<//>` : null}
      ${u.display_name ? html`<div class="muted small">${u.display_name}</div>` : null}` },
    { title: 'Тип', render: (u, f) => html`<${Value} field="тип" value=${kindOf(u)} onFilter=${f}><${KindBadge} kind=${u.kind}/><//>` },
    { title: 'Роли', render: (u, f) => (u.roles || []).length ? (u.roles || []).map((r) => html`<${Value} key=${r.slug} field="роль" value=${r.slug} onFilter=${f}>
      <${Badge} tone="accent-soft">${r.name}<//><//>`) : html`<span class="muted">—</span>` },
    { title: 'Статус', render: (u, f) => html`<${Value} field="статус" value=${statusOf(u)} onFilter=${f}><${StatusBadge} user=${u}/><//>` },
    { title: 'Последний вход', className: 'ex-time', render: (u) => html`<span title=${fmtDate(u.last_login_at)}>${fmtAgo(u.last_login_at)}</span>` },
  ];

  return html`
    <div class="screen wide-screen">
      <${ScreenHead} title="Пользователи" desc="Сотрудники панели и игроки. Менять можно тех, чья роль ниже вашей."
        actions=${can('users.create') ? html`<button type="button" class="btn primary" onClick=${() => setOpened('new')}><${Icon} name="plus" size=${18}/> Сотрудник</button>` : null}/>
      <${Explorer} rows=${rows} loading=${list.loading} error=${list.error} fields=${USER_FIELDS} text=${userText} time=${userTime}
        facets=${['тип', 'роль', 'статус', 'вход']} columns=${columns} q=${q} onQuery=${onQuery}
        placeholder="тип:сотрудник роль:analyst -статус:заблокирован вход:2026-10*" histogramTitle="Регистрации"
        opened=${opened && opened !== 'new' ? opened : null} onOpen=${setOpened} onClose=${() => setOpened(null)}
        note=${list.data && list.data.total > (rows || []).length ? `загружены ${(rows || []).length} из ${list.data.total}` : null}/>

      ${opened === 'new' ? html`<${CreateUserPanel} roles=${roles.data || []} onClose=${() => setOpened(null)}
        onAdded=${() => list.reload()} onCreated=${(user) => setOpened(user.id)}/>` : null}
      ${opened && opened !== 'new' ? html`<${UserPanel} key=${opened} id=${opened} roles=${roles.data || []} onFilter=${addTerm} onClose=${() => setOpened(null)} onChanged=${replaceRow}
        onDeleted=${() => { setOpened(null); list.reload(); }}/>` : null}
    </div>`;
}

/** Галочки ролей: роли не ниже вашей выключены — их нельзя ни выдать, ни снять. */
function RolePicker({ roles, selected, onChange, disabled = false }) {
  const mine = myPriority();
  return html`
    <div class="role-picker">${roles.map((r) => {
      const locked = disabled || r.priority >= mine;
      return html`
        <label key=${r.slug} class=${'role-option' + (selected.includes(r.slug) ? ' on' : '') + (locked ? ' locked' : '')}
          title=${locked && !disabled ? 'Роль не ниже вашей — выдавать и снимать её нельзя' : r.description}>
          <input type="checkbox" checked=${selected.includes(r.slug)} disabled=${locked}
            onChange=${(e) => onChange(e.target.checked ? [...selected, r.slug] : selected.filter((s) => s !== r.slug))}/>
          <span><b>${r.name}</b> <span class="muted small">· ${r.slug} · уровень ${r.priority}</span>
            ${r.description ? html`<span class="role-desc">${r.description}</span>` : null}</span>
        </label>`;
    })}</div>`;
}

function GeneratedPassword({ username, password }) {
  if (!password) return null;
  return html`
    <div class="notice ok">
      <p>Пароль для <b>${username}</b> — показан один раз, передайте его пользователю:</p>
      <p class="secret"><code>${password}</code> <${CopyButton} text=${password}/></p>
    </div>`;
}

function CreateUserPanel({ roles, onClose, onCreated, onAdded }) {
  const [form, setForm] = useState({ username: '', display_name: '', password: '', roles: [], must: true });
  const [created, setCreated] = useState(null);
  const [error, setError] = useState(null);
  const set = (k) => (v) => setForm({ ...form, [k]: v });
  const assignable = roles.filter((r) => r.slug !== 'player');

  const submit = async () => {
    setError(null);
    try {
      const r = await api('/v1/admin/users', {
        method: 'POST',
        body: { username: form.username.trim(), display_name: form.display_name, password: form.password, roles: form.roles, must_change_password: form.must },
      });
      setCreated(r);
      onAdded();
      toast('Сотрудник создан');
    } catch (err) {
      setError(err);
    }
  };

  return html`
    <${SidePanel} title="Новый сотрудник" subtitle="Логин и пароль задаёте вы — почта не нужна" onClose=${onClose}>
      ${created ? html`
        <${GeneratedPassword} username=${created.user.username} password=${created.generated_password}/>
        ${!created.generated_password ? html`<p class="notice ok">Сотрудник <b>${created.user.username}</b> создан с вашим паролем.</p>` : null}
        <div class="btn-row">
          <button type="button" class="btn primary" onClick=${() => onCreated(created.user)}>Открыть карточку</button>
          <button type="button" class="btn" onClick=${() => { setCreated(null); setForm({ username: '', display_name: '', password: '', roles: [], must: true }); }}>Создать ещё</button>
        </div>` : html`
        <div class="form-grid">
          <${Field} label="Логин" hint="Латиница, цифры, точка, дефис, подчёркивание; 3–32 символа">
            <${Input} value=${form.username} onInput=${set('username')} autocomplete="off" autofocus/><//>
          <${Field} label="Имя"><${Input} value=${form.display_name} onInput=${set('display_name')} placeholder="Как показывать в панели"/><//>
        </div>
        <${Field} label="Пароль" hint="Пусто — сгенерировать надёжный (покажем один раз). Не короче 8 символов.">
          <${PasswordInput} value=${form.password} onInput=${set('password')} autocomplete="new-password"/><//>
        <${Check} label="Попросить сменить пароль при первом входе" checked=${form.must} onChange=${set('must')}/>
        <${PanelSection} title="Роли" description="Можно выдать только роли ниже вашей.">
          ${can('roles.assign') ? html`<${RolePicker} roles=${assignable} selected=${form.roles} onChange=${set('roles')}/>`
            : html`<p class="muted small">Для выдачи ролей нужно право roles.assign.</p>`}
        <//>
        <${ErrorText} error=${error}/>
        <div class="btn-row"><${ActionButton} className="btn primary" onClick=${submit}
          disabled=${!form.username.trim() || form.roles.length === 0}>Создать<//></div>`}
    <//>`;
}

function UserPanel({ id, roles, onFilter, onClose, onChanged, onDeleted }) {
  const card = useLoad(() => api(`/v1/admin/users/${encodeURIComponent(id)}`), [id]);
  const [dialog, setDialog] = useState(null);
  const data = card.data;
  const user = data?.user;

  const apply = (updated) => {
    card.setData((c) => ({ ...c, user: updated }));
    onChanged(updated);
  };

  const self = user && user.id === me()?.user?.id;
  const manageable = !!data?.manageable;
  return html`
    <${SidePanel} title=${user ? user.username : 'Пользователь'} subtitle=${user?.display_name}
      badges=${user ? html`<${KindBadge} kind=${user.kind}/> <${StatusBadge} user=${user}/>` : null} onClose=${onClose}
      actions=${user && manageable && can('users.delete')
        ? html`<button type="button" class="btn small danger-outline" onClick=${() => setDialog('delete')}><${Icon} name="trash" size=${16}/> Удалить</button>` : null}>
      <${ErrorText} error=${card.error}/>
      ${!user ? html`<${Loading}/>` : html`
        ${self ? html`<p class="notice">Это вы. Свой логин, имя и пароль меняйте в «Настройки → Профиль».</p>`
          : !manageable ? html`<p class="notice warn">Роль пользователя не ниже вашей — только просмотр.</p>` : null}
        <${Facts} rows=${[
          ['ID', html`<code>${user.id}</code>`],
          ['Роли', (user.roles || []).length ? user.roles.map((r) => html`<${Value} key=${r.slug} field="роль" value=${r.slug} onFilter=${onFilter}>
            <${Badge} tone="accent-soft">${r.name}<//><//>`) : html`<${RoleBadges} roles=${user.roles}/>`],
          ['Создан', fmtDate(user.created_at)],
          ['Последний вход', user.last_login_at ? html`${fmtDate(user.last_login_at)} · <${Value} field="ip" value=${user.last_login_ip} onFilter=${onFilter} mono=${true}/>` : 'никогда'],
          user.installation_id ? ['Установка игры', html`<${Value} field="установка" value=${user.installation_id} onFilter=${onFilter} mono=${true}/>
            · <a href=${'#/sessions?q=' + encodeURIComponent('установка:' + user.installation_id)}>ночи этой установки</a>`] : null,
          user.status === 'blocked' ? ['Причина блокировки', user.blocked_reason || '—'] : null,
        ]}/>
        ${manageable && can('users.update') ? html`<${ProfileSection} user=${user} onSaved=${apply}/>` : null}
        ${manageable && can('users.update') ? html`<${PasswordSection} user=${user} onSaved=${() => card.reload()}/>` : null}
        ${manageable && can('roles.assign') ? html`<${RolesSection} user=${user} roles=${roles} onSaved=${apply}/>` : null}
        ${manageable && can('users.block') ? html`
          <${PanelSection} title="Доступ" description="Блокировка и выход завершают все сессии пользователя сразу.">
            <div class="btn-row">
              ${user.status === 'blocked'
                ? html`<${ActionButton} className="btn" onClick=${async () => { apply(await api(`/v1/admin/users/${id}/unblock`, { method: 'POST' })); toast('Разблокирован'); }}>Разблокировать<//>`
                : html`<button type="button" class="btn danger-outline" onClick=${() => setDialog('block')}><${Icon} name="ban" size=${16}/> Заблокировать</button>`}
              <${ActionButton} className="btn" onClick=${async () => { const r = await api(`/v1/admin/users/${id}/logout`, { method: 'POST' }); toast(`Завершено сессий: ${r.revoked_sessions}`); card.reload(); }}>Завершить все сессии<//>
            </div>
          <//>` : null}
        <${PanelSection} title="История логинов" description="Каждая смена логина сохраняется.">
          ${data.usernames?.length ? html`<ul class="history">${data.usernames.map((h) => html`
            <li key=${h.id}><b>${h.old_username}</b> → <b>${h.new_username}</b>
              <span class="muted small">${fmtDate(h.created_at)} · ${h.changed_by === user.id ? 'сам' : h.changed_by_name || 'администратор'}</span></li>`)}</ul>`
            : html`<p class="muted small">Логин не менялся.</p>`}
        <//>
        ${data.logins ? html`<${PanelSection} title="Последние входы"><${LoginRows} rows=${data.logins} showUser=${false}/><//>` : null}
        <${PanelSection} title="Сессии">
          ${data.sessions?.length ? html`<div class="table-wrap"><table class="tbl">
            <thead><tr><th>Где</th><th>Адрес</th><th>Активность</th><th>Статус</th></tr></thead>
            <tbody>${data.sessions.map((s) => html`<tr key=${s.id}>
              <td>${s.kind === 'game' ? 'игра' : fmtAgent(s.user_agent)}</td><td><code>${s.ip || '—'}</code></td><td>${fmtAgo(s.last_seen_at)}</td>
              <td>${s.revoked_at ? html`<${Badge}>завершена<//>` : html`<${Badge} tone="ok">активна<//>`}</td></tr>`)}</tbody>
          </table></div>` : html`<p class="muted small">Входов ещё не было.</p>`}
        <//>`}
      ${dialog === 'block' ? html`<${BlockDialog} user=${user} onClose=${() => setDialog(null)} onDone=${apply}/>` : null}
      ${dialog === 'delete' ? html`<${DeleteDialog} user=${user} onClose=${() => setDialog(null)} onDone=${onDeleted}/>` : null}
    <//>`;
}

/**
 * Удалить пользователя. У игрока можно стереть и его ночи (установку игры со всеми сессиями); иначе они остаются
 * в «Игровых сессиях» без владельца. Логин после удаления свободен.
 */
function DeleteDialog({ user, onClose, onDone }) {
  const player = user.kind === 'player' && !!user.installation_id;
  const [withData, setWithData] = useState(player);
  return html`
    <${Dialog} title=${'Удалить ' + user.username + '?'} danger=${true} confirmLabel="Удалить навсегда" onClose=${onClose}
      onConfirm=${async () => {
        const r = await api(`/v1/admin/users/${encodeURIComponent(user.id)}`, { method: 'DELETE', query: withData ? { with_data: 1 } : {} });
        toast(r.deleted_sessions ? `Пользователь удалён вместе с ночами (${r.deleted_sessions})` : 'Пользователь удалён');
        onDone();
      }}>
      <p>Аккаунт, его роли, входы и история логинов удалятся без возможности восстановить. Логин станет свободен.</p>
      ${player ? html`<${Check} label="Удалить и ночи игрока" checked=${withData} onChange=${setWithData}
        hint=${'Установка ' + user.installation_id + ' со всеми сессиями, перепиской, событиями и запросами к модели. Без галочки ночи останутся в «Игровых сессиях».'}/>` : null}
    <//>`;
}

function ProfileSection({ user, onSaved }) {
  const [form, setForm] = useState({ username: user.username, display_name: user.display_name });
  useEffect(() => setForm({ username: user.username, display_name: user.display_name }), [user.id, user.username]);
  const dirty = form.username !== user.username || form.display_name !== user.display_name;
  const save = async () => {
    const body = {};
    if (form.username !== user.username) body.username = form.username;
    if (form.display_name !== user.display_name) body.display_name = form.display_name;
    onSaved(await api(`/v1/admin/users/${user.id}`, { method: 'PATCH', body }));
    toast('Сохранено');
  };
  return html`
    <${PanelSection} title="Профиль" description="Смена логина попадает в историю логинов.">
      <div class="form-grid">
        <${Field} label="Логин"><${Input} value=${form.username} onInput=${(v) => setForm({ ...form, username: v })}/><//>
        <${Field} label="Имя"><${Input} value=${form.display_name} onInput=${(v) => setForm({ ...form, display_name: v })}/><//>
      </div>
      <div class="btn-row"><${ActionButton} className="btn primary" onClick=${save} disabled=${!dirty}>Сохранить<//></div>
    <//>`;
}

function PasswordSection({ user, onSaved }) {
  const [password, setPassword] = useState('');
  const [must, setMust] = useState(true);
  const [generated, setGenerated] = useState('');
  const save = async () => {
    const r = await api(`/v1/admin/users/${user.id}/password`, { method: 'POST', body: { password, must_change_password: must } });
    setGenerated(r.generated_password || '');
    setPassword('');
    toast(r.generated_password ? 'Пароль сгенерирован' : 'Пароль задан');
    onSaved();
  };
  return html`
    <${PanelSection} title="Пароль" description="Новый пароль завершает все сессии пользователя.">
      <${GeneratedPassword} username=${user.username} password=${generated}/>
      <${Field} label="Новый пароль" hint="Пусто — сгенерировать"><${PasswordInput} value=${password} onInput=${setPassword} autocomplete="new-password"/><//>
      <${Check} label="Попросить сменить при входе" checked=${must} onChange=${setMust}/>
      <div class="btn-row"><${ActionButton} className="btn" onClick=${save}>${password ? 'Задать пароль' : 'Сгенерировать пароль'}<//></div>
    <//>`;
}

function RolesSection({ user, roles, onSaved }) {
  const [selected, setSelected] = useState(user.roles.map((r) => r.slug));
  useEffect(() => setSelected(user.roles.map((r) => r.slug)), [user.id, user.roles.map((r) => r.slug).join()]);
  const dirty = selected.slice().sort().join() !== user.roles.map((r) => r.slug).sort().join();
  const save = async () => {
    onSaved(await api(`/v1/admin/users/${user.id}/roles`, { method: 'PUT', body: { roles: selected } }));
    toast('Роли сохранены');
  };
  return html`
    <${PanelSection} title="Роли" description="Выдавать и снимать можно только роли ниже вашей.">
      <${RolePicker} roles=${roles} selected=${selected} onChange=${setSelected}/>
      <div class="btn-row"><${ActionButton} className="btn primary" onClick=${save} disabled=${!dirty || selected.length === 0}>Сохранить роли<//></div>
    <//>`;
}

function BlockDialog({ user, onClose, onDone }) {
  const [reason, setReason] = useState('');
  return html`
    <${Dialog} title=${'Заблокировать ' + user.username + '?'} danger=${true} confirmLabel="Заблокировать" disabled=${!reason.trim()} onClose=${onClose}
      onConfirm=${async () => { onDone(await api(`/v1/admin/users/${user.id}/block`, { method: 'POST', body: { reason } })); toast('Заблокирован'); }}>
      <p>Все сессии пользователя завершатся сразу, войти он не сможет до разблокировки.</p>
      <${Field} label="Причина" hint="Её увидит пользователь при попытке входа"><${Textarea} value=${reason} onInput=${setReason} rows=${2}/><//>
    <//>`;
}
