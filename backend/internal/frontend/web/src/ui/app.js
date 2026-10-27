// Каркас (как в pdd-trainer): док (на телефоне развёрнутый док — шторка поверх страницы с затемнением, Esc и
// переход закрывают её), верхняя полоса, экраны по адресу, горячие клавиши, подсказки, сообщения.
// Без входа — экран входа; экран без прав показывает отказ (сервер всё равно проверит права сам).

import { html, useEffect, useState, useErrorBoundary } from './html.js';
import { Icon } from './icons.js';
import { useStore, useRoute, useMedia } from './hooks.js';
import { navigate } from './router.js';
import { getPrefs, setPrefs, dockPosition, dockFill } from './prefs.js';
import { actionFor } from './keys.js';
import { SECTION_TITLES, allTargets } from './nav.js';
import { session, canAny, me, logout } from './session.js';
import { Dock, dockKeys, AppLetter } from './components/Dock.js';
import { TooltipLayer } from './components/Tooltip.js';
import { ToastHost } from './components/Toast.js';
import { ShortcutsSheet } from './components/Shortcuts.js';
import { runPageKey, listKeys } from './pageKeys.js';
import { LoginScreen } from './screens/Login.js';
import { OverviewScreen } from './screens/Overview.js';
import { ModelScreen } from './screens/Model.js';
import { PromptScreen } from './screens/Prompt.js';
import { ChatScreen } from './screens/Chat.js';
import { AnalyticsScreen } from './screens/Analytics.js';
import { SessionsScreen } from './screens/Sessions.js';
import { DatabaseScreen } from './screens/Database.js';
import { UsersScreen } from './screens/Users.js';
import { RolesScreen } from './screens/Roles.js';
import { LoginsScreen } from './screens/Logins.js';
import { SettingsScreen } from './screens/Settings.js';
import { MyNightsScreen } from './screens/My.js';

const SCREENS = {
  overview: OverviewScreen, model: ModelScreen, prompt: PromptScreen, chat: ChatScreen, analytics: AnalyticsScreen,
  sessions: SessionsScreen, database: DatabaseScreen, users: UsersScreen, roles: RolesScreen, logins: LoginsScreen,
  settings: SettingsScreen, my: MyNightsScreen,
};

const GO = {
  goOverview: '/overview', goModel: '/model', goPrompt: '/prompt', goChat: '/chat', goAnalytics: '/analytics',
  goSessions: '/sessions', goDatabase: '/database', goUsers: '/users', goRoles: '/roles', goLogins: '/logins', goSettings: '/settings',
};

function targetOf(path) {
  return allTargets().find((t) => t.to === path);
}

function allowed(name) {
  const t = allTargets().find((x) => (x.names || []).includes(name));
  return !t || canAny(t.perm);
}

/**
 * Ошибка отрисовки внутри раздела не должна ронять всю панель: без этого Preact останавливал перерисовку,
 * и не работали ни меню, ни закрытие карточки — помогала только перезагрузка страницы.
 */
function ScreenGuard({ route, children }) {
  const [error, reset] = useErrorBoundary((e) => console.error(e));
  useEffect(() => {
    if (error) reset();
  }, [route.path]);
  if (!error) return children;
  return html`<div class="screen"><div class="empty"><${Icon} name="warn" size=${32}/><h2>В разделе произошла ошибка</h2>
    <p class="muted"><code>${error.message || String(error)}</code></p>
    <p><button type="button" class="btn primary" onClick=${reset}>Попробовать снова</button></p></div></div>`;
}

/** Переход без записи в истории — после отрисовки (игрока с «Обзора» — в «Мои ночи»). */
function Redirect({ to }) {
  useEffect(() => navigate(to, { replace: true }), [to]);
  return null;
}

function Screen({ route }) {
  // Игроку сводка сервера не положена: домашний экран у него — свои ночи.
  if (route.name === 'overview' && !allowed('overview') && allowed('my')) return html`<${Redirect} to="/my"/>`;
  const S = SCREENS[route.name];
  if (!S) return html`<div class="screen"><div class="empty"><h2>Нет такого раздела</h2><p><a href="#/overview">На обзор</a></p></div></div>`;
  if (!allowed(route.name)) {
    return html`<div class="screen"><div class="empty"><${Icon} name="lock" size=${32}/><h2>Нет доступа</h2>
      <p>Для этого раздела нужна роль с соответствующими правами. Попросите администратора.</p></div></div>`;
  }
  return html`<${ScreenGuard} route=${route}><${S} key=${route.name} query=${route.query}/><//>`;
}

function focusSearch() {
  if (runPageKey('search')) return true;
  const field = document.querySelector('[data-search-input]');
  if (!field) return false;
  field.focus();
  field.select?.();
  return true;
}

