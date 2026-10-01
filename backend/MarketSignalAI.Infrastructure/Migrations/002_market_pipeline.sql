-- Apply after 001_initial.sql. All DATETIME values are UTC.
CREATE TABLE IF NOT EXISTS MarketSnapshots (
 Id BIGINT PRIMARY KEY AUTO_INCREMENT,
 ProductId BIGINT NOT NULL,
 Price DECIMAL(18,6) NOT NULL,
 `Open` DECIMAL(18,6) NOT NULL,
 High DECIMAL(18,6) NOT NULL,
 Low DECIMAL(18,6) NOT NULL,
 PreviousClose DECIMAL(18,6) NOT NULL,
 Volume BIGINT NOT NULL,
 MarketTime DATETIME(6) NOT NULL,
 FetchedAt DATETIME(6) NOT NULL,
 FOREIGN KEY(ProductId) REFERENCES Products(Id),
 INDEX IX_MarketSnapshots_Latest(ProductId,FetchedAt DESC,Id DESC),
 CHECK(Price>0), CHECK(`Open`>0), CHECK(High>0), CHECK(Low>0), CHECK(PreviousClose>0), CHECK(Volume>=0)
);
CREATE TABLE IF NOT EXISTS TradingDays (
 TradeDate DATE NOT NULL,
 Market VARCHAR(32) NOT NULL,
 Status ENUM('OPEN','CLOSED') NOT NULL,
 CheckedAt DATETIME(6) NOT NULL,
 PRIMARY KEY(TradeDate,Market)
);
