CREATE TABLE IF NOT EXISTS MarketReferenceInstruments (
 Id BIGINT PRIMARY KEY AUTO_INCREMENT,
 UserId BIGINT NOT NULL,
 Symbol VARCHAR(64) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 Name VARCHAR(200) NOT NULL,
 Market VARCHAR(32) NOT NULL,
 UNIQUE KEY UQ_ReferenceInstrument(UserId,Symbol),
 UNIQUE KEY UQ_ReferenceInstrumentOwner(Id,UserId),
 FOREIGN KEY(UserId) REFERENCES Users(Id)
);
CREATE TABLE IF NOT EXISTS ProductMarketReferences (
 Id BIGINT PRIMARY KEY AUTO_INCREMENT,
 UserId BIGINT NOT NULL,
 ProductId BIGINT NOT NULL,
 ReferenceInstrumentId BIGINT NOT NULL,
 ReferenceType VARCHAR(32) CHARACTER SET ascii COLLATE ascii_bin NOT NULL,
 UNIQUE KEY UQ_ProductReference(UserId,ProductId,ReferenceType,ReferenceInstrumentId),
 FOREIGN KEY(UserId) REFERENCES Users(Id),
 FOREIGN KEY(ProductId) REFERENCES Products(Id),
 FOREIGN KEY(ReferenceInstrumentId,UserId) REFERENCES MarketReferenceInstruments(Id,UserId),
 CHECK(ReferenceType IN ('UNDERLYING','BROAD_MARKET','SECTOR'))
);
