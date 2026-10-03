import type { AnalysisView } from './types';

export const analysisStatusFields = [
  { key: 'marketRegime', label: '市場狀態' }, { key: 'trend', label: '趨勢' },
  { key: 'momentum', label: '動能' }, { key: 'volume', label: '量價' },
  { key: 'riskReward', label: '風險報酬' },
] as const;
export type AnalysisStatusField = typeof analysisStatusFields[number]['key'];
export type StatusTone = 'positive' | 'negative' | 'neutral' | 'warning' | 'unknown';
type StatusPresentation = { label: string; tone: StatusTone };
const statuses: Record<AnalysisStatusField, Record<string, StatusPresentation>> = {
  marketRegime: { BULL: { label: '多頭', tone: 'positive' }, BEAR: { label: '空頭', tone: 'negative' }, SIDEWAYS: { label: '盤整', tone: 'neutral' } },
  trend: { BULLISH: { label: '偏多', tone: 'positive' }, BEARISH: { label: '偏空', tone: 'negative' }, NEUTRAL: { label: '中性', tone: 'neutral' } },
  momentum: { POSITIVE: { label: '正向', tone: 'positive' }, NEGATIVE: { label: '負向', tone: 'negative' }, NEUTRAL: { label: '中性', tone: 'neutral' } },
  volume: { SUPPORTIVE: { label: '支持', tone: 'positive' }, WARNING: { label: '警示', tone: 'warning' }, NEUTRAL: { label: '中性', tone: 'neutral' } },
  riskReward: { FAVORABLE: { label: '偏有利', tone: 'positive' }, UNFAVORABLE: { label: '偏不利', tone: 'negative' }, NEUTRAL: { label: '中性', tone: 'neutral' }, UNKNOWN: { label: '無法判定', tone: 'unknown' } },
};
const common: Record<string, StatusPresentation> = {
  UNKNOWN: { label: '資料不足', tone: 'unknown' }, UNAVAILABLE: { label: '無法判定', tone: 'unknown' },
  MISSING: { label: '未提供', tone: 'unknown' }, '': { label: '未提供', tone: 'unknown' },
  PARTIAL: { label: '資料部分不足', tone: 'warning' }, WARNING: { label: '警示', tone: 'warning' },
};
export function analysisStatusPresentation(field: AnalysisStatusField, value: string | null | undefined): StatusPresentation {
  return statuses[field][value ?? ''] ?? common[value ?? ''] ?? { label: '無法判定', tone: 'unknown' };
}
export type AnalysisStatusValues = Pick<AnalysisView['result'], AnalysisStatusField>;

export function analysisStatusText(value: string): string | undefined {
  return common[value]?.label ?? Object.values(statuses).map(field => field[value]?.label).find(Boolean);
}
