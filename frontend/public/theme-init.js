// Apply the stored choice before styles paint, including on a direct detail-page load.
(() => {
  let theme = 'light';
  try { if (localStorage.getItem('market-signal-theme') === 'dark') theme = 'dark'; } catch {}
  document.documentElement.dataset.theme = theme;
  document.querySelector('meta[name="theme-color"]')?.setAttribute('content', theme === 'dark' ? '#111315' : '#ffffff');
})();
