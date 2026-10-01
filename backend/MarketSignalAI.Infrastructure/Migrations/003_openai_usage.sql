-- Apply after 002_market_pipeline.sql. Token counts are nullable only for failures with unknown usage.
CREATE TABLE IF NOT EXISTS AIAnalysisUsage (
 AnalysisId BIGINT PRIMARY KEY,
 InputTokens BIGINT NOT NULL,
 OutputTokens BIGINT NOT NULL,
 FOREIGN KEY(AnalysisId) REFERENCES AIAnalysisResults(Id),
 CHECK(InputTokens>=0), CHECK(OutputTokens>=0)
);
CREATE TABLE IF NOT EXISTS AIProviderFailures (
 Id BIGINT PRIMARY KEY AUTO_INCREMENT,
 UserId BIGINT NOT NULL,
 ProductId BIGINT NOT NULL,
 AIProviderId BIGINT NOT NULL,
 Model VARCHAR(100) NULL,
 ReasoningEffort VARCHAR(16) NULL,
 Error VARCHAR(500) NOT NULL,
 InputTokens BIGINT NULL,
 OutputTokens BIGINT NULL,
 CreatedAt DATETIME(6) NOT NULL,
 FOREIGN KEY(UserId) REFERENCES Users(Id),
 FOREIGN KEY(ProductId) REFERENCES Products(Id),
 FOREIGN KEY(AIProviderId) REFERENCES AIProviders(Id),
 INDEX IX_ProviderFailures(UserId,ProductId,CreatedAt DESC),
 CHECK(InputTokens IS NULL OR InputTokens>=0), CHECK(OutputTokens IS NULL OR OutputTokens>=0)
);
