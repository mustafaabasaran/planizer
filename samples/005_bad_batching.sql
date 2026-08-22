-- Migration 005: payment-type lookup and order payment columns.
-- This is what a migration looks like before review: a column added in a batch is updated
-- in the same batch (compile-time error 207), a variable declared before GO is used after it,
-- the transaction spans GO, nothing is guarded against a second run, Turkish literals lack the
-- N prefix, and a lookup table is read from another database by name.

USE [Orders];
GO

DECLARE @now datetime2(3) = SYSUTCDATETIME();

BEGIN TRANSACTION;

CREATE TABLE dbo.PaymentType
(
    Id   tinyint      NOT NULL CONSTRAINT PK_PaymentType PRIMARY KEY,
    Name nvarchar(50) NOT NULL
);

INSERT INTO dbo.PaymentType (Id, Name)
VALUES (1, 'Kredi Kartı'),
       (2, 'Havale / EFT'),
       (3, 'Kapıda Ödeme');

ALTER TABLE dbo.Orders ADD PaymentTypeId tinyint NULL;

UPDATE dbo.Orders
SET    PaymentTypeId = 1
WHERE  PaidAt IS NOT NULL;
GO

UPDATE o
SET    o.PaymentTypeId = l.PaymentTypeId
FROM   dbo.Orders AS o
JOIN   [LegacyDb].dbo.LegacyPaymentMap AS l ON l.OrderNumber = o.OrderNumber
WHERE  o.PaymentTypeId IS NULL
  AND  o.CreatedAt < @now;

COMMIT TRANSACTION;
GO
