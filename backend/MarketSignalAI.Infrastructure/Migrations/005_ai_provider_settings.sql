CREATE TABLE IF NOT EXISTS AIProviderSettings (
 UserId BIGINT NOT NULL,
 Provider VARCHAR(32) NOT NULL,
 Enabled BOOLEAN NOT NULL,
 ConfiguredModel VARCHAR(100) NOT NULL,
 PRIMARY KEY(UserId,Provider),
 FOREIGN KEY(UserId) REFERENCES Users(Id),
 CHECK(Provider IN ('openai','deepseek','claude'))
);
