/*
  Setup companies 2, 4, 5, 6, 7 to match Company 3 (Arian Traders) chart-of-accounts
  structure and banking layout with zero balances.

  DESTRUCTIVE: wipes transactional data, items, customers, vendors, banks, and COA
  for target companies only. Company 3 is never modified.

  Safe to re-run on the same target companies.
*/
SET NOCOUNT ON;
SET XACT_ABORT ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;

DECLARE @SourceCompanyId INT = 3;
DECLARE @Now DATETIME2 = SYSUTCDATETIME();
DECLARE @Actor NVARCHAR(100) = N'setup-companies-like-co3';

DECLARE @TargetCompanies TABLE (CompanyId INT PRIMARY KEY);
INSERT INTO @TargetCompanies (CompanyId) VALUES (2), (4), (5), (6), (7);

IF NOT EXISTS (SELECT 1 FROM Companies WHERE Id = @SourceCompanyId AND IsDeleted = 0)
BEGIN
    RAISERROR('Source company %d not found.', 16, 1, @SourceCompanyId);
    RETURN;
END;

IF EXISTS (SELECT 1 FROM @TargetCompanies WHERE CompanyId = @SourceCompanyId)
BEGIN
    RAISERROR('Source company cannot be included in target list.', 16, 1);
    RETURN;
END;

DECLARE @CleanupStats TABLE (
    CompanyId INT,
    CompanyName NVARCHAR(200),
    SalesInvoiceAttachments INT DEFAULT 0,
    SalesInvoiceLines INT DEFAULT 0,
    SalesInvoices INT DEFAULT 0,
    VendorBillAttachments INT DEFAULT 0,
    VendorBillLines INT DEFAULT 0,
    VendorBills INT DEFAULT 0,
    CustomerReceipts INT DEFAULT 0,
    VendorPayments INT DEFAULT 0,
    BankTransactions INT DEFAULT 0,
    BankReconciliations INT DEFAULT 0,
    JournalEntryLines INT DEFAULT 0,
    JournalEntries INT DEFAULT 0,
    InventoryTransactions INT DEFAULT 0,
    Items INT DEFAULT 0,
    Customers INT DEFAULT 0,
    Vendors INT DEFAULT 0,
    BanksDeleted INT DEFAULT 0,
    ChartOfAccountsDeleted INT DEFAULT 0,
    ChartOfAccountsCreated INT DEFAULT 0,
    BanksCreated INT DEFAULT 0);

INSERT INTO @CleanupStats (CompanyId, CompanyName)
SELECT tc.CompanyId, c.CompanyName
FROM @TargetCompanies tc
INNER JOIN Companies c ON c.Id = tc.CompanyId;

DECLARE @CompanyId INT;
DECLARE company_cursor CURSOR LOCAL FAST_FORWARD FOR
    SELECT CompanyId FROM @TargetCompanies ORDER BY CompanyId;

OPEN company_cursor;
FETCH NEXT FROM company_cursor INTO @CompanyId;

