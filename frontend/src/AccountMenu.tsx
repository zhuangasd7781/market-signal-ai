import { useEffect, useRef, useState } from 'react';
import { Link, useLocation, useNavigate } from 'react-router-dom';

const settings = [
  { path: '/settings/ai', label: 'AI 設定' },
  { path: '/settings/schedule', label: '分析排程' },
  { path: '/settings/prompts', label: 'Prompt 設定' },
  { path: '/settings/reference-instruments', label: 'Reference Instruments' },
];

export function AccountMenu() {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null);
  const trigger = useRef<HTMLButtonElement>(null);
  const location = useLocation();
  const navigate = useNavigate();

  useEffect(() => { setOpen(false); }, [location.pathname]);
  useEffect(() => {
    if (!open) return;
    function onPointerDown(event: PointerEvent) {
      if (!root.current?.contains(event.target as Node)) setOpen(false);
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === 'Escape') { setOpen(false); trigger.current?.focus(); }
    }
    document.addEventListener('pointerdown', onPointerDown);
    document.addEventListener('keydown', onKeyDown);
    return () => {
      document.removeEventListener('pointerdown', onPointerDown);
      document.removeEventListener('keydown', onKeyDown);
    };
  }, [open]);

  function leaveDemo() {
    sessionStorage.removeItem('demo-session');
    setOpen(false);
    navigate('/login');
  }

  return <div className="account-menu" ref={root}>
    <button className="account-trigger" ref={trigger} type="button" aria-expanded={open}
      aria-controls="account-settings-menu" aria-label="Demo 投資人，開啟帳戶選單"
      onClick={() => setOpen(value => !value)}>
      <span className="avatar" aria-hidden="true">D</span><span className="account-name">Demo 投資人</span><span className="menu-chevron" aria-hidden="true">▾</span>
    </button>
    {open && <div id="account-settings-menu" className="account-menu-popover" role="navigation" aria-label="帳戶與設定">
      <p className="account-menu-title">設定</p>
      {settings.map(item => <Link key={item.path} className="account-menu-link" to={item.path}
        aria-current={location.pathname === item.path ? 'page' : undefined} onClick={() => setOpen(false)}>{item.label}</Link>)}
      <div className="account-menu-divider" />
      <button className="account-menu-leave" type="button" onClick={leaveDemo}>離開示範</button>
    </div>}
  </div>;
}
