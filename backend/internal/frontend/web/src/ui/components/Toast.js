// Короткое сообщение внизу экрана (как в pdd-trainer). toast.error — красное, держится дольше.

import { html, useState, useEffect } from '../html.js';

let push = () => {};

export function toast(text, kind = 'info') {
  push(text, kind);
}
toast.error = (err) => push(err?.message || String(err), 'error');

export function ToastHost() {
  const [msg, setMsg] = useState(null);
  useEffect(() => {
    let timer = null;
    push = (text, kind) => {
      clearTimeout(timer);
      setMsg({ text, kind, at: Date.now() });
      timer = setTimeout(() => setMsg(null), kind === 'error' ? 5000 : 2200);
    };
    return () => clearTimeout(timer);
  }, []);
  return msg ? html`<div class=${'toast' + (msg.kind === 'error' ? ' error' : '')} role="status" key=${msg.at}>${msg.text}</div>` : null;
}