WHILE @@FETCH_STATUS = 0
BEGIN
    BEGIN TRANSACTION;

    DECLARE @CoaMap TABLE (
        SourceCoaId INT NOT NULL PRIMARY KEY,
        TargetCoaId INT NOT NULL);

    /* --- Phase 1: delete transactional data (child tables first) --- */

    DELETE sia
    FROM SalesInvoiceAttachments sia
    INNER JOIN SalesInvoices si ON si.Id = sia.SalesInvoiceId
    WHERE si.CompanyId = @CompanyId;
    UPDATE @CleanupStats SET SalesInvoiceAttachments = @@ROWCOUNT WHERE CompanyId = @CompanyId;

    DELETE sil
    FROM SalesInvoiceLines sil
    INNER JOIN SalesInvoices si ON si.Id = sil.SalesInvoiceId
    WHERE si.CompanyId = @CompanyId;
    UPDATE @CleanupStats SET SalesInvoiceLines = @@ROWCOUNT WHERE CompanyId = @CompanyId;

    UPDATE SalesInvoices SET JournalEntryId = NULL WHERE CompanyId = @CompanyId;

    DELETE vba
    FROM VendorBillAttachments vba
    INNER JOIN VendorBills vb ON vb.Id = vba.VendorBillId
    WHERE vb.CompanyId = @CompanyId;
    UPDATE @CleanupStats SET VendorBillAttachments = @@ROWCOUNT WHERE CompanyId = @CompanyId;

    DELETE vbl
    FROM VendorBillLines vbl
    INNER JOIN VendorBills vb ON vb.Id = vbl.VendorBillId
    WHERE vb.CompanyId = @CompanyId;
    UPDATE @CleanupStats SET VendorBillLines = @@ROWCOUNT WHERE CompanyId = @CompanyId;

    UPDATE VendorBills SET JournalEntryId = NULL WHERE CompanyId = @CompanyId;

    UPDATE CustomerReceipts
    SET DepositedBankTransactionId = NULL
    WHERE CompanyId = @CompanyId AND DepositedBankTransactionId IS NOT NULL;

    DELETE FROM CustomerReceipts WHERE CompanyId = @CompanyId;
    UPDATE @CleanupStats SET CustomerReceipts = @@ROWCOUNT WHERE CompanyId = @CompanyId;

    DELETE FROM VendorPayments WHERE CompanyId = @CompanyId;
    UPDATE @CleanupStats SET VendorPayments = @@ROWCOUNT WHERE CompanyId = @CompanyId;

    UPDATE BankTransactions
    SET JournalEntryId = NULL,
        TransferToBankId = NULL,
        ChartOfAccountId = NULL,
        TransferToChartOfAccountId = NULL,
        CounterChartOfAccountId = NULL
    WHERE CompanyId = @CompanyId;

    DELETE FROM BankTransactions WHERE CompanyId = @CompanyId;
    UPDATE @CleanupStats SET BankTransactions = @@ROWCOUNT WHERE CompanyId = @CompanyId;

    DELETE br
    FROM BankReconciliations br
    INNER JOIN Banks b ON b.Id = br.BankId
    WHERE b.CompanyId = @CompanyId;
    UPDATE @CleanupStats SET BankReconciliations = @@ROWCOUNT WHERE CompanyId = @CompanyId;

    DELETE jel
    FROM JournalEntryLines jel
    INNER JOIN JournalEntries je ON je.Id = jel.JournalEntryId
    WHERE je.CompanyId = @CompanyId;
    UPDATE @CleanupStats SET JournalEntryLines = @@ROWCOUNT WHERE CompanyId = @CompanyId;

    DELETE FROM JournalEntries WHERE CompanyId = @CompanyId;
    UPDATE @CleanupStats SET JournalEntries = @@ROWCOUNT WHERE CompanyId = @CompanyId;

    DELETE FROM SalesInvoices WHERE CompanyId = @CompanyId;
    UPDATE @CleanupStats SET SalesInvoices = @@ROWCOUNT WHERE CompanyId = @CompanyId;

    DELETE FROM VendorBills WHERE CompanyId = @CompanyId;
    UPDATE @CleanupStats SET VendorBills = @@ROWCOUNT WHERE CompanyId = @CompanyId;

    DELETE FROM InventoryTransactions WHERE CompanyId = @CompanyId;
    UPDATE @CleanupStats SET InventoryTransactions = @@ROWCOUNT WHERE CompanyId = @CompanyId;

    DELETE FROM Items WHERE CompanyId = @CompanyId;
    UPDATE @CleanupStats SET Items = @@ROWCOUNT WHERE CompanyId = @CompanyId;

    DELETE FROM Customers WHERE CompanyId = @CompanyId;
    UPDATE @CleanupStats SET Customers = @@ROWCOUNT WHERE CompanyId = @CompanyId;

    DELETE FROM Vendors WHERE CompanyId = @CompanyId;
    UPDATE @CleanupStats SET Vendors = @@ROWCOUNT WHERE CompanyId = @CompanyId;

    DELETE FROM Banks WHERE CompanyId = @CompanyId;
    UPDATE @CleanupStats SET BanksDeleted = @@ROWCOUNT WHERE CompanyId = @CompanyId;

    UPDATE ChartOfAccounts SET ParentAccountId = NULL WHERE CompanyId = @CompanyId;

    DELETE FROM ChartOfAccounts WHERE CompanyId = @CompanyId;
    UPDATE @CleanupStats SET ChartOfAccountsDeleted = @@ROWCOUNT WHERE CompanyId = @CompanyId;

    /* --- Phase 2: copy COA tree from Company 3 with zero balances --- */

    DECLARE @SourceCoaId INT;
    DECLARE @NewCoaId INT;

    DECLARE coa_cursor CURSOR LOCAL FAST_FORWARD FOR
        SELECT Id
        FROM ChartOfAccounts
        WHERE CompanyId = @SourceCompanyId
        ORDER BY Id;

    OPEN coa_cursor;
    FETCH NEXT FROM coa_cursor INTO @SourceCoaId;

    WHILE @@FETCH_STATUS = 0
    BEGIN
        INSERT INTO ChartOfAccounts (
            CompanyId,
            AccountNumber,
            AccountName,
            TypeId,
            SubTypeId,
            ParentAccountId,
            Description,
            IsActive,
            OpeningBalance,
            CreatedAt,
            CreatedBy)
        SELECT
            @CompanyId,
            src.AccountNumber,
            src.AccountName,
            src.TypeId,
            src.SubTypeId,
            NULL,
            src.Description,
            src.IsActive,
            0,
            @Now,
            @Actor
        FROM ChartOfAccounts src
        WHERE src.Id = @SourceCoaId;

        SET @NewCoaId = SCOPE_IDENTITY();

        INSERT INTO @CoaMap (SourceCoaId, TargetCoaId)
        VALUES (@SourceCoaId, @NewCoaId);

        FETCH NEXT FROM coa_cursor INTO @SourceCoaId;
    END;

    CLOSE coa_cursor;
    DEALLOCATE coa_cursor;

    UPDATE tgt
    SET tgt.ParentAccountId = parentMap.TargetCoaId,
        tgt.UpdatedAt = @Now,
        tgt.UpdatedBy = @Actor
    FROM ChartOfAccounts tgt
    INNER JOIN @CoaMap childMap ON childMap.TargetCoaId = tgt.Id
    INNER JOIN ChartOfAccounts src ON src.Id = childMap.SourceCoaId
    INNER JOIN @CoaMap parentMap ON parentMap.SourceCoaId = src.ParentAccountId
    WHERE tgt.CompanyId = @CompanyId
      AND src.ParentAccountId IS NOT NULL;

    UPDATE @CleanupStats
    SET ChartOfAccountsCreated = (SELECT COUNT(*) FROM ChartOfAccounts WHERE CompanyId = @CompanyId)
    WHERE CompanyId = @CompanyId;

    /* --- Phase 3: seed banks from Company 3 linked to new COA accounts --- */

    INSERT INTO Banks (
        CompanyId,
        BankName,
        AccountTitle,
        AccountNumber,
        IBAN,
        ChartOfAccountId,
        OpeningBalance,
        CurrentBalance,
        NextChequeNumber,
        IsActive,
        CreatedAt,
        CreatedBy)
    SELECT
        @CompanyId,
        srcBank.BankName,
        srcBank.AccountTitle,
        srcBank.AccountNumber,
        srcBank.IBAN,
        coaMap.TargetCoaId,
        0,
        0,
        srcBank.NextChequeNumber,
        srcBank.IsActive,
        @Now,
        @Actor
    FROM Banks srcBank
    INNER JOIN @CoaMap coaMap ON coaMap.SourceCoaId = srcBank.ChartOfAccountId
    WHERE srcBank.CompanyId = @SourceCompanyId
      AND srcBank.IsDeleted = 0
      AND srcBank.ChartOfAccountId IS NOT NULL;

    UPDATE @CleanupStats
    SET BanksCreated = (SELECT COUNT(*) FROM Banks WHERE CompanyId = @CompanyId AND IsDeleted = 0)
    WHERE CompanyId = @CompanyId;

    COMMIT TRANSACTION;

    DELETE FROM @CoaMap;

    FETCH NEXT FROM company_cursor INTO @CompanyId;
