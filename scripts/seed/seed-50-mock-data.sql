SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

USE OnlineMarketDb;
GO

SET NOCOUNT ON;

DELETE FROM OutboxMessages;
DELETE FROM Payments;
DELETE FROM OrderItems;
DELETE FROM OrderAddresses;
DELETE FROM Orders;
DELETE FROM CartItems;
DELETE FROM Carts;
DELETE FROM CustomerAddresses;
DELETE FROM StockMovements;
DELETE FROM Stocks;
DELETE FROM Products;
DELETE FROM Brands;
DELETE FROM Categories;
DELETE FROM Customers;
DELETE FROM AspNetUsers WHERE UserName LIKE 'user%@onlinemarket.com';

PRINT 'Starting mock data seeding (50 rows per table)...';

-- 1. SEED 50 CATEGORIES
DECLARE @i INT = 1;
WHILE @i <= 50
BEGIN
    INSERT INTO Categories (Id, ParentCategoryId, Name, Slug, Description, DisplayOrder, IsActive, CreatedAtUtc, UpdatedAtUtc)
    VALUES (
        NEWID(),
        NULL,
        N'Kategori ' + CAST(@i AS NVARCHAR(10)),
        N'kategori-' + CAST(@i AS NVARCHAR(10)),
        N'Aciklama Kategori ' + CAST(@i AS NVARCHAR(10)),
        @i,
        1,
        GETUTCDATE(),
        GETUTCDATE()
    );
    SET @i = @i + 1;
END;
PRINT '50 Categories inserted.';

-- 2. SEED 50 BRANDS
SET @i = 1;
WHILE @i <= 50
BEGIN
    INSERT INTO Brands (Id, Name, Slug, LogoUrl, IsActive, CreatedAtUtc, UpdatedAtUtc)
    VALUES (
        NEWID(),
        N'Marka ' + CAST(@i AS NVARCHAR(10)),
        N'marka-' + CAST(@i AS NVARCHAR(10)),
        N'https://example.com/brand-' + CAST(@i AS NVARCHAR(10)) + N'.png',
        1,
        GETUTCDATE(),
        GETUTCDATE()
    );
    SET @i = @i + 1;
END;
PRINT '50 Brands inserted.';

SELECT ROW_NUMBER() OVER (ORDER BY Id) AS RowNum, Id INTO #TempCat FROM Categories;
SELECT ROW_NUMBER() OVER (ORDER BY Id) AS RowNum, Id INTO #TempBrand FROM Brands;

