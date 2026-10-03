import { analysisStatusFields, analysisStatusPresentation, type AnalysisStatusValues } from './analysisStatus';

export function AnalysisStatusGrid({ analysis }: { analysis: AnalysisStatusValues }) {
  return <dl className="context-grid analysis-status-grid" aria-label="分析狀態">
    {analysisStatusFields.map(({ key, label }) => {
      const status = analysisStatusPresentation(key, analysis[key]);
      return <div key={key}><dt>{label}</dt><dd><span className={`analysis-status analysis-status--${status.tone}`} data-status-field={key}>{status.label}</span></dd></div>;
    })}
  </dl>;
}