END;

CLOSE company_cursor;
DEALLOCATE company_cursor;

PRINT '=== Cleanup and setup summary ===';
SELECT
    CompanyId,
    CompanyName,
    SalesInvoiceAttachments,
    SalesInvoiceLines,
    SalesInvoices,
    VendorBillAttachments,
    VendorBillLines,
    VendorBills,
    CustomerReceipts,
    VendorPayments,
    BankTransactions,
    BankReconciliations,
    JournalEntryLines,
    JournalEntries,
    InventoryTransactions,
    Items,
    Customers,
    Vendors,
    BanksDeleted,
    ChartOfAccountsDeleted,
    ChartOfAccountsCreated,
    BanksCreated
FROM @CleanupStats
ORDER BY CompanyId;

PRINT '=== COA account count comparison (source vs targets) ===';
SELECT
    c.Id AS CompanyId,
    c.CompanyName,
    COUNT(coa.Id) AS CoaCount,
    SUM(CASE WHEN coa.OpeningBalance <> 0 THEN 1 ELSE 0 END) AS NonZeroOpeningAccounts,
    SUM(CASE WHEN coa.ParentAccountId IS NULL THEN 1 ELSE 0 END) AS RootAccounts
FROM Companies c
LEFT JOIN ChartOfAccounts coa ON coa.CompanyId = c.Id
WHERE c.Id IN (2, 3, 4, 5, 6, 7)
GROUP BY c.Id, c.CompanyName
ORDER BY c.Id;

