const logos: Record<string, string> = {
  deepseek: '/provider-logos/deepseek.svg',
  openai: '/provider-logos/openai.svg',
  claude: '/provider-logos/claude.svg',
};

export function ProviderLogo({ code }: { code: string }) {
  const normalized = code.toLowerCase();
  const source = logos[normalized];
  if (!source) return <span className="provider-dot" aria-hidden="true" />;
  return <span
    className={`provider-logo provider-logo--${normalized}`}
    aria-hidden="true"
    style={{ maskImage: `url("${source}")`, WebkitMaskImage: `url("${source}")` }}
  />;
}
