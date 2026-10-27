// Строка поиска в духе SIEM — перенос core/query.js из Kingdom (а туда — из license-store):
//
//   (сценарий:night-1 OR сценарий:night-2) AND NOT результат:caught "фраза" сообщений:>5
//
// поле:значение — совпадение без учёта регистра, `*` — подстановка; для чисел — >, >=, <, <= или точное число.
// Слово без поля ищется подстрокой в основных текстах строки. Операторы: AND (или пробел), OR (или |),
// NOT (или - / ! перед условием), скобки, "фраза". Приоритет: NOT, затем AND, затем OR. Неизвестное поле —
// ошибка, а не тихий поиск по тексту. Дерево проверяется на каждой строке в браузере.

const MAX_TERMS = 30;
const MAX_VALUE = 200;
const MAX_DEPTH = 10;
/** Имя поля: латиница или кириллица, затем буквы, цифры, «_». */
const FIELD_RE = /^([A-Za-zА-Яа-яЁё][\wА-Яа-яЁё]*):/;

export class QueryError extends Error {}

/** Английские операторы — в любом регистре, русские — только заглавными, чтобы «и», «не» оставались словами. */
function operatorOf(word) {
  const up = word.toUpperCase();
  if (up === 'AND' || up === '&&' || word === 'И') return 'and';
  if (up === 'OR' || up === '||' || word === 'ИЛИ') return 'or';
  if (up === 'NOT' || word === 'НЕ') return 'not';
  return null;
}

const hasOr = (q) => /\|/.test(q) || q.split(/\s+/).some((w) => operatorOf(w) === 'or');

function tokenize(q) {
  const tokens = [];
  let i = 0;
  while (i < q.length) {
    const ch = q[i];
    if (/\s/.test(ch)) {
      i++;
      continue;
    }
    if (ch === '(' || ch === ')') {
      tokens.push({ kind: ch === '(' ? 'lparen' : 'rparen' });
      i++;
      continue;
    }
    if (ch === '|') {
      tokens.push({ kind: 'or' });
      i++;
      continue;
    }
    // «-» и «!» в начале условия — отрицание.
    if ((ch === '-' || ch === '!') && i + 1 < q.length && !/\s/.test(q[i + 1])) {
      tokens.push({ kind: 'not' });
      i++;
      continue;
    }
    const field = FIELD_RE.exec(q.slice(i));
    if (field) i += field[0].length;
    let value = '';
    const quoted = q[i] === '"';
    if (quoted) {
      const end = q.indexOf('"', i + 1);
      value = end === -1 ? q.slice(i + 1) : q.slice(i + 1, end);
      i = end === -1 ? q.length : end + 1;
    } else {
      const start = i;
      while (i < q.length && !/[\s()]/.test(q[i])) i++;
      value = q.slice(start, i);
    }
    if (!field && !quoted) {
      const op = operatorOf(value);
      if (op) {
        tokens.push({ kind: op });
        continue;
      }
    }
    if (value || field) tokens.push({ kind: 'term', field: field ? field[1] : null, value: value.slice(0, MAX_VALUE) });
  }
  return tokens;
}

/**
 * Разбор строки в дерево условий. Пустая строка — null (без фильтра).
 * fields: { имя: [синонимы] } — каждое условие получает каноническое имя поля.
 */
export function parseQuery(q, fields = {}) {
  const alias = new Map();
  for (const [name, more] of Object.entries(fields)) {
    alias.set(name.toLowerCase(), name);
    for (const a of more || []) alias.set(a.toLowerCase(), name);
  }
  const tokens = tokenize(q || '');
  let pos = 0;
  let terms = 0;
  const syntax = (message) => new QueryError('Ошибка в запросе: ' + message);

  function parseOr(depth) {
    const nodes = [];
    const first = parseAnd(depth);
    if (first) nodes.push(first);
    while (tokens[pos]?.kind === 'or') {
      pos++;
      const next = parseAnd(depth);
      if (!next) throw syntax('после OR нужно условие');
      nodes.push(next);
    }
    if (!nodes.length) return null;
    return nodes.length === 1 ? nodes[0] : { type: 'or', nodes };
  }

  function parseAnd(depth) {
    const nodes = [];
    for (;;) {
      const token = tokens[pos];
      if (!token || token.kind === 'or' || token.kind === 'rparen') break;
      if (token.kind === 'and') {
        pos++;
        continue;
      }
      const node = parseUnary(depth);
      if (node) nodes.push(node);
    }
    if (!nodes.length) return null;
    return nodes.length === 1 ? nodes[0] : { type: 'and', nodes };
  }

  function parseUnary(depth) {
    if (depth > MAX_DEPTH) throw syntax('слишком глубокая вложенность скобок');
    const token = tokens[pos];
    if (!token) return null;
    if (token.kind === 'not') {
      pos++;
      const node = parseUnary(depth + 1);
      if (!node) throw syntax('после NOT нужно условие');
      return { type: 'not', node };
    }
    if (token.kind === 'lparen') {
      pos++;
      const node = parseOr(depth + 1);
      if (tokens[pos]?.kind !== 'rparen') throw syntax('не закрыта скобка');
      pos++;
      return node;
    }
    if (token.kind === 'term') {
      pos++;
      let field = null;
      if (token.field) {
        field = alias.get(token.field.toLowerCase()) || null;
        if (!field) throw new QueryError(`Неизвестное поле «${token.field}». Доступны: ${Object.keys(fields).join(', ')}`);
      }
      if (!token.value) throw syntax(`у поля ${token.field} нет значения`);
      if (++terms > MAX_TERMS) throw syntax(`не больше ${MAX_TERMS} условий`);
      return { type: 'term', term: { field, value: token.value } };
    }
    throw syntax('лишняя закрывающая скобка');
  }

  const root = parseOr(0);
  if (pos < tokens.length) throw syntax('лишняя закрывающая скобка');
  return root;
}

