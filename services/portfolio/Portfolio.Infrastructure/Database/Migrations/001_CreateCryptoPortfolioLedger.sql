IF OBJECT_ID(N'dbo.CryptoPortfolioLedger', N'U') IS NULL
CREATE TABLE dbo.CryptoPortfolioLedger (
    AccountKey char(64) NOT NULL PRIMARY KEY,
    Payload nvarchar(max) NOT NULL CHECK (ISJSON(Payload) = 1),
    UpdatedAt datetimeoffset NOT NULL
);
