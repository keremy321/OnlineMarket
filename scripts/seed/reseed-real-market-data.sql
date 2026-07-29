SET QUOTED_IDENTIFIER ON;
SET ANSI_NULLS ON;
GO

USE OnlineMarketDb;
GO

SET NOCOUNT ON;

-- Clear previous mock/dummy data
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

PRINT 'Cleared old tables successfully.';

-- 1. INSERT AUTHENTIC CATEGORIES
INSERT INTO Categories (Id, ParentCategoryId, Name, Slug, Description, DisplayOrder, IsActive, CreatedAtUtc, UpdatedAtUtc) VALUES
('11111111-1111-1111-1111-111111111111', NULL, N'Meyve & Sebze', 'meyve-sebze', N'Taze meyve ve sebzeler', 1, 1, GETUTCDATE(), GETUTCDATE()),
('22222222-2222-2222-2222-222222222222', NULL, N'Süt & Kahvaltılık', 'sut-kahvaltilik', N'Süt, peynir, yoğurt ve kahvaltılıklar', 2, 1, GETUTCDATE(), GETUTCDATE()),
('33333333-3333-3333-3333-333333333333', NULL, N'Temel Gıda & Bakliyat', 'temel-gida-bakliyat', N'Pirinç, yağ, un ve bakliyatlar', 3, 1, GETUTCDATE(), GETUTCDATE()),
('44444444-4444-4444-4444-444444444444', NULL, N'İçecekler', 'icecekler', N'Su, maden suyu, çay ve meyve suları', 4, 1, GETUTCDATE(), GETUTCDATE()),
('55555555-5555-5555-5555-555555555555', NULL, N'Fırın & Pastane', 'firin-pastane', N'Ekmek, tost ekmeği ve hamur işleri', 5, 1, GETUTCDATE(), GETUTCDATE()),
('66666666-6666-6666-6666-666666666666', NULL, N'Atıştırmalık & Şekerleme', 'atistirmalik-sekerleme', N'Bisküvi, çikolata ve kuruyemişler', 6, 1, GETUTCDATE(), GETUTCDATE());

PRINT 'Authentic Categories inserted.';