function Shell() {
  const route = useRoute();
  const desktop = useMedia('(min-width: 900px)');
  const phone = !desktop;
  const prefs = getPrefs();
  const position = dockPosition(phone);
  const fill = dockFill(phone);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [help, setHelp] = useState(false);
  const expanded = phone ? mobileOpen : prefs.expanded;
  const overlayOpen = phone && mobileOpen && !prefs.hidden;
  const autoHide = prefs.autoHide && !phone;
  const title = SECTION_TITLES[route.name] || 'Still Awake?';
  const user = me()?.user;

  useEffect(() => {
    if (!phone) setMobileOpen(false);
  }, [phone]);
  useEffect(() => {
    document.title = `${title} · Still Awake?`;
    setMobileOpen(false);
  }, [route.path]);

  const toggleDock = () => {
    if (phone) setMobileOpen(!mobileOpen);
    else setPrefs({ expanded: !prefs.expanded });
  };
  const toggleHidden = () => {
    setMobileOpen(false);
    setPrefs({ hidden: !prefs.hidden });
  };

  // Горячие клавиши (ui/keys.js): общие — здесь, зависящие от экрана — в pageKeys (экран подставляет свои).
  // Обработчик вернул false — нажатие не наше, отдаём браузеру.
  useEffect(() => {
    const onKey = (e) => {
      if (e.key === 'Escape') {
        if (help) setHelp(false);
        else if (overlayOpen) setMobileOpen(false);
        else return;
        e.preventDefault();
        return;
      }
      const id = actionFor(e);
      if (!id) return;
      let fn;
      if (GO[id]) {
        const t = targetOf(GO[id]);
        fn = () => (id === 'goSettings' || id === 'goOverview' || (t && canAny(t.perm)) ? navigate(GO[id]) : false);
      } else {
        fn = {
          search: focusSearch,
          back: () => (history.length > 1 ? (history.back(), true) : false),
          help: () => setHelp(!help),
          dockExpand: toggleDock,
          dockHide: toggleHidden,
          menuUp: () => dockKeys.move(-1),
          menuDown: () => dockKeys.move(1),
          menuActivate: () => dockKeys.activate(),
          listDown: () => listKeys.run('listDown'),
          listUp: () => listKeys.run('listUp'),
          listOpen: () => listKeys.run('listOpen'),
          listClose: () => listKeys.run('listClose'),
        }[id] || (() => runPageKey(id));
      }
      if (fn() !== false) e.preventDefault();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  });

  return html`
    <div class=${'app' + (desktop ? ' desktop' : ' mobile') + ' dock-' + position + (fill ? ' dock-fill' : '') + (prefs.sidePanelPush ? ' panel-push' : '')}>
      <${Dock} route=${route} phone=${phone} position=${position} expanded=${expanded} hidden=${!!prefs.hidden} autoHide=${autoHide}
        overlay=${overlayOpen} fill=${fill} onToggle=${toggleDock} onToggleHidden=${toggleHidden}
        onNavigate=${() => phone && setMobileOpen(false)} username=${user?.username} onLogout=${() => logout()}
        subMode=${prefs.dockSubMode} grow=${prefs.dockGrow} sizeMode=${prefs.dockSizeMode} size=${prefs.dockSize}
        thickness=${prefs.dockWidthMode === 'fixed' ? prefs.dockWidth : null} pagePush=${prefs.dockPush}/>
      ${overlayOpen ? html`<div class="dock-shade" aria-hidden="true" onClick=${() => setMobileOpen(false)}></div>` : null}
      <div class="main-col">
        <header class="topbar">
          <h1 class="topbar-title">${title}</h1>
          <span class="topbar-user muted small">${user?.display_name || user?.username}</span>
          <button class="icon-btn" onClick=${() => setHelp(true)} aria-label="Горячие клавиши" title="Горячие клавиши (?)"><${Icon} name="keyboard" size=${20}/></button>
        </header>
        ${user?.must_change_password && !(route.name === 'settings' && route.query.section === 'password') ? html`
          <div class="banner warn"><${Icon} name="key" size=${18}/> Администратор попросил сменить пароль.
            <a href="#/settings?section=password">Сменить сейчас</a></div>` : null}
        <main class="content wide" id="main">
          <${Screen} route=${route}/>
        </main>
      </div>
      ${help ? html`<${ShortcutsSheet} onClose=${() => setHelp(false)}/>` : null}
      <${TooltipLayer}/>
      <${ToastHost}/>
    </div>`;
}

export function App() {
  useStore();
  const s = session();
  if (s.status === 'loading') return html`<div class="boot"><${AppLetter} size=${44}/><p>Still Awake?…</p></div>`;
  if (s.status === 'anon') return html`<${LoginScreen} expired=${!!s.expired}/><${ToastHost}/>`;
  return html`<${Shell}/>`;
}