PRINT '=== Required GL accounts present (all targets) ===';
SELECT
    tc.CompanyId,
    c.CompanyName,
    req.AccountNumber,
    CASE WHEN coa.Id IS NULL THEN 'MISSING' ELSE 'OK' END AS Status
FROM @TargetCompanies tc
INNER JOIN Companies c ON c.Id = tc.CompanyId
CROSS JOIN (VALUES
    (N'10000'), (N'10015'), (N'10017'), (N'11110'), (N'12110'),
    (N'12910'), (N'20000'), (N'25500'), (N'26100'), (N'32010'),
    (N'47910'), (N'50000')) AS req(AccountNumber)
LEFT JOIN ChartOfAccounts coa
    ON coa.CompanyId = tc.CompanyId
   AND coa.AccountNumber = req.AccountNumber
ORDER BY tc.CompanyId, req.AccountNumber;

PRINT '=== Bank balances (should all be zero) ===';
SELECT
    b.CompanyId,
    c.CompanyName,
    COUNT(*) AS BankCount,
    SUM(b.OpeningBalance) AS TotalOpeningBalance,
    SUM(b.CurrentBalance) AS TotalCurrentBalance
FROM Banks b
INNER JOIN Companies c ON c.Id = b.CompanyId
WHERE b.CompanyId IN (SELECT CompanyId FROM @TargetCompanies)
  AND b.IsDeleted = 0
GROUP BY b.CompanyId, c.CompanyName
ORDER BY b.CompanyId;

PRINT '=== Remaining transactional rows (should be zero) ===';
SELECT
    tc.CompanyId,
    c.CompanyName,
    (SELECT COUNT(*) FROM Items i WHERE i.CompanyId = tc.CompanyId) AS Items,
    (SELECT COUNT(*) FROM InventoryTransactions it WHERE it.CompanyId = tc.CompanyId) AS InventoryTransactions,
    (SELECT COUNT(*) FROM SalesInvoices si WHERE si.CompanyId = tc.CompanyId) AS SalesInvoices,
    (SELECT COUNT(*) FROM VendorBills vb WHERE vb.CompanyId = tc.CompanyId) AS VendorBills,
    (SELECT COUNT(*) FROM JournalEntries je WHERE je.CompanyId = tc.CompanyId) AS JournalEntries,
    (SELECT COUNT(*) FROM Customers cu WHERE cu.CompanyId = tc.CompanyId) AS Customers,
    (SELECT COUNT(*) FROM Vendors v WHERE v.CompanyId = tc.CompanyId) AS Vendors
FROM @TargetCompanies tc
INNER JOIN Companies c ON c.Id = tc.CompanyId
ORDER BY tc.CompanyId;

PRINT 'Setup companies like Company 3 completed.';