-- 3. SEED 50 PRODUCTS, STOCKS, & STOCK MOVEMENTS
SET @i = 1;
WHILE @i <= 50
BEGIN
    DECLARE @ProdId UNIQUEIDENTIFIER = NEWID();
    DECLARE @CatId UNIQUEIDENTIFIER = (SELECT Id FROM #TempCat WHERE RowNum = @i);
    DECLARE @BrandId UNIQUEIDENTIFIER = (SELECT Id FROM #TempBrand WHERE RowNum = @i);
    DECLARE @Price DECIMAL(18,2) = 10.00 + (@i * 5.50);
    DECLARE @Qty INT = 50 + (@i * 2);

    INSERT INTO Products (Id, Sku, Name, Slug, Description, CategoryId, BrandId, Price, VatRate, NetContent, UnitType, ImageUrl, IsActive, CreatedAtUtc, UpdatedAtUtc)
    VALUES (
        @ProdId,
        N'PRD-SKU-' + RIGHT(N'000' + CAST(@i AS NVARCHAR(10)), 3),
        N'Urun ' + CAST(@i AS NVARCHAR(10)),
        N'urun-' + CAST(@i AS NVARCHAR(10)),
        N'Detayli urun aciklamasi ' + CAST(@i AS NVARCHAR(10)),
        @CatId,
        @BrandId,
        @Price,
        10.00,
        1.000,
        1,
        N'https://example.com/product-' + CAST(@i AS NVARCHAR(10)) + N'.jpg',
        1,
        GETUTCDATE(),
        GETUTCDATE()
    );

    INSERT INTO Stocks (ProductId, Quantity, ReorderLevel, UpdatedAtUtc)
    VALUES (@ProdId, @Qty, 10, GETUTCDATE());

    INSERT INTO StockMovements (ProductId, MovementType, QuantityChange, PreviousQuantity, NewQuantity, ReferenceType, ReferenceId, Description, CreatedByUserId, CreatedAtUtc)
    VALUES (@ProdId, 1, @Qty, 0, @Qty, 4, NULL, N'Initial Seed Stock', NULL, GETUTCDATE());

    SET @i = @i + 1;
END;
PRINT '50 Products, Stocks, and StockMovements inserted.';

-- 4. SEED 50 USERS, CUSTOMERS, & ADDRESSES
SET @i = 1;
WHILE @i <= 50
BEGIN
    DECLARE @UserId UNIQUEIDENTIFIER = NEWID();
    DECLARE @CustId UNIQUEIDENTIFIER = NEWID();
    DECLARE @Email NVARCHAR(256) = N'user' + CAST(@i AS NVARCHAR(10)) + N'@onlinemarket.com';

    INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed, PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumber, PhoneNumberConfirmed, TwoFactorEnabled, LockoutEnd, LockoutEnabled, AccessFailedCount)
    VALUES (
        @UserId,
        @Email,
        UPPER(@Email),
        @Email,
        UPPER(@Email),
        1,
        N'AQAAAAIAAYagAAAAEG8a...',
        NEWID(),
        NEWID(),
        N'555000' + RIGHT(N'000' + CAST(@i AS NVARCHAR(10)), 3),
        1, 0, NULL, 1, 0
    );

    INSERT INTO Customers (Id, UserId, FirstName, LastName, IsActive, CreatedAtUtc, UpdatedAtUtc)
    VALUES (
        @CustId,
        @UserId,
        N'MusteriAd ' + CAST(@i AS NVARCHAR(10)),
        N'MusteriSoyad ' + CAST(@i AS NVARCHAR(10)),
        1,
        GETUTCDATE(),
        GETUTCDATE()
    );

    INSERT INTO CustomerAddresses (Id, CustomerId, Title, ContactName, PhoneNumber, AddressLine1, AddressLine2, District, City, PostalCode, CountryCode, IsDefault, IsActive, CreatedAtUtc, UpdatedAtUtc)
    VALUES (
        NEWID(),
        @CustId,
        N'Ev ' + CAST(@i AS NVARCHAR(10)),
        N'MusteriAd ' + CAST(@i AS NVARCHAR(10)),
        N'555000' + RIGHT(N'000' + CAST(@i AS NVARCHAR(10)), 3),
        N'Ataturk Cad. No: ' + CAST(@i AS NVARCHAR(10)),
        N'Daire ' + CAST(@i AS NVARCHAR(10)),
        N'Kadikoy',
        N'Istanbul',
        N'34000',
        'TR',
        1, 1,
        GETUTCDATE(),
        GETUTCDATE()
    );

    SET @i = @i + 1;
END;
PRINT '50 AspNetUsers, Customers, and CustomerAddresses inserted.';

SELECT ROW_NUMBER() OVER (ORDER BY Id) AS RowNum, Id INTO #TempCust FROM Customers;
SELECT ROW_NUMBER() OVER (ORDER BY Id) AS RowNum, Id, Price, Name, Sku INTO #TempProd FROM Products;

-- 5. SEED 50 CARTS, CART ITEMS, ORDERS, ORDER ADDRESSES, PAYMENTS, & OUTBOX MESSAGES
SET @i = 1;
WHILE @i <= 50
BEGIN
    DECLARE @CartId UNIQUEIDENTIFIER = NEWID();
    DECLARE @OrderId UNIQUEIDENTIFIER = NEWID();
    DECLARE @CurrentCustId UNIQUEIDENTIFIER = (SELECT Id FROM #TempCust WHERE RowNum = @i);
    DECLARE @CurrentProdId UNIQUEIDENTIFIER = (SELECT Id FROM #TempProd WHERE RowNum = @i);
    DECLARE @ProdPrice DECIMAL(18,2) = (SELECT Price FROM #TempProd WHERE RowNum = @i);
    DECLARE @ProdName NVARCHAR(200) = (SELECT Name FROM #TempProd WHERE RowNum = @i);
    DECLARE @ProdSku NVARCHAR(64) = (SELECT Sku FROM #TempProd WHERE RowNum = @i);

    INSERT INTO Carts (Id, CustomerId, Status, CreatedAtUtc, UpdatedAtUtc)
    VALUES (@CartId, @CurrentCustId, 2, GETUTCDATE(), GETUTCDATE());

    INSERT INTO CartItems (Id, CartId, ProductId, Quantity, LastKnownUnitPrice, CreatedAtUtc, UpdatedAtUtc)
    VALUES (NEWID(), @CartId, @CurrentProdId, 2, @ProdPrice, GETUTCDATE(), GETUTCDATE());

    DECLARE @Subtotal DECIMAL(18,2) = @ProdPrice * 2;
    DECLARE @VatTotal DECIMAL(18,2) = @Subtotal * 0.10;
    DECLARE @GrandTotal DECIMAL(18,2) = @Subtotal + @VatTotal;
    DECLARE @OrderNum NVARCHAR(32) = N'OM-20260728-' + RIGHT(N'00000' + CAST(@i AS NVARCHAR(10)), 5);

    INSERT INTO Orders (Id, OrderNumber, CustomerId, SourceCartId, Status, Subtotal, VatTotal, GrandTotal, Currency, CorrelationId, PlacedAtUtc, CreatedAtUtc)
    VALUES (
        @OrderId,
        @OrderNum,
        @CurrentCustId,
        @CartId,
        1,
        @Subtotal,
        @VatTotal,
        @GrandTotal,
        'TRY',
        NEWID(),
        GETUTCDATE(),
        GETUTCDATE()
    );

    INSERT INTO OrderAddresses (OrderId, RecipientName, PhoneNumber, AddressLine1, AddressLine2, District, City, PostalCode, CountryCode)
    VALUES (
        @OrderId,
        N'MusteriAd ' + CAST(@i AS NVARCHAR(10)),
        N'555000' + RIGHT(N'000' + CAST(@i AS NVARCHAR(10)), 3),
        N'Ataturk Cad. No: ' + CAST(@i AS NVARCHAR(10)),
        N'Daire ' + CAST(@i AS NVARCHAR(10)),
        N'Kadikoy',
        N'Istanbul',
        N'34000',
        'TR'
    );

    INSERT INTO OrderItems (Id, OrderId, ProductId, ProductNameSnapshot, SkuSnapshot, Quantity, UnitPrice, VatRate, NetLineAmount, VatAmount, LineTotal)
    VALUES (
        NEWID(),
        @OrderId,
        @CurrentProdId,
        @ProdName,
        @ProdSku,
        2,
        @ProdPrice,
        10.00,
        @Subtotal,
        @VatTotal,
        @GrandTotal
    );

    INSERT INTO Payments (Id, OrderId, Method, Status, Amount, SimulationReference, ProcessedAtUtc, CreatedAtUtc)
    VALUES (
        NEWID(),
        @OrderId,
        2,
        2,
        @GrandTotal,
        N'SIM-REF-' + RIGHT(N'00000' + CAST(@i AS NVARCHAR(10)), 5),
        GETUTCDATE(),
        GETUTCDATE()
    );

    INSERT INTO OutboxMessages (EventId, EventType, Destination, AggregateType, AggregateId, Payload, Status, OccurredAtUtc, AvailableAtUtc, ProcessedAtUtc, AttemptCount, CorrelationId)
    VALUES (
        NEWID(),
        N'OrderConfirmedForRecommendationV1',
        N'Recommendation',
        N'Order',
        @OrderId,
        N'{"OrderId":"' + CAST(@OrderId AS NVARCHAR(36)) + N'","OrderNumber":"' + @OrderNum + N'"}',
        3,
        GETUTCDATE(),
        GETUTCDATE(),
        GETUTCDATE(),
        1,
        NEWID()
    );

    SET @i = @i + 1;
END;
PRINT '50 Carts, CartItems, Orders, OrderAddresses, OrderItems, Payments, and OutboxMessages inserted.';

DROP TABLE #TempCat;
DROP TABLE #TempBrand;
DROP TABLE #TempCust;
DROP TABLE #TempProd;

PRINT 'SUCCESS! All tables seeded with 50 mock items each.';
GO