/** Проверить дерево: каждое условие проверяет вызывающий (term → boolean), связки — здесь. */
export function evaluate(node, term) {
  if (!node) return true;
  switch (node.type) {
    case 'term': return !!term(node.term);
    case 'not': return !evaluate(node.node, term);
    case 'and': return node.nodes.every((n) => evaluate(n, term));
    default: return node.nodes.some((n) => evaluate(n, term));
  }
}

export const foldYo = (s) => s.replace(/ё/g, 'е').replace(/Ё/g, 'Е');

const cache = new Map();
/** Значение в регулярное выражение: `*` — любая подстрока; без `*` и с contains — поиск подстроки. */
function likeRegExp(value, contains = false) {
  const key = (contains ? '1' : '0') + value;
  let re = cache.get(key);
  if (!re) {
    const body = value.toLowerCase().split('*').map((p) => p.replace(/[.+?^${}()|[\]\\]/g, '\\$&')).join('.*');
    re = new RegExp(contains && !value.includes('*') ? body : `^${body}$`, 'i');
    if (cache.size > 500) cache.clear();
    cache.set(key, re);
  }
  return re;
}

/** Текст совпадает со значением: точно (без учёта регистра и «ё») или по маске. */
export function textMatch(text, value) {
  return text != null && text !== '' && likeRegExp(foldYo(value)).test(foldYo(String(text)));
}

/** Текст содержит подстроку (или совпадает с маской). */
export function textContains(text, value) {
  return text != null && likeRegExp(foldYo(value), true).test(foldYo(String(text)));
}

const NUMERIC = /^(>=|<=|>|<)?\s*(-?\d+(?:[.,]\d+)?)$/;

/** Число против условия: «>5», «<=10», «3». Не число — false. */
export function numberMatch(number, value) {
  const m = NUMERIC.exec(value.trim());
  if (!m || number == null || number === '' || Number.isNaN(Number(number))) return false;
  const n = Number(number);
  const v = Number(m[2].replace(',', '.'));
  switch (m[1]) {
    case '>': return n > v;
    case '>=': return n >= v;
    case '<': return n < v;
    case '<=': return n <= v;
    default: return n === v;
  }
}

/**
 * Склеить условия через AND. Часть с OR берётся в скобки: иначе «a OR b» + «статус:x» прочиталось бы как
 * «a OR (b AND статус:x)».
 */
export function andQuery(...parts) {
  const kept = parts.map((p) => (p || '').trim()).filter(Boolean);
  if (kept.length < 2) return kept[0] ?? '';
  return kept.map((p) => (hasOr(p.replace(/"[^"]*"/g, '')) ? `(${p})` : p)).join(' ');
}

/**
 * Добавить условие «поле:значение» (клик по значению в таблице, карточке или топе). Условие по тому же полю
 * заменяется; исключения (negate) копятся. Если в строке есть скобки или OR — условие просто дописывается.
 */
export function withTerm(q, field, value, negate = false) {
  const text = String(value);
  const quoted = /[\s()|"]/.test(text) ? `"${text.replace(/"/g, '')}"` : text;
  const term = `${negate ? '-' : ''}${field}:${quoted}`;
  const plain = q.replace(/"[^"]*"/g, '');
  if (/[()|]/.test(plain) || plain.split(/\s+/).some((w) => operatorOf(w))) {
    return hasOr(plain) ? `(${q.trim()}) ${term}` : `${q.trim()} ${term}`;
  }
  const parts = q.match(/-?(?:[\wА-Яа-яЁё]+:)?(?:"[^"]*"|\S+)/g) ?? [];
  const lower = field.toLowerCase() + ':';
  const kept = parts.filter((part) => part !== term && (negate || !part.toLowerCase().startsWith(lower)));
  return [...kept, term].join(' ');
}
