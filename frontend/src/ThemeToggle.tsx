import { useState } from 'react';

export function ThemeToggle() {
  const [dark, setDark] = useState(() => document.documentElement.dataset.theme === 'dark');
  function toggle() {
    const next = !dark;
    const theme = next ? 'dark' : 'light';
    document.documentElement.dataset.theme = theme;
    document.querySelector('meta[name="theme-color"]')?.setAttribute('content', next ? '#111315' : '#ffffff');
    try { localStorage.setItem('market-signal-theme', theme); } catch { /* Theme still works when storage is unavailable. */ }
    setDark(next);
  }
  return <button className="theme-toggle" onClick={toggle} aria-label={dark ? '切換為淺色模式' : '切換為深色模式'} title={dark ? '切換為淺色模式' : '切換為深色模式'}>
    <svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true">
      {dark ? <><circle cx="12" cy="12" r="4" /><path d="M12 2v2m0 16v2M2 12h2m16 0h2M5 5l1.5 1.5m11 11L19 19M5 19l1.5-1.5m11-11L19 5" /></> : <path d="M20.5 14A8.7 8.7 0 0 1 10 3.5 8.7 8.7 0 1 0 20.5 14Z" />}
    </svg>
  </button>;
}
