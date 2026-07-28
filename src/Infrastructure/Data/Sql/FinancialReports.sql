/*
  Company financial reports — SQL Server procedures for SSRS / ERP UI.
  Parameterized by @CompanyId (Company 3 primary consumer).
*/
SET NOCOUNT ON;
GO

CREATE OR ALTER PROCEDURE dbo.usp_FinReport_PostedJournalLines
    @CompanyId INT
AS
BEGIN
    SET NOCOUNT ON;

    SELECT
        l.ChartOfAccountId,
        CAST(j.EntryDate AS date) AS EntryDate,
        SUM(l.Debit) AS Debit,
        SUM(l.Credit) AS Credit
    FROM dbo.JournalEntryLines AS l
    INNER JOIN dbo.JournalEntries AS j ON j.Id = l.JournalEntryId
    INNER JOIN dbo.ChartOfAccounts AS a ON a.Id = l.ChartOfAccountId
    WHERE j.CompanyId = @CompanyId
      AND j.Status = 2 /* Posted */
      AND j.IsDeleted = 0
      AND a.IsDeleted = 0
    GROUP BY l.ChartOfAccountId, CAST(j.EntryDate AS date)
    ORDER BY l.ChartOfAccountId, CAST(j.EntryDate AS date);
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Rpt_TrialBalance
    @CompanyId INT,
    @FromDate DATE,
    @ToDate DATE
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH Journal AS (
        SELECT
            l.ChartOfAccountId,
            SUM(CASE WHEN CAST(j.EntryDate AS date) < @FromDate THEN l.Debit ELSE 0 END) AS DebitBefore,
            SUM(CASE WHEN CAST(j.EntryDate AS date) < @FromDate THEN l.Credit ELSE 0 END) AS CreditBefore,
            SUM(CASE WHEN CAST(j.EntryDate AS date) BETWEEN @FromDate AND @ToDate THEN l.Debit ELSE 0 END) AS DebitPeriod,
            SUM(CASE WHEN CAST(j.EntryDate AS date) BETWEEN @FromDate AND @ToDate THEN l.Credit ELSE 0 END) AS CreditPeriod,
            SUM(CASE WHEN CAST(j.EntryDate AS date) <= @ToDate THEN l.Debit ELSE 0 END) AS DebitUpTo,
            SUM(CASE WHEN CAST(j.EntryDate AS date) <= @ToDate THEN l.Credit ELSE 0 END) AS CreditUpTo
        FROM dbo.JournalEntryLines AS l
        INNER JOIN dbo.JournalEntries AS j ON j.Id = l.JournalEntryId
        WHERE j.CompanyId = @CompanyId
          AND j.Status = 2
          AND j.IsDeleted = 0
        GROUP BY l.ChartOfAccountId
    ),
    Calc AS (
        SELECT
            a.Id AS AccountId,
            a.AccountNumber,
            a.AccountName,
            t.TypeName,
            a.TypeId,
            a.OpeningBalance,
            ISNULL(j.DebitBefore, 0) AS DebitBefore,
            ISNULL(j.CreditBefore, 0) AS CreditBefore,
            ISNULL(j.DebitPeriod, 0) AS PeriodDebit,
            ISNULL(j.CreditPeriod, 0) AS PeriodCredit,
            ISNULL(j.DebitUpTo, 0) AS DebitUpTo,
            ISNULL(j.CreditUpTo, 0) AS CreditUpTo,
            /* Journal delta: liability/equity credit-normal */
            CASE WHEN a.TypeId IN (2, 3)
                THEN ISNULL(j.CreditBefore, 0) - ISNULL(j.DebitBefore, 0)
                ELSE ISNULL(j.DebitBefore, 0) - ISNULL(j.CreditBefore, 0)
            END AS OpeningJournalDelta,
            CASE WHEN a.TypeId IN (2, 3)
                THEN ISNULL(j.CreditUpTo, 0) - ISNULL(j.DebitUpTo, 0)
                ELSE ISNULL(j.DebitUpTo, 0) - ISNULL(j.CreditUpTo, 0)
            END AS ClosingJournalDelta
        FROM dbo.ChartOfAccounts AS a
        LEFT JOIN dbo.AccountTypes AS t ON t.TypeId = a.TypeId
        LEFT JOIN Journal AS j ON j.ChartOfAccountId = a.Id
        WHERE a.CompanyId = @CompanyId
          AND a.IsActive = 1
          AND a.IsDeleted = 0
    ),
    Nets AS (
        SELECT
            *,
            OpeningBalance + OpeningJournalDelta AS OpeningNet,
            OpeningBalance + ClosingJournalDelta AS ClosingNet
        FROM Calc
    ),
    Display AS (
        SELECT
            *,
            /* Display normalize: AR inverted; liabilities except AP inverted */
            CASE
                WHEN AccountNumber = N'11110' THEN -OpeningNet
                WHEN TypeId = 2 AND AccountNumber <> N'20000' THEN -OpeningNet
                ELSE OpeningNet
            END AS DisplayOpening,
            CASE
                WHEN AccountNumber = N'11110' THEN -ClosingNet
                WHEN TypeId = 2 AND AccountNumber <> N'20000' THEN -ClosingNet
                ELSE ClosingNet
            END AS DisplayClosing
        FROM Nets
    )
    SELECT
        AccountId,
        AccountNumber,
        AccountName,
        TypeName,
        CAST(ROUND(DisplayOpening, 2) AS DECIMAL(18, 2)) AS OpeningBalance,
        CAST(ROUND(PeriodDebit, 2) AS DECIMAL(18, 2)) AS PeriodDebit,
        CAST(ROUND(PeriodCredit, 2) AS DECIMAL(18, 2)) AS PeriodCredit,
        CAST(ROUND(DisplayClosing, 2) AS DECIMAL(18, 2)) AS ClosingBalance,
        CAST(ROUND(
            CASE
                WHEN AccountNumber = N'11110' THEN CASE WHEN ClosingNet < 0 THEN ABS(ClosingNet) ELSE 0 END
                WHEN AccountNumber = N'20000' THEN CASE WHEN ClosingNet < 0 THEN ABS(ClosingNet) ELSE 0 END
                WHEN AccountNumber = N'30000' THEN CASE WHEN ClosingNet > 0 THEN ABS(ClosingNet) ELSE 0 END
                WHEN TypeId = 2 THEN CASE WHEN ClosingNet > 0 THEN ABS(ClosingNet) ELSE 0 END
                WHEN TypeId IN (3, 4) THEN CASE WHEN ClosingNet < 0 THEN ABS(ClosingNet) ELSE 0 END
                ELSE CASE WHEN ClosingNet > 0 THEN ABS(ClosingNet) ELSE 0 END
            END, 2) AS DECIMAL(18, 2)) AS ClosingDebit,
        CAST(ROUND(
            CASE
                WHEN AccountNumber = N'11110' THEN CASE WHEN ClosingNet >= 0 THEN ABS(ClosingNet) ELSE 0 END
                WHEN AccountNumber = N'20000' THEN CASE WHEN ClosingNet >= 0 THEN ABS(ClosingNet) ELSE 0 END
                WHEN AccountNumber = N'30000' THEN CASE WHEN ClosingNet <= 0 THEN ABS(ClosingNet) ELSE 0 END
                WHEN TypeId = 2 THEN CASE WHEN ClosingNet <= 0 THEN ABS(ClosingNet) ELSE 0 END
                WHEN TypeId IN (3, 4) THEN CASE WHEN ClosingNet >= 0 THEN ABS(ClosingNet) ELSE 0 END
                ELSE CASE WHEN ClosingNet < 0 THEN ABS(ClosingNet) ELSE 0 END
            END, 2) AS DECIMAL(18, 2)) AS ClosingCredit
    FROM Display
    WHERE ROUND(DisplayOpening, 2) <> 0
       OR ROUND(PeriodDebit, 2) <> 0
       OR ROUND(PeriodCredit, 2) <> 0
       OR ROUND(DisplayClosing, 2) <> 0
    ORDER BY AccountNumber;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Rpt_ProfitAndLoss
    @CompanyId INT,
    @FromDate DATE,
    @ToDate DATE
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH Journal AS (
        SELECT
            l.ChartOfAccountId,
            SUM(CASE WHEN CAST(j.EntryDate AS date) BETWEEN @FromDate AND @ToDate THEN l.Debit ELSE 0 END) AS DebitPeriod,
            SUM(CASE WHEN CAST(j.EntryDate AS date) BETWEEN @FromDate AND @ToDate THEN l.Credit ELSE 0 END) AS CreditPeriod
        FROM dbo.JournalEntryLines AS l
        INNER JOIN dbo.JournalEntries AS j ON j.Id = l.JournalEntryId
        WHERE j.CompanyId = @CompanyId
          AND j.Status = 2
          AND j.IsDeleted = 0
        GROUP BY l.ChartOfAccountId
    )
    SELECT
        a.Id AS AccountId,
        a.AccountNumber,
        a.AccountName,
        CASE a.TypeId
            WHEN 4 THEN N'Revenue'
            WHEN 5 THEN N'Cost of Goods Sold'
            ELSE N'Expenses'
        END AS Section,
        CAST(ROUND(
            CASE WHEN a.TypeId = 4
                THEN ISNULL(j.CreditPeriod, 0) - ISNULL(j.DebitPeriod, 0)
                ELSE ISNULL(j.DebitPeriod, 0) - ISNULL(j.CreditPeriod, 0)
            END, 2) AS DECIMAL(18, 2)) AS Amount
    FROM dbo.ChartOfAccounts AS a
    LEFT JOIN Journal AS j ON j.ChartOfAccountId = a.Id
    WHERE a.CompanyId = @CompanyId
      AND a.IsActive = 1
      AND a.IsDeleted = 0
      AND a.TypeId IN (4, 5, 6)
      AND ROUND(
            CASE WHEN a.TypeId = 4
                THEN ISNULL(j.CreditPeriod, 0) - ISNULL(j.DebitPeriod, 0)
                ELSE ISNULL(j.DebitPeriod, 0) - ISNULL(j.CreditPeriod, 0)
            END, 2) <> 0
    ORDER BY a.TypeId, a.AccountNumber;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Rpt_BalanceSheet
    @CompanyId INT,
    @AsOfDate DATE
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH Journal AS (
        SELECT
            l.ChartOfAccountId,
            SUM(CASE WHEN CAST(j.EntryDate AS date) <= @AsOfDate THEN l.Debit ELSE 0 END) AS DebitUpTo,
            SUM(CASE WHEN CAST(j.EntryDate AS date) <= @AsOfDate THEN l.Credit ELSE 0 END) AS CreditUpTo
        FROM dbo.JournalEntryLines AS l
        INNER JOIN dbo.JournalEntries AS j ON j.Id = l.JournalEntryId
        WHERE j.CompanyId = @CompanyId
          AND j.Status = 2
          AND j.IsDeleted = 0
        GROUP BY l.ChartOfAccountId
    ),
    Pl AS (
        SELECT
            CAST(ROUND(SUM(
                CASE
                    WHEN a.TypeId = 4 THEN -(a.OpeningBalance + ISNULL(j.DebitUpTo, 0) - ISNULL(j.CreditUpTo, 0))
                    WHEN a.TypeId IN (5, 6) THEN -(a.OpeningBalance + ISNULL(j.DebitUpTo, 0) - ISNULL(j.CreditUpTo, 0))
                    ELSE 0
                END
            ), 2) AS DECIMAL(18, 2)) AS NetIncomeYtd
        FROM dbo.ChartOfAccounts AS a
        LEFT JOIN Journal AS j ON j.ChartOfAccountId = a.Id
        WHERE a.CompanyId = @CompanyId
          AND a.IsActive = 1
          AND a.IsDeleted = 0
          AND a.TypeId IN (4, 5, 6)
    ),
    Bs AS (
        SELECT
            a.Id AS AccountId,
            a.AccountNumber,
            a.AccountName,
            a.TypeId,
            CASE a.TypeId
                WHEN 1 THEN N'Assets'
                WHEN 2 THEN N'Liabilities'
                ELSE N'Equity'
            END AS Section,
            a.OpeningBalance + CASE WHEN a.TypeId IN (2, 3)
                THEN ISNULL(j.CreditUpTo, 0) - ISNULL(j.DebitUpTo, 0)
                ELSE ISNULL(j.DebitUpTo, 0) - ISNULL(j.CreditUpTo, 0)
            END AS StoredNet
        FROM dbo.ChartOfAccounts AS a
        LEFT JOIN Journal AS j ON j.ChartOfAccountId = a.Id
        WHERE a.CompanyId = @CompanyId
          AND a.IsActive = 1
          AND a.IsDeleted = 0
          AND a.TypeId IN (1, 2, 3)
          AND a.AccountNumber <> N'30000'
    ),
    Displayed AS (
        SELECT
            AccountId,
            AccountNumber,
            AccountName,
            Section,
            CAST(ROUND(
                CASE
                    WHEN AccountNumber = N'11110' THEN -StoredNet
                    WHEN TypeId = 2 AND AccountNumber <> N'20000' THEN -StoredNet
                    ELSE StoredNet
                END, 2) AS DECIMAL(18, 2)) AS Amount
        FROM Bs
    )
    SELECT AccountId, AccountNumber, AccountName, Section, Amount
    FROM Displayed
    WHERE Amount <> 0

    UNION ALL

    SELECT
        0,
        N'—',
        N'Net Income',
        N'Equity',
        NetIncomeYtd
    FROM Pl
    WHERE NetIncomeYtd <> 0

    ORDER BY Section, AccountNumber;