-- 2. INSERT TOP BRANDS
INSERT INTO Brands (Id, Name, Slug, LogoUrl, IsActive, CreatedAtUtc, UpdatedAtUtc) VALUES
('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', N'Sütaş', 'sutas', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', N'Torku', 'torku', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('cccccccc-cccc-cccc-cccc-cccccccccccc', N'Eker', 'eker', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('dddddddd-dddd-dddd-dddd-dddddddddddd', N'Uno', 'uno', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee', N'Çaykur', 'caykur', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('ffffffff-ffff-ffff-ffff-ffffffffffff', N'Balparmak', 'balparmak', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('10101010-1010-1010-1010-101010101010', N'Reis Gıda', 'reis-gida', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('20202020-2020-2020-2020-202020202020', N'Komili', 'komili', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('30303030-3030-3030-3030-303030303030', N'Yudum', 'yudum', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('40404040-4040-4040-4040-404040404040', N'Beypazarı', 'beypazari', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('50505050-5050-5050-5050-505050505050', N'Ülker', 'ulker', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('60606060-6060-6060-6060-606060606060', N'Eti', 'eti', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('70707070-7070-7070-7070-707070707070', N'Anadolu Hasadı', 'anadolu-hasadi', NULL, 1, GETUTCDATE(), GETUTCDATE());

PRINT 'Authentic Brands inserted.';

-- 3. INSERT REAL PRODUCTS
INSERT INTO Products (Id, Sku, Name, Slug, CategoryId, BrandId, Price, VatRate, NetContent, UnitType, Description, ImageUrl, IsActive, CreatedAtUtc, UpdatedAtUtc) VALUES
('a0000000-0000-0000-0000-000000000001', 'PRD-ELM-001', N'Amasya Elması 1 KG', 'amasya-elmasi-1-kg', '11111111-1111-1111-1111-111111111111', '70707070-7070-7070-7070-707070707070', 38.50, 1.00, 1.000, 3, N'Taze Amasya elması', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('a0000000-0000-0000-0000-000000000002', 'PRD-DOM-002', N'Çanakkale Domates 1 KG', 'canakkale-domates-1-kg', '11111111-1111-1111-1111-111111111111', '70707070-7070-7070-7070-707070707070', 29.90, 1.00, 1.000, 3, N'Çanakkale tarlalarından lezzetli domates', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('a0000000-0000-0000-0000-000000000003', 'PRD-POR-003', N'Finike Sıkmalık Portakal 1 KG', 'finike-sikmalik-portakal-1-kg', '11111111-1111-1111-1111-111111111111', '70707070-7070-7070-7070-707070707070', 32.00, 1.00, 1.000, 3, N'Bol sulu Finike portakalı', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('a0000000-0000-0000-0000-000000000004', 'PRD-SAL-004', N'Organik Çengelköy Salatalık 1 KG', 'organik-cengelkoy-salatalik-1-kg', '11111111-1111-1111-1111-111111111111', '70707070-7070-7070-7070-707070707070', 24.50, 1.00, 1.000, 3, N'Kıtır organik salatalık', NULL, 1, GETUTCDATE(), GETUTCDATE()),

('b0000000-0000-0000-0000-000000000001', 'PRD-SUT-001', N'Sütaş Tam Yağlı Süt 1L', 'sutas-tam-yagli-sut-1l', '22222222-2222-2222-2222-222222222222', 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', 38.50, 10.00, 1.000, 1, N'Taze Sütaş süt', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('b0000000-0000-0000-0000-000000000002', 'PRD-PEY-002', N'Torku Tam Yağlı Süzme Peynir 500g', 'torku-tam-yagli-suzme-peynir-500g', '22222222-2222-2222-2222-222222222222', 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb', 115.00, 10.00, 0.500, 2, N'Yumuşak lezzetli süzme peynir', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('b0000000-0000-0000-0000-000000000003', 'PRD-YOG-003', N'Eker Süzme Yoğurt 900g', 'eker-suzme-yogurt-900g', '22222222-2222-2222-2222-222222222222', 'cccccccc-cccc-cccc-cccc-cccccccccccc', 89.90, 10.00, 0.900, 2, N'Geleneksel Eker süzme yoğurt', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('b0000000-0000-0000-0000-000000000004', 'PRD-BAL-004', N'Balparmak Süzme Çiçek Balı 460g', 'balparmak-suzme-cicek-bali-460g', '22222222-2222-2222-2222-222222222222', 'ffffffff-ffff-ffff-ffff-ffffffffffff', 185.00, 10.00, 0.460, 2, N'%100 doğal Balparmak balı', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('b0000000-0000-0000-0000-000000000005', 'PRD-YUM-005', N'Gezen Tavuk Yumurtası 15''li', 'gezen-tavuk-yumurtasi-15li', '22222222-2222-2222-2222-222222222222', '70707070-7070-7070-7070-707070707070', 64.50, 10.00, 1.000, 2, N'Organik yumurta', NULL, 1, GETUTCDATE(), GETUTCDATE()),

('c0000000-0000-0000-0000-000000000001', 'PRD-PIR-001', N'Reis Osmancık Pirinç 1 KG', 'reis-osmancik-pirinc-1-kg', '33333333-3333-3333-3333-333333333333', '10101010-1010-1010-1010-101010101010', 68.00, 1.00, 1.000, 2, N'Pilavlık yerli Osmancık pirinç', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('c0000000-0000-0000-0000-000000000002', 'PRD-ZEY-002', N'Komili Soğuk Sıkım Sızma Zeytinyağı 1L', 'komili-soguk-sikim-sizma-zeytinyagi-1l', '33333333-3333-3333-3333-333333333333', '20202020-2020-2020-2020-202020202020', 295.00, 10.00, 1.000, 1, N'Ege sızma zeytinyağı', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('c0000000-0000-0000-0000-000000000003', 'PRD-YAG-003', N'Yudum Ayçiçek Yağı 2L', 'yudum-aycicek-yagi-2l', '33333333-3333-3333-3333-333333333333', '30303030-3030-3030-3030-303030303030', 142.50, 10.00, 2.000, 1, N'Hafif ayçiçek yağı', NULL, 1, GETUTCDATE(), GETUTCDATE()),

('d0000000-0000-0000-0000-000000000001', 'PRD-MAD-001', N'Beypazarı Doğal Maden Suyu 6x200ml', 'beypazari-dogal-maden-suyu-6x200ml', '44444444-4444-4444-4444-444444444444', '40404040-4040-4040-4040-404040404040', 42.00, 20.00, 1.200, 2, N'Doğal zengin mineralli su', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('d0000000-0000-0000-0000-000000000002', 'PRD-CAY-002', N'Çaykur Rize Turist Dökme Çay 1000g', 'caykur-rize-turist-dokme-cay-1000g', '44444444-4444-4444-4444-444444444444', 'eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee', 175.00, 20.00, 1.000, 2, N'Geleneksel Rize turist çayı', NULL, 1, GETUTCDATE(), GETUTCDATE()),

('e0000000-0000-0000-0000-000000000001', 'PRD-EKM-001', N'Uno Tam Buğday Ekmeği 500g', 'uno-tam-bugday-ekmegi-500g', '55555555-5555-5555-5555-555555555555', 'dddddddd-dddd-dddd-dddd-dddddddddddd', 22.00, 1.00, 0.500, 2, N'Lifli tam buğday ekmeği', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('e0000000-0000-0000-0000-000000000002', 'PRD-TST-002', N'Uno Premium Tost Ekmeği 675g', 'uno-premium-tost-ekmegi-675g', '55555555-5555-5555-5555-555555555555', 'dddddddd-dddd-dddd-dddd-dddddddddddd', 54.00, 1.00, 0.675, 2, N'Yumuşak tost ekmeği', NULL, 1, GETUTCDATE(), GETUTCDATE()),

('f0000000-0000-0000-0000-000000000001', 'PRD-GOF-001', N'Ülker Çikolatalı Gofret 5''li Paket', 'ulker-cikolatali-gofret-5li-paket', '66666666-6666-6666-6666-666666666666', '50505050-5050-5050-5050-505050505050', 35.00, 20.00, 0.180, 2, N'Çıtır çıtır çikolatalı gofret', NULL, 1, GETUTCDATE(), GETUTCDATE()),
('f0000000-0000-0000-0000-000000000002', 'PRD-BIS-002', N'Eti Burçak Yulaflı Bisküvi 3''lü', 'eti-burcak-yulaflı-biskuvi-3lu', '66666666-6666-6666-6666-666666666666', '60606060-6060-6060-6060-606060606060', 42.00, 20.00, 0.375, 2, N'Doğal yulaflı bisküvi', NULL, 1, GETUTCDATE(), GETUTCDATE());

PRINT 'Authentic Products inserted.';

-- 4. INSERT STOCKS
INSERT INTO Stocks (ProductId, Quantity, ReorderLevel, UpdatedAtUtc)
SELECT Id, 100, 10, GETUTCDATE() FROM Products;

PRINT 'Stocks inserted successfully.';
