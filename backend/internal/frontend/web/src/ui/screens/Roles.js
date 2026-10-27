// Роли и права — перенос settings.roles.tsx из license-store. Право — готовое действие системы (каждое открывает свои
// маршруты API), роль — набор таких действий с уровнем. Свою роль собирают из прав, которые есть у самого автора,
// с уровнем ниже своего. Системные роли (из кода сервера) только читаются — их можно взять за основу.

import { html, useState, useEffect, useMemo } from '../html.js';
import { api } from '../api.js';
import { useLoad } from '../hooks.js';
import { can, me, myPriority } from '../session.js';
import { usePageKeys, useListKeys } from '../pageKeys.js';
import { Field, Input, Textarea, ActionButton, ErrorText, Loading, Badge, Dialog, SearchBox } from '../components/ui.js';
import { SidePanel, PanelSection } from '../components/SidePanel.js';
import { toast } from '../components/Toast.js';
import { Icon } from '../icons.js';
import { ScreenHead } from './common.js';

const MIN = 11;
const MAX = 99;
const GROUPS = { users: 'Пользователи', security: 'Безопасность', llm: 'Модель', data: 'Данные', game: 'Игра' };

/** Латиница для кода роли из названия: «Старший саппорт» → starshiy_sapport. */
function slugify(name) {
  const map = {
    а: 'a', б: 'b', в: 'v', г: 'g', д: 'd', е: 'e', ё: 'e', ж: 'zh', з: 'z', и: 'i', й: 'y', к: 'k', л: 'l', м: 'm',
    н: 'n', о: 'o', п: 'p', р: 'r', с: 's', т: 't', у: 'u', ф: 'f', х: 'h', ц: 'c', ч: 'ch', ш: 'sh', щ: 'sch',
    ъ: '', ы: 'y', ь: '', э: 'e', ю: 'yu', я: 'ya',
  };
  const latin = [...name.toLowerCase()].map((ch) => map[ch] ?? ch).join('');
  const slug = latin.replace(/[^a-z0-9]+/g, '_').replace(/^_+|_+$/g, '').slice(0, 64);
  return /^[a-z]/.test(slug) ? slug : slug ? `role_${slug}`.slice(0, 64) : '';
}

