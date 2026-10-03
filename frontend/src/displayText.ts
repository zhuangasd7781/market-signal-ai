import { analysisStatusText } from './analysisStatus';
import type { Action, AnalysisView } from './types';

export const actionLabels: Record<Action, string> = { ADD: '加碼', HOLD: '持有', REDUCE: '減碼', EXIT: '出場' };
export const referenceTypeLabels: Record<string, string> = { UNDERLYING: '追蹤標的', BROAD_MARKET: '大盤', SECTOR: '產業' };
export const referenceTypeText = (value: string) => referenceTypeLabels[value] ?? '其他參考類型';
const analysisLabels: Record<string, string> = {
  UNKNOWN: '資料不足',
  ACTIVE: '已確認', EXPECTED: '預期中', INVALIDATED: '已失效', UNVERIFIED: '尚未確認',
  AVAILABLE: '可用', PARTIAL: '部分可用', UNAVAILABLE: '無法取得',
  OPEN: '開市', CLOSED: '休市', COMPLETE: '完整', CLOSE_ONLY: '僅收盤價', INSUFFICIENT: '資料不足', INTRADAY: '盤中行情', LATEST_CLOSED_QUOTE: '休市最近行情',
  STALE: '非最新交易日', MISSING: '未提供', FALLBACK: '替代來源',
};
export const analysisValueText = (value: string) => analysisLabels[value] ?? value;
export const marketText = (value: string) => ({ TW: '台灣', US: '美國' }[value] ?? value);
export const assetTypeText = (value: string) => ({ Stock: '股票', ETF: 'ETF', Index: '指數' }[value] ?? value);
export function hasEffectiveRootEvent(event: AnalysisView['result']['rootEvent']) {
  return ['ACTIVE', 'EXPECTED'].includes(event.status) && event.direction !== 'UNKNOWN' && !!event.summary.trim();
}
// Display-only substitutions; the original analysis, input, schema and enum values remain unchanged.
export function analysisText(value: string) {
  return value.replace(/\b[A-Z][A-Z_]+\b/g,
    token => actionLabels[token as Action] ?? referenceTypeLabels[token] ?? analysisStatusText(token) ?? analysisLabels[token] ?? token);
}
const errors: Record<string, string> = {
  'Unknown AI provider.': '找不到指定的 AI 服務。',
  'Enabled is required.': '請設定是否啟用分析。',
  'Model must be a valid model ID of at most 100 characters.': '請輸入有效的 Model ID，最多 100 個字元。',
  'Claude currently supports mock-v1 only; OpenAI and DeepSeek require a real model ID.': 'Claude 目前僅支援示範模型 mock-v1；GPT 與 DeepSeek 請使用實際 Model ID。',
  'Prompt content must contain 1 to 30000 characters.': 'Prompt 內容須為 1 至 30000 個字元。',
  'Enabled and Times are required.': '請設定是否啟用自動分析及分析時間。',
  'Time must use HH:mm (Asia/Taipei).': '請使用 24 小時制的時:分格式（台北時間）。',
  'At most 24 analysis times are allowed.': '最多可設定 24 個分析時間。',
  'Duplicate analysis times are not allowed.': '分析時間不可重複。',
  'Reference name (1–200 characters) and market (1–32 characters) are required.': '請填寫標的名稱（1 至 200 個字元）及市場（1 至 32 個字元）。',
  'Provide an exact Yahoo ticker of 1–64 uppercase letters/digits or . ^ = _ - characters.': '請填寫完整 Yahoo Symbol，限 1 至 64 個大寫英文字母、數字或 . ^ = _ -。',
  'ReferenceType must be UNDERLYING, BROAD_MARKET or SECTOR.': '參考類型須為追蹤標的、大盤或產業。',
  'Reference instrument not found.': '找不到市場參考標的。',
  'Reference instrument already exists.': '此市場參考標的已存在。',
  'Remove product mappings before deleting this instrument.': '此標的仍有商品使用，請先移除商品的參考設定。',
  'Product reference not found.': '找不到此商品的參考設定。',
  'Product reference already exists.': '此商品已有相同的參考設定。',
  'Missing request header.': 'API 請求缺少必要資訊，請重新整理後再試。',
  'Failed to fetch': '無法連線至服務，請確認網路後再試。',
  'NetworkError when attempting to fetch resource.': '無法連線至服務，請確認網路後再試。',
  'A requested provider is disabled. Enable it in AI settings first.': '指定的 AI 尚未啟用，請先至 AI 設定開啟。',
  'AI analysis failed.': 'AI 分析失敗，請稍後再試。',
  'AI timeout.': 'AI 分析逾時，請稍後再試。',
};
export function errorText(message: string, status?: number) {
  if (errors[message]) return errors[message];
  if (/Intraday quote is available through Yahoo Taiwan/.test(message)) return '可取得 Yahoo 台灣盤中行情；日線歷史尚未驗證，無法據此判斷歷史趨勢。';
  if (/YAHOO_TW supplied only 0 daily bars/.test(message)) return '盤中行情可用，缺少日線歷史；期貨夜盤與商品行情日期可能不同，請留意行情時間。';
  const http = message.match(/HTTP (\d{3})/);
  if (http) return `資料服務回應錯誤（HTTP ${http[1]}），請稍後再試。`;
  if (/Yahoo reference (quote|history) unavailable/.test(message)) return '目前無法取得 Yahoo 市場參考資料。';
  if (/Reference history is partial|Yahoo sparse history/.test(message)) return 'Yahoo 歷史資料不完整。';
  if (/[\u3400-\u9fff]/.test(message)) return analysisText(message);
  return status === 404 ? '找不到指定資料。' : '操作未完成，請稍後再試。';
}
