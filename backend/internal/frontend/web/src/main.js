// Точка входа: применить тему, нарисовать приложение, узнать, кто вошёл.

import { html, render } from './ui/html.js';
import { App } from './ui/app.js';
import { applyLook } from './ui/prefs.js';
import { loadSession } from './ui/session.js';

applyLook();
render(html`<${App}/>`, document.getElementById('app'));
loadSession();
