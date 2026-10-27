// Вход по логину и паролю (как в license-store, без почты: аккаунты создаёт администратор).

import { html, useState } from '../html.js';
import { login } from '../session.js';
import { readLocal, writeLocal } from '../hooks.js';
import { AppLetter } from '../components/Dock.js';
import { Field, Input, ErrorText } from '../components/ui.js';
import { Icon } from '../icons.js';

export function PasswordInput({ value, onInput, autocomplete = 'current-password', placeholder = '' }) {
  const [shown, setShown] = useState(false);
  return html`
    <span class="password-input">
      <${Input} type=${shown ? 'text' : 'password'} value=${value} onInput=${onInput} autocomplete=${autocomplete} placeholder=${placeholder}/>
      <button type="button" class="icon-btn" onClick=${() => setShown(!shown)} aria-label=${shown ? 'Скрыть пароль' : 'Показать пароль'}
        title=${shown ? 'Скрыть пароль' : 'Показать пароль'}><${Icon} name=${shown ? 'eyeOff' : 'eye'} size=${18}/></button>
    </span>`;
}

export function LoginScreen({ expired = false }) {
  const [username, setUsername] = useState(() => readLocal('lastLogin', ''));
  const [password, setPassword] = useState('');
  const [error, setError] = useState(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      await login(username.trim(), password);
      writeLocal('lastLogin', username.trim());
    } catch (err) {
      setError(err);
      setPassword('');
    } finally {
      setBusy(false);
    }
  };

  return html`
    <div class="login-page">
      <form class="login-card" onSubmit=${submit}>
        <div class="login-brand">
          <${AppLetter} size=${46}/>
          <div><h1>Still Awake?</h1><p class="muted">Панель управления</p></div>
        </div>
        ${expired ? html`<p class="notice warn">Сессия закончилась — войдите снова.</p>` : null}
        <${Field} label="Логин"><${Input} value=${username} onInput=${setUsername} autocomplete="username" autofocus=${!username}/><//>
        <${Field} label="Пароль"><${PasswordInput} value=${password} onInput=${setPassword}/><//>
        <${ErrorText} error=${error}/>
        <button type="submit" class="btn primary wide" disabled=${busy || !username.trim() || !password}>${busy ? 'Входим…' : 'Войти'}</button>
        <p class="muted small login-note">Аккаунты сотрудников создаёт администратор: логин и пароль, без почты.
          Первый супер-админ — из <code>ADMIN_USERNAME</code> / <code>ADMIN_PASSWORD</code> в .env, иначе логин <code>admin</code>
          и пароль из консоли сервера.</p>
      </form>
    </div>`;
}
