CREATE TABLE IF NOT EXISTS PromptVersions (
    Id BIGINT NOT NULL AUTO_INCREMENT,
    UserId BIGINT NOT NULL,
    VersionNumber INT NOT NULL,
    Content MEDIUMTEXT NOT NULL,
    CreatedAt DATETIME(6) NOT NULL,
    PRIMARY KEY(Id),
    UNIQUE KEY UX_Prompt_UserVersion(UserId,VersionNumber),
    UNIQUE KEY UX_Prompt_UserId(UserId,Id),
    FOREIGN KEY(UserId) REFERENCES Users(Id)
);
CREATE TABLE IF NOT EXISTS UserActivePrompts (
    UserId BIGINT NOT NULL,
    PromptVersionId BIGINT NOT NULL,
    PRIMARY KEY(UserId),
    FOREIGN KEY(UserId,PromptVersionId) REFERENCES PromptVersions(UserId,Id)
);
