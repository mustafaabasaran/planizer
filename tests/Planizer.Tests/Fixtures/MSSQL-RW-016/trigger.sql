-- Fixed-width bytes: int 4 + char(8000) 8000 + decimal(19,4) 9 + datetime2 8
-- + uniqueidentifier 16 + bit 1 + char(50) 50 = 8088 > 8060. nvarchar is excluded.
-- expect: MSSQL-RW-016 severity=Warning line=4
CREATE TABLE dbo.WideTable
(
    Id int NOT NULL,
    Payload char(8000) NOT NULL,
    Amount decimal(19, 4) NOT NULL,
    CreatedAt datetime2 NOT NULL,
    RowGuid uniqueidentifier NOT NULL,
    IsActive bit NOT NULL,
    Extra char(50) NULL,
    Comment nvarchar(1000) NULL
);
