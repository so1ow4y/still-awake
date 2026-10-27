// Единая точка импорта Preact + htm (вендорный файл лежит локально — панель работает без интернета).
export {
  html, render, h,
  useState, useEffect, useLayoutEffect, useRef, useMemo, useCallback, useErrorBoundary,
} from '../../vendor/htm-preact-standalone.mjs';