export function RolesScreen() {
  const canManage = can('roles.manage');
  const roles = useLoad(() => api('/v1/admin/roles'));
  const [draft, setDraft] = useState(null);
  const rows = roles.data || [];
  const openId = draft?.mode === 'edit' ? draft.role.id : null;
  const cursor = useListKeys({
    ids: rows.map((r) => r.id), openedId: openId,
    onOpen: (id) => { const role = rows.find((r) => r.id === id); if (role) setDraft({ mode: 'edit', role }); },
    onClose: () => setDraft(null),
  });
  usePageKeys({ refresh: () => roles.reload() });

  return html`
    <div class="screen">
      <${ScreenHead} title="Роли и права" desc=${'Ваш уровень — ' + myPriority() + '. Свои роли собираются из ваших прав и всегда ниже вашего уровня.'}
        actions=${canManage ? html`<button type="button" class="btn primary" onClick=${() => setDraft({ mode: 'create' })}><${Icon} name="plus" size=${18}/> Роль</button>` : null}/>
      <section class="card">
        <${ErrorText} error=${roles.error}/>
        ${roles.loading && !roles.data ? html`<${Loading}/>` : html`
          <div class="table-wrap"><table class="tbl rows">
            <thead><tr><th>Роль</th><th class="num">Уровень</th><th class="num">Прав</th><th class="num">Пользователей</th></tr></thead>
            <tbody>${rows.map((role) => html`
              <tr key=${role.id} data-row-id=${role.id} onClick=${() => setDraft(openId === role.id ? null : { mode: 'edit', role })}
                class=${(openId === role.id ? 'active ' : '') + (cursor === role.id ? 'cursor' : '')}>
                <td><b>${role.name}</b> ${role.is_system ? html`<${Badge}><${Icon} name="lock" size=${12}/> системная<//>` : html`<${Badge} tone="accent-soft">своя<//>`}
                  <div class="muted small"><code>${role.slug}</code>${role.description ? ' · ' + role.description : ''}</div></td>
                <td class="num">${role.priority}</td><td class="num">${role.permissions.length}</td><td class="num">${role.users}</td>
              </tr>`)}</tbody>
          </table></div>`}
      </section>
      ${draft ? html`<${RolePanel} key=${draft.mode === 'edit' ? draft.role.id : 'new-' + (draft.base?.id || '')} draft=${draft} canManage=${canManage}
        onClose=${() => setDraft(null)} onCopy=${(role) => setDraft({ mode: 'create', base: role })}
        onSaved=${(role) => { roles.reload(); setDraft({ mode: 'edit', role }); }} onDeleted=${() => { roles.reload(); setDraft(null); }}/>` : null}
    </div>`;
}

function RolePanel({ draft, canManage, onClose, onCopy, onSaved, onDeleted }) {
  const permissions = useLoad(() => api('/v1/admin/permissions'));
  const mine = useMemo(() => new Set(me()?.permissions || []), []);
  const editing = draft.mode === 'edit' ? draft.role : null;
  const source = editing || draft.base;
  const readOnly = !canManage || !!editing?.is_system || (editing && editing.priority >= myPriority());

  const [name, setName] = useState(editing ? editing.name : source ? `${source.name} (копия)` : '');
  const [slug, setSlug] = useState(editing?.slug || (source ? `${source.slug}_copy` : ''));
  const [slugTouched, setSlugTouched] = useState(!!(editing || source));
  const [description, setDescription] = useState(source?.description || '');
  const [selected, setSelected] = useState(() => new Set((source?.permissions || []).filter((p) => editing || mine.has(p))));
  const [priority, setPriority] = useState(String(editing?.priority ?? (source && !source.is_system ? source.priority : Math.max(MIN, Math.min(MAX, myPriority() - 10)))));
  const [search, setSearch] = useState('');
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState(null);

  useEffect(() => {
    if (!slugTouched && !editing) setSlug(slugify(name));
  }, [name]);

  const prio = Number(priority);
  const problems = readOnly ? [] : [
    !name.trim() && 'Укажите название',
    !editing && !/^[a-z][a-z0-9_]{2,63}$/.test(slug) && 'Код роли: 3–64 символа — латиница, цифры, _',
    selected.size === 0 && 'Выберите хотя бы одно право',
    !(Number.isInteger(prio) && prio >= MIN && prio <= MAX) && `Уровень — от ${MIN} до ${MAX}`,
    Number.isInteger(prio) && prio >= myPriority() && `Уровень должен быть ниже вашего (${myPriority()})`,
  ].filter(Boolean);

  const groups = useMemo(() => {
    const needle = search.trim().toLowerCase();
    const map = new Map();
    for (const p of permissions.data || []) {
      if (needle && !`${p.slug} ${p.description} ${p.group}`.toLowerCase().includes(needle)) continue;
      map.set(p.group, [...(map.get(p.group) || []), p]);
    }
    return [...map.entries()];
  }, [permissions.data, search]);

  const allowed = (s) => !readOnly && mine.has(s);
  const toggle = (s, on) => {
    const next = new Set(selected);
    if (on) next.add(s); else next.delete(s);
    setSelected(next);
  };
  const toggleGroup = (items, on) => {
    const next = new Set(selected);
    for (const p of items) if (allowed(p.slug)) { if (on) next.add(p.slug); else next.delete(p.slug); }
    setSelected(next);
  };

  const submit = async () => {
    setError(null);
    try {
      const body = { name: name.trim(), description: description.trim(), priority: prio, permissions: [...selected] };
      const role = editing
        ? await api(`/v1/admin/roles/${editing.id}`, { method: 'PATCH', body })
        : await api('/v1/admin/roles', { method: 'POST', body: { ...body, slug } });
      toast('Сохранено');
      onSaved(role);
    } catch (err) {
      setError(err);
    }
  };

  return html`
    <${SidePanel} title=${editing ? editing.name : 'Новая роль'} onClose=${onClose}
      subtitle=${editing ? html`<code>${editing.slug}</code>` : source ? 'на основе «' + source.name + '»' : 'из ваших прав, уровнем ниже вашего'}
      badges=${editing ? (editing.is_system ? html`<${Badge}><${Icon} name="lock" size=${12}/> системная<//>` : html`<${Badge} tone="accent-soft">своя<//>`) : null}
      actions=${canManage && editing ? html`<button type="button" class="btn small" onClick=${() => onCopy(editing)}><${Icon} name="copy" size=${15}/> На основе</button>` : null}>
      ${editing?.is_system ? html`<p class="notice">Системные роли задаются в коде сервера и пересобираются при запуске. Возьмите её за основу — «На основе».</p>` : null}
      ${editing && !editing.is_system && editing.priority >= myPriority() ? html`<p class="notice warn">Роль не ниже вашего уровня — только просмотр.</p>` : null}
      <div class="form-grid">
        <${Field} label="Название" wide=${true}><${Input} value=${name} onInput=${setName} maxlength="128" disabled=${readOnly}/><//>
        <${Field} label="Код роли"><${Input} value=${slug} onInput=${(v) => { setSlugTouched(true); setSlug(v.toLowerCase()); }} disabled=${readOnly || !!editing} class="input mono"/><//>
        <${Field} label="Уровень" hint=${'От ' + MIN + ' до ' + Math.min(MAX, myPriority() - 1) + '. Кто выше уровнем, тот управляет теми, кто ниже.'}>
          <${Input} type="number" min=${MIN} max=${MAX} value=${priority} onInput=${setPriority} disabled=${readOnly}/><//>
        <${Field} label="Описание" wide=${true}><${Textarea} value=${description} onInput=${setDescription} rows=${2} maxlength="500" disabled=${readOnly}/><//>
      </div>
      <${PanelSection} title=${'Права: ' + selected.size} description=${readOnly ? null : 'Доступны только права, которые есть у вас.'}>
        <${SearchBox} value=${search} onInput=${setSearch} placeholder="Найти право"/>
        <${ErrorText} error=${permissions.error}/>
        ${!permissions.data ? html`<${Loading}/>` : groups.map(([group, items]) => {
          const selectable = items.filter((p) => allowed(p.slug));
          const all = selectable.length > 0 && selectable.every((p) => selected.has(p.slug));
          return html`
            <div class="perm-group" key=${group}>
              <div class="perm-group-head"><span>${GROUPS[group] || group}</span>
                ${!readOnly && selectable.length ? html`<button type="button" class="link-btn" onClick=${() => toggleGroup(items, !all)}>${all ? 'снять все' : 'все'}</button>` : null}</div>
              ${items.map((p) => {
                const ok = allowed(p.slug);
                return html`
                  <label key=${p.slug} class=${'perm' + (selected.has(p.slug) ? ' on' : '') + (ok ? '' : ' locked')}
                    title=${!ok && !readOnly ? 'Этого права нет у вас — включить его в роль нельзя' : ''}>
                    <input type="checkbox" checked=${selected.has(p.slug)} disabled=${!ok} onChange=${(e) => toggle(p.slug, e.target.checked)}/>
                    <span><span>${p.description || p.slug}</span><code>${p.slug}</code></span>
                  </label>`;
              })}
            </div>`;
        })}
      <//>
      ${!readOnly ? html`
        ${problems.length ? html`<ul class="problems">${problems.map((p) => html`<li key=${p}>${p}</li>`)}</ul>` : null}
        <${ErrorText} error=${error}/>
        <div class="btn-row">
          <${ActionButton} className="btn primary" onClick=${submit} disabled=${problems.length > 0}>${editing ? 'Сохранить' : 'Создать'}<//>
          ${editing ? html`<button type="button" class="btn danger-outline" onClick=${() => setDeleting(true)}><${Icon} name="trash" size=${16}/> Удалить роль</button>` : null}
        </div>` : null}
      ${deleting ? html`<${Dialog} title=${'Удалить роль «' + editing.name + '»?'} danger=${true} confirmLabel="Удалить" onClose=${() => setDeleting(false)}
        onConfirm=${async () => { const r = await api(`/v1/admin/roles/${editing.id}`, { method: 'DELETE' }); toast(`Роль удалена (была у ${r.users})`); onDeleted(); }}>
        <p>${editing.users ? `Роль есть у ${editing.users} пользователей — она будет снята. Игроки без ролей получат роль «Игрок».` : 'Роль ни у кого не назначена.'}</p><//>` : null}
    <//>`;
}
