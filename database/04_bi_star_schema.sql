/* Divakar.GloveOps — reporting star schema for Power BI (schema "bi").
   Facts: daily production per plant/line and daily sales.
   Data is generated (fictional) for 2025-01-01 .. 2026-10-31. */
USE DivakarGloveOps;
GO
IF SCHEMA_ID('bi') IS NULL EXEC('CREATE SCHEMA bi');
GO
DROP VIEW  IF EXISTS bi.vSampleRequest;
DROP TABLE IF EXISTS bi.FactSales, bi.FactProduction, bi.UserPlant, bi.DimDate, bi.DimPlant, bi.DimProduct, bi.DimCustomer;
GO

CREATE TABLE bi.DimDate (
    DateKey    INT PRIMARY KEY,          -- 20260130
    [Date]     DATE NOT NULL UNIQUE,
    [Year]     INT NOT NULL,
    [Quarter]  TINYINT NOT NULL,
    [Month]    TINYINT NOT NULL,
    MonthName  VARCHAR(10) NOT NULL,
    YearMonth  CHAR(7) NOT NULL,         -- 2026-01
    IsWeekend  BIT NOT NULL
);

CREATE TABLE bi.DimPlant    (PlantKey INT PRIMARY KEY, PlantCode VARCHAR(10), PlantName NVARCHAR(100), Country NVARCHAR(50));
CREATE TABLE bi.DimProduct  (ProductKey INT PRIMARY KEY, Sku VARCHAR(30), ProductName NVARCHAR(150), Material VARCHAR(10), Category NVARCHAR(50), PlantKey INT);
CREATE TABLE bi.DimCustomer (CustomerKey INT PRIMARY KEY, CustomerName NVARCHAR(150), Country NVARCHAR(50));

/* Row-level security mapping: which Power BI user sees which plant */
CREATE TABLE bi.UserPlant (UserEmail VARCHAR(150) NOT NULL, PlantKey INT NOT NULL, PRIMARY KEY (UserEmail, PlantKey));

CREATE TABLE bi.FactProduction (
    DateKey      INT NOT NULL REFERENCES bi.DimDate(DateKey),
    PlantKey     INT NOT NULL REFERENCES bi.DimPlant(PlantKey),
    ProductKey   INT NOT NULL REFERENCES bi.DimProduct(ProductKey),
    LineNumber       TINYINT NOT NULL,
    OutputBoxes  INT NOT NULL,
    DefectBoxes  INT NOT NULL,
    DowntimeMin  INT NOT NULL
);

CREATE TABLE bi.FactSales (
    DateKey     INT NOT NULL REFERENCES bi.DimDate(DateKey),
    CustomerKey INT NOT NULL REFERENCES bi.DimCustomer(CustomerKey),
    ProductKey  INT NOT NULL REFERENCES bi.DimProduct(ProductKey),
    Boxes       INT NOT NULL,
    AmountUSD   DECIMAL(12,2) NOT NULL
);
GO

/* ---- dimensions ---- */
;WITH n AS (SELECT TOP (DATEDIFF(DAY, '2025-01-01', '2026-10-31') + 1)
                   ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) - 1 AS i
            FROM sys.all_objects a CROSS JOIN sys.all_objects b)
INSERT bi.DimDate
SELECT CONVERT(INT, FORMAT(d, 'yyyyMMdd')), d, YEAR(d), DATEPART(QUARTER, d), MONTH(d),
       LEFT(DATENAME(MONTH, d), 10), FORMAT(d, 'yyyy-MM'),
       IIF(DATEPART(WEEKDAY, d) IN (1, 7), 1, 0)   -- assumes default DATEFIRST 7
FROM (SELECT DATEADD(DAY, i, CAST('2025-01-01' AS DATE)) AS d FROM n) x;

INSERT bi.DimPlant    SELECT PlantId, Code, Name, Country FROM dbo.Plant;
INSERT bi.DimProduct  SELECT ProductId, Sku, Name, Material, Category, PlantId FROM dbo.Product;
INSERT bi.DimCustomer SELECT CustomerId, Name, Country FROM dbo.Customer;

INSERT bi.UserPlant VALUES ('ming@gloveops.demo', 1), ('wei@gloveops.demo', 2), ('linh@gloveops.demo', 3);

/* ---- production: every day x plant x 4 lines, each line runs one product of its plant ---- */
INSERT bi.FactProduction (DateKey, PlantKey, ProductKey, LineNumber, OutputBoxes, DefectBoxes, DowntimeMin)
SELECT d.DateKey, p.PlantKey, pr.ProductKey, l.LineNumber, o.OutputBoxes,
       CAST(o.OutputBoxes * (0.006 + p.PlantKey * 0.003 + ABS(CHECKSUM(NEWID())) % 15 / 1000.0) AS INT),
       ABS(CHECKSUM(NEWID())) % 90
FROM bi.DimDate d
CROSS JOIN bi.DimPlant p
CROSS JOIN (VALUES (1),(2),(3),(4)) l(LineNumber)
CROSS APPLY (SELECT TOP (1) ProductKey FROM bi.DimProduct x
             WHERE x.PlantKey = p.PlantKey
             ORDER BY (x.ProductKey * 7 + l.LineNumber) % 5) pr
CROSS APPLY (SELECT IIF(d.IsWeekend = 1, 600, 1100) + ABS(CHECKSUM(NEWID())) % 400 AS OutputBoxes) o;

/* ---- sales: about 12 order lines per weekday ---- */
INSERT bi.FactSales (DateKey, CustomerKey, ProductKey, Boxes, AmountUSD)
SELECT d.DateKey, c.CustomerKey, pr.ProductKey, b.Boxes,
       b.Boxes * CASE pr.Material WHEN 'NBR' THEN 4.20 WHEN 'LATEX' THEN 5.10 WHEN 'PVC' THEN 2.60 ELSE 1.90 END
FROM bi.DimDate d
CROSS JOIN (VALUES (1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12)) k(n)
CROSS APPLY (SELECT TOP (1) CustomerKey FROM bi.DimCustomer ORDER BY CHECKSUM(NEWID(), k.n, d.DateKey)) c
CROSS APPLY (SELECT TOP (1) ProductKey, Material FROM bi.DimProduct ORDER BY CHECKSUM(NEWID(), k.n, d.DateKey)) pr
CROSS APPLY (SELECT 50 + ABS(CHECKSUM(NEWID())) % 950 AS Boxes) b
WHERE d.IsWeekend = 0;
GO

/* Live sample-request data for the "Sample Requests" report page */
CREATE VIEW bi.vSampleRequest AS
SELECT r.RequestNo, r.Status, r.Qty, r.Size,
       CONVERT(INT, FORMAT(r.CreatedAt, 'yyyyMMdd')) AS DateKey,
       r.CustomerId AS CustomerKey, r.ProductId AS ProductKey,
       DATEDIFF(MINUTE, r.SubmittedAt, r.ClosedAt) / 60.0 AS LeadHours
FROM dbo.SampleRequest r;
GO

SELECT 'FactProduction' AS TableName, COUNT(*) AS Rows FROM bi.FactProduction
UNION ALL SELECT 'FactSales', COUNT(*) FROM bi.FactSales
UNION ALL SELECT 'DimDate', COUNT(*) FROM bi.DimDate;
GO
