// Меню дока. Пункт: { to, title, icon, names?, key?, perm? } — perm: достаточно любого из прав (как в license-store),
// names — экраны, на которых пункт подсвечен. Ветка: { id, title, icon, items }. Пункт без прав скрывается;
// ветка из одного видимого пункта становится обычной ссылкой — лишний клик ни к чему.

import { canAny, me } from './session.js';

const LLM_VIEW = ['llm.settings', 'llm.debug', 'llm.prompt'];
const ROLES_VIEW = ['roles.read', 'roles.assign', 'roles.manage'];
/** Обзор — сводка по всему серверу: сотрудникам с любым правом на данные или модель, игрокам — нет. */
const STAFF_VIEW = ['sessions.read', 'analytics.read', 'users.read', 'db.read', 'logins.read', ...LLM_VIEW];

const MENU = [
  {
    title: 'Панель',
    items: [
      { to: '/overview', title: 'Обзор', icon: 'home', names: ['overview'], key: 'goOverview', perm: STAFF_VIEW },
      { to: '/my', title: 'Мои ночи', icon: 'gamepad', names: ['my'], perm: ['game.play'], players: true },
      {
        id: 'model', title: 'Модель', icon: 'cpu',
        items: [
          { to: '/model', title: 'Провайдер и статус', icon: 'cpu', names: ['model'], key: 'goModel', perm: LLM_VIEW },
          { to: '/prompt', title: 'Системный промпт', icon: 'prompt', names: ['prompt'], key: 'goPrompt', perm: ['llm.prompt'] },
          { to: '/chat', title: 'Отладочный чат', icon: 'chat', names: ['chat'], key: 'goChat', perm: ['llm.debug'] },
        ],
      },
      {
        id: 'data', title: 'Данные', icon: 'chart',
        items: [
          { to: '/analytics', title: 'Аналитика', icon: 'chart', names: ['analytics'], key: 'goAnalytics', perm: ['analytics.read'] },
          { to: '/sessions', title: 'Игровые сессии', icon: 'gamepad', names: ['sessions'], key: 'goSessions', perm: ['sessions.read'] },
          { to: '/database', title: 'База данных', icon: 'database', names: ['database'], key: 'goDatabase', perm: ['db.read'] },
        ],
      },
      {
        id: 'access', title: 'Доступ', icon: 'shield',
        items: [
          { to: '/users', title: 'Пользователи', icon: 'users', names: ['users'], key: 'goUsers', perm: ['users.read'] },
          { to: '/roles', title: 'Роли и права', icon: 'shield', names: ['roles'], key: 'goRoles', perm: ROLES_VIEW },
          { to: '/logins', title: 'Журнал входов', icon: 'login', names: ['logins'], key: 'goLogins', perm: ['logins.read'] },
        ],
      },
    ],
  },
];

/** Пункт виден: есть право, а «только для игроков» — ещё и аккаунт игрока (у супер-админа есть и game.play). */
const visible = (item) => canAny(item.perm) && (!item.players || me()?.user?.kind === 'player');

export function buildMenu() {
  return MENU.map((group) => ({
    title: group.title,
    items: group.items.flatMap((item) => {
      if (!item.items) return visible(item) ? [item] : [];
      const subs = item.items.filter((sub) => canAny(sub.perm));
      if (subs.length === 0) return [];
      if (subs.length === 1) return [{ ...subs[0], icon: subs[0].icon || item.icon }];
      return [{ ...item, items: subs, names: subs.flatMap((s) => s.names || []) }];
    }),
  })).filter((g) => g.items.length > 0);
}

/** Все доступные экраны: адрес → пункт (для проверки доступа и заголовков). */
export function allTargets() {
  return MENU.flatMap((g) => g.items.flatMap((it) => (it.items ? it.items : [it])));
}

/** Нижняя панель телефона: четыре самых нужных доступных раздела, пятая кнопка «Ещё». */
export function compactMenu() {
  const order = ['/overview', '/my', '/chat', '/analytics', '/users', '/sessions', '/prompt', '/model', '/roles'];
  const targets = allTargets().filter(visible);
  return order.map((to) => targets.find((t) => t.to === to)).filter(Boolean).slice(0, 4);
}

export function isTargetActive(route, item) {
  if (item.names && !item.items) return item.names.includes(route.name);
  return route.path === item.to;
}

export const isBranchActive = (route, branch) => branch.items.some((s) => isTargetActive(route, s)) || (branch.names || []).includes(route.name);

export const SECTION_TITLES = {
  overview: 'Обзор', my: 'Мои ночи', model: 'Провайдер и статус модели', prompt: 'Системный промпт', chat: 'Отладочный чат',
  analytics: 'Аналитика', sessions: 'Игровые сессии', database: 'База данных', users: 'Пользователи',
  roles: 'Роли и права', logins: 'Журнал входов', settings: 'Настройки',
};