END
GO

CREATE OR ALTER PROCEDURE dbo.usp_Rpt_ArAging
    @CompanyId INT,
    @AsOfDate DATE
AS
BEGIN
    SET NOCOUNT ON;

    ;WITH Customers AS (
        SELECT c.Id, c.BuyerId, c.BuyerName, c.OpeningBalance
        FROM dbo.Customers AS c
        WHERE c.CompanyId = @CompanyId
          AND c.IsActive = 1
          AND c.IsDeleted = 0
    ),
    CreditNotes AS (
        SELECT si.CustomerId, SUM(si.NetTotal) AS Amount
        FROM dbo.SalesInvoices AS si
        WHERE si.CompanyId = @CompanyId
          AND si.Status = 2
          AND si.InvoiceType = 3 /* CreditNote */
          AND CAST(si.InvoiceDate AS date) <= @AsOfDate
          AND si.IsDeleted = 0
        GROUP BY si.CustomerId
    ),
    Receipts AS (
        SELECT r.CustomerId, SUM(r.Amount) AS Amount
        FROM dbo.CustomerReceipts AS r
        WHERE r.CompanyId = @CompanyId
          AND CAST(r.ReceiptDate AS date) <= @AsOfDate
          AND r.IsDeleted = 0
        GROUP BY r.CustomerId
    ),
    CreditPool AS (
        SELECT
            c.Id AS CustomerId,
            CASE WHEN c.OpeningBalance < 0 THEN ABS(c.OpeningBalance) ELSE 0 END
                + ISNULL(cn.Amount, 0)
                + ISNULL(rc.Amount, 0) AS Pool
        FROM Customers AS c
        LEFT JOIN CreditNotes AS cn ON cn.CustomerId = c.Id
        LEFT JOIN Receipts AS rc ON rc.CustomerId = c.Id
    ),
    Receivables AS (
        SELECT
            c.Id AS CustomerId,
            CAST('19000101' AS date) AS DocDate,
            0 AS Seq,
            c.OpeningBalance AS Amount
        FROM Customers AS c
        WHERE c.OpeningBalance > 0

        UNION ALL

        SELECT
            si.CustomerId,
            CAST(si.InvoiceDate AS date),
            si.Id,
            si.NetTotal
        FROM dbo.SalesInvoices AS si
        WHERE si.CompanyId = @CompanyId
          AND si.Status = 2
          AND si.InvoiceType <> 3
          AND CAST(si.InvoiceDate AS date) <= @AsOfDate
          AND si.IsDeleted = 0
    ),
    Ordered AS (
        SELECT
            r.CustomerId,
            r.DocDate,
            r.Seq,
            r.Amount,
            SUM(r.Amount) OVER (
                PARTITION BY r.CustomerId
                ORDER BY r.DocDate, r.Seq
                ROWS UNBOUNDED PRECEDING) AS RunningRecv
        FROM Receivables AS r
    ),
    Remaining AS (
        SELECT
            o.CustomerId,
            o.DocDate,
            CASE
                WHEN o.RunningRecv <= ISNULL(p.Pool, 0) THEN CAST(0 AS DECIMAL(18, 2))
                WHEN o.RunningRecv - o.Amount >= ISNULL(p.Pool, 0)
                    THEN CAST(o.Amount AS DECIMAL(18, 2))
                ELSE CAST(o.RunningRecv - ISNULL(p.Pool, 0) AS DECIMAL(18, 2))
            END AS RemainingAmount
        FROM Ordered AS o
        LEFT JOIN CreditPool AS p ON p.CustomerId = o.CustomerId
    ),
    Bucketed AS (
        SELECT
            CustomerId,
            SUM(CASE WHEN DocDate = '19000101' THEN RemainingAmount ELSE 0 END) AS OpeningBalance,
            SUM(CASE WHEN DocDate <> '19000101' AND DATEDIFF(DAY, DocDate, @AsOfDate) <= 30 THEN RemainingAmount ELSE 0 END) AS [Current],
            SUM(CASE WHEN DocDate <> '19000101' AND DATEDIFF(DAY, DocDate, @AsOfDate) BETWEEN 31 AND 60 THEN RemainingAmount ELSE 0 END) AS Days31To60,
            SUM(CASE WHEN DocDate <> '19000101' AND DATEDIFF(DAY, DocDate, @AsOfDate) BETWEEN 61 AND 90 THEN RemainingAmount ELSE 0 END) AS Days61To90,
            SUM(CASE WHEN DocDate <> '19000101' AND DATEDIFF(DAY, DocDate, @AsOfDate) > 90 THEN RemainingAmount ELSE 0 END) AS Over90
        FROM Remaining
        WHERE RemainingAmount > 0
        GROUP BY CustomerId
    )
    SELECT
        c.Id AS CustomerId,
        c.BuyerId AS CustomerCode,
        c.BuyerName AS CustomerName,
        CAST(ROUND(ISNULL(b.OpeningBalance, 0), 2) AS DECIMAL(18, 2)) AS OpeningBalance,
        CAST(ROUND(ISNULL(b.[Current], 0), 2) AS DECIMAL(18, 2)) AS [Current],
        CAST(ROUND(ISNULL(b.Days31To60, 0), 2) AS DECIMAL(18, 2)) AS Days31To60,
        CAST(ROUND(ISNULL(b.Days61To90, 0), 2) AS DECIMAL(18, 2)) AS Days61To90,
        CAST(ROUND(ISNULL(b.Over90, 0), 2) AS DECIMAL(18, 2)) AS Over90,
        CAST(ROUND(
            ISNULL(b.OpeningBalance, 0) + ISNULL(b.[Current], 0) + ISNULL(b.Days31To60, 0)
            + ISNULL(b.Days61To90, 0) + ISNULL(b.Over90, 0), 2) AS DECIMAL(18, 2)) AS Total
    FROM Customers AS c
    INNER JOIN Bucketed AS b ON b.CustomerId = c.Id
    WHERE ROUND(
            ISNULL(b.OpeningBalance, 0) + ISNULL(b.[Current], 0) + ISNULL(b.Days31To60, 0)
            + ISNULL(b.Days61To90, 0) + ISNULL(b.Over90, 0), 2) <> 0
    ORDER BY c.BuyerName;
END
GO
