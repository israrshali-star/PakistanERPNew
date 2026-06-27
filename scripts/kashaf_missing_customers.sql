SET QUOTED_IDENTIFIER ON;

DECLARE @scenarioId INT = 1;

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'Asian Sports' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0162', 'Asian Sports', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'ASIF SILK FACTORY' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0163', 'ASIF SILK FACTORY', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'CEO KING' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0164', 'CEO KING', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'GHAZI SILK FACTORY' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0165', 'GHAZI SILK FACTORY', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'HA SIALKOT TEXTILE MILLS' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0166', 'HA SIALKOT TEXTILE MILLS', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'KARRIZAO INDUSTRIES' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0167', 'KARRIZAO INDUSTRIES', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'KHURSHED ALAM & SONS' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0168', 'KHURSHED ALAM & SONS', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'Leather Engineer Co.' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0169', 'Leather Engineer Co.', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'MANSHA & BROTHER PVT LTD' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0170', 'MANSHA & BROTHER PVT LTD', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'MUBASHAR SILK FACTORY' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0171', 'MUBASHAR SILK FACTORY', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'MUQADAS SILK FACTORY' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0172', 'MUQADAS SILK FACTORY', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'MUSTAFA TEXTILE' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0173', 'MUSTAFA TEXTILE', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'NABEEL''S PRODUCT' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0174', 'NABEEL''S PRODUCT', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'NISAR WEAVING FACTORY' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0175', 'NISAR WEAVING FACTORY', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'OSAMA SILK FACTORY' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0176', 'OSAMA SILK FACTORY', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'REBELDO INTERNATIONAL' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0177', 'REBELDO INTERNATIONAL', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'REDE SPORTS CO' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0178', 'REDE SPORTS CO', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'SALEEM YOUNUS SILK FACTORY' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0179', 'SALEEM YOUNUS SILK FACTORY', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'SOLEHRE BROTHERS INDUSTRIES' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0180', 'SOLEHRE BROTHERS INDUSTRIES', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'SUNNY INDUSTRIES (PVT) LIMITED' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0181', 'SUNNY INDUSTRIES (PVT) LIMITED', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');

IF NOT EXISTS (SELECT 1 FROM Customers WHERE CompanyId = 5 AND BuyerName = 'ZOLI INTERNATIONAL (PVT) LTD' AND IsDeleted = 0)
    INSERT INTO Customers (CompanyId, BuyerId, BuyerName, OpeningBalance, CustomerType, ScenarioId, IsActive, IsDeleted, CreatedAt, CreatedBy)
    VALUES (5, 'CUST-0182', 'ZOLI INTERNATIONAL (PVT) LTD', 0, 1, @scenarioId, 1, 0, GETUTCDATE(), 'opening-import');
