export type Action = 'ADD' | 'HOLD' | 'REDUCE' | 'EXIT';
export interface Product { id: number; symbol: string; name: string; market: string; assetType: string; isLeveraged: boolean; quantityUnit: string; unitSize: number }
export interface Provider { id: number; code: string; displayName: string; sortOrder: number }
export interface Signal { provider: string; action: Action; quantity: number | null; changed: boolean; analyzedAt: string }
export interface WatchRow { product: Product; signals: Signal[]; lastAnalyzedAt: string | null }
export interface Watchlist { providers: Provider[]; items: WatchRow[]; isMock: boolean }
export interface Position { quantity: number; averageCost: number; updatedAt: string }
export interface MarketQuote { symbol: string; price: number; open: number; high: number; low: number; previousClose: number; volume: number; marketTime: string; fetchedAt: string }
export interface AnalysisView {
  id: number; provider: string; displayName: string; model: string; createdAt: string;
  result: {
    action: Action; quantity: number | null; confidence: number;
    rootEvent: { direction: string; status: string; summary: string };
    marketRegime: string; trend: string; momentum: string; volume: string; riskReward: string;
    reasons: string[]; risks: string[]; bullCase: string[]; bearCase: string[]; invalidation: string;
    nextActions: { condition: string; action: Action; quantity: number | null }[];
  };
}
