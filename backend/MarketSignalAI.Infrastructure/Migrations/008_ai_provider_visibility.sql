-- Applied once by MySqlAIProviderSettingsStore after checking column existence.
ALTER TABLE AIProviderSettings ADD COLUMN IsVisible BOOLEAN NOT NULL DEFAULT TRUE;
