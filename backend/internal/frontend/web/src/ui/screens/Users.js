// Пользователи — поведение license-store (admin.users + user-details): поиск «поле:значение», карточка справа,
// правка, пароль, роли, блокировка. Сотрудников создаёт администратор — логин и пароль, без почты; игроки
// появляются сами, когда регистрируются в игре по телефону.
//
// Правило иерархии одно на всё: менять можно только тех, чья старшая роль ниже вашей, и назначать только роли
// ниже своей. Сервер проверяет то же самое — интерфейс лишь заранее выключает недоступное.

import { html, useState, useEffect } from '../html.js';
import { api } from '../api.js';
import { useLoad } from '../hooks.js';
import { setQuery } from '../router.js';
import { can, canAny, me, myPriority } from '../session.js';
import { fmtDate, fmtAgo, fmtAgent } from '../format.js';
import { usePageKeys, useListKeys } from '../pageKeys.js';
import { Field, Input, Check, Choice, ActionButton, ErrorText, Loading, Empty, Badge, SearchBox, Dialog, CopyButton, Textarea } from '../components/ui.js';
import { SidePanel, PanelSection, Facts } from '../components/SidePanel.js';
import { toast } from '../components/Toast.js';
import { Icon } from '../icons.js';
import { ScreenHead, RoleBadges, StatusBadge, KindBadge } from './common.js';
import { LoginRows } from './Logins.js';
import { PasswordInput } from './Login.js';

const KINDS = [['', 'все'], ['staff', 'сотрудники'], ['player', 'игроки']];
const PAGE = 100;

