ALTER TABLE AIAnalysisResults ADD COLUMN NewsContextId BIGINT NULL, ADD INDEX IX_AIAnalysisResults_NewsContextId (NewsContextId);
