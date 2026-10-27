// Подписка на изменения: настройки устройства и сессия зовут notify(), экраны перерисовываются (useStore).
// version растёт с каждым notify — так подписчик узнаёт об изменениях, случившихся до его подписки.

const listeners = new Set();
let version = 0;

export const storeVersion = () => version;

export function subscribe(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

export function notify() {
  version++;
  for (const fn of listeners) fn();
}