export function UsersScreen({ query = {} }) {
  const kind = KINDS.some(([v]) => v === query.kind) ? query.kind || '' : '';
  const q = query.q || '';
  const [search, setSearch] = useState(q);
  const [limit, setLimit] = useState(PAGE);
  const [opened, setOpened] = useState(query.id || null);
  const list = useLoad(() => api('/v1/admin/users', { query: { q, kind, limit } }), [q, kind, limit]);
  const roles = useLoad(() => (canAny(['roles.read', 'roles.assign', 'roles.manage']) ? api('/v1/admin/roles') : Promise.resolve([])));
  const rows = list.data?.items || [];

  const cursor = useListKeys({
    ids: rows.map((u) => u.id), openedId: opened && opened !== 'new' ? opened : null,
    onOpen: setOpened, onClose: () => setOpened(null),
  });
  usePageKeys({ refresh: () => list.reload(), search: () => { document.querySelector('[data-search-input]')?.focus(); return true; } });

  const replaceRow = (user) => list.setData((d) => d && ({ ...d, items: d.items.map((u) => (u.id === user.id ? user : u)) }));

  return html`
    <div class="screen wide-screen">
      <${ScreenHead} title="Пользователи" desc="Сотрудники панели и игроки. Менять можно тех, чья роль ниже вашей."
        actions=${html`
          <${Choice} label="Кто" value=${kind} options=${KINDS} onChange=${(v) => setQuery({ kind: v })}/>
          ${can('users.create') ? html`<button type="button" class="btn primary" onClick=${() => setOpened('new')}><${Icon} name="plus" size=${18}/> Сотрудник</button>` : null}`}/>
      <div class="toolbar">
        <${SearchBox} value=${search} onInput=${setSearch} placeholder="логин, имя, телефон · role:analyst · status:blocked · phone:900"
          onEnter=${() => setQuery({ q: search.trim() })}/>
        <button type="button" class="btn" onClick=${() => setQuery({ q: search.trim() })}>Найти</button>
        ${q ? html`<button type="button" class="btn" onClick=${() => { setSearch(''); setQuery({ q: '' }); }}>Сбросить</button>` : null}
      </div>
      <section class="card">
        <${ErrorText} error=${list.error}/>
        ${list.loading && !list.data ? html`<${Loading}/>` : rows.length === 0 ? html`<${Empty}>Никого не нашлось. Поиск — и по прежним логинам.<//>` : html`
          <p class="muted small">Найдено: ${list.data.total}</p>
          <div class="table-wrap"><table class="tbl rows">
            <thead><tr><th>Пользователь</th><th>Тип</th><th>Роли</th><th>Телефон</th><th>Статус</th><th>Последний вход</th></tr></thead>
            <tbody>${rows.map((u) => html`
              <tr key=${u.id} data-row-id=${u.id} onClick=${() => setOpened(opened === u.id ? null : u.id)}
                class=${(opened === u.id ? 'active ' : '') + (cursor === u.id ? 'cursor' : '')}>
                <td><b>${u.username}</b>${u.id === me()?.user?.id ? html` <${Badge} tone="accent">вы<//>` : null}
                  ${u.display_name ? html`<div class="muted small">${u.display_name}</div>` : null}</td>
                <td><${KindBadge} kind=${u.kind}/></td>
                <td><${RoleBadges} roles=${u.roles}/></td>
                <td>${u.phone || html`<span class="muted">—</span>`}</td>
                <td><${StatusBadge} user=${u}/></td>
                <td title=${fmtDate(u.last_login_at)}>${fmtAgo(u.last_login_at)}</td>
              </tr>`)}</tbody>
          </table></div>
          ${list.data.total > rows.length ? html`<div class="btn-row"><button type="button" class="btn" onClick=${() => setLimit(limit + PAGE)}>Показать ещё</button></div>` : null}`}
      </section>

      ${opened === 'new' ? html`<${CreateUserPanel} roles=${roles.data || []} onClose=${() => setOpened(null)}
        onAdded=${() => list.reload()} onCreated=${(user) => setOpened(user.id)}/>` : null}
      ${opened && opened !== 'new' ? html`<${UserPanel} key=${opened} id=${opened} roles=${roles.data || []} onClose=${() => setOpened(null)} onChanged=${replaceRow}/>` : null}
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

function UserPanel({ id, roles, onClose, onChanged }) {
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
      badges=${user ? html`<${KindBadge} kind=${user.kind}/> <${StatusBadge} user=${user}/>` : null} onClose=${onClose}>
      <${ErrorText} error=${card.error}/>
      ${!user ? html`<${Loading}/>` : html`
        ${self ? html`<p class="notice">Это вы. Свой логин, имя и пароль меняйте в «Настройки → Профиль».</p>`
          : !manageable ? html`<p class="notice warn">Роль пользователя не ниже вашей — только просмотр.</p>` : null}
        <${Facts} rows=${[
          ['ID', html`<code>${user.id}</code>`],
          user.phone ? ['Телефон', user.phone] : null,
          ['Роли', html`<${RoleBadges} roles=${user.roles}/>`],
          ['Создан', fmtDate(user.created_at)],
          ['Последний вход', user.last_login_at ? `${fmtDate(user.last_login_at)} · ${user.last_login_ip || '—'}` : 'никогда'],
          user.installation_id ? ['Установка игры', html`<a href=${'#/sessions?q=' + encodeURIComponent(user.installation_id)}><code>${user.installation_id}</code></a>`] : null,
          user.status === 'blocked' ? ['Причина блокировки', user.blocked_reason || '—'] : null,
        ]}/>
        ${manageable && can('users.update') ? html`<${ProfileSection} user=${user} onSaved=${apply}/>` : null}
        ${manageable && can('users.update') && user.kind === 'staff' ? html`<${PasswordSection} user=${user} onSaved=${() => card.reload()}/>` : null}
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
    <//>`;
}

function ProfileSection({ user, onSaved }) {
  const [form, setForm] = useState({ username: user.username, display_name: user.display_name, phone: user.phone || '' });
  useEffect(() => setForm({ username: user.username, display_name: user.display_name, phone: user.phone || '' }), [user.id, user.username]);
  const dirty = form.username !== user.username || form.display_name !== user.display_name || form.phone !== (user.phone || '');
  const save = async () => {
    const body = {};
    if (form.username !== user.username) body.username = form.username;
    if (form.display_name !== user.display_name) body.display_name = form.display_name;
    if (form.phone !== (user.phone || '')) body.phone = form.phone;
    onSaved(await api(`/v1/admin/users/${user.id}`, { method: 'PATCH', body }));
    toast('Сохранено');
  };
  return html`
    <${PanelSection} title="Профиль" description="Смена логина попадает в историю логинов.">
      <div class="form-grid">
        <${Field} label="Логин"><${Input} value=${form.username} onInput=${(v) => setForm({ ...form, username: v })}/><//>
        <${Field} label="Имя"><${Input} value=${form.display_name} onInput=${(v) => setForm({ ...form, display_name: v })}/><//>
        ${user.kind === 'player' ? html`<${Field} label="Телефон"><${Input} value=${form.phone} onInput=${(v) => setForm({ ...form, phone: v })}/><//>` : null}
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
