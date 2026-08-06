using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OnlineMarket.Web.Common.Messaging;
using OnlineMarket.Web.Domain.Entities;
using OnlineMarket.Web.Domain.Enums;
using OnlineMarket.Web.Infrastructure.Persistence;

namespace OnlineMarket.Web.Infrastructure.Importing;

public sealed class DemoExcelImporter
{
    private const string CustomerRole = "Customer";
    private const string Currency = "TRY";
    private const string AddressTitle = "Demo Address";

    private readonly OnlineMarketDbContext dbContext;
    private readonly UserManager<ApplicationUser> userManager;
    private readonly RoleManager<IdentityRole<Guid>> roleManager;
    private readonly ILogger<DemoExcelImporter> logger;
    private readonly DemoExcelWorkbookReader workbookReader = new();

    public DemoExcelImporter(
        OnlineMarketDbContext dbContext,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole<Guid>> roleManager,
        ILogger<DemoExcelImporter> logger)
    {
        this.dbContext = dbContext;
        this.userManager = userManager;
        this.roleManager = roleManager;
        this.logger = logger;
    }

    public async Task<DemoExcelImportResult> ImportAsync(
        string workbookPath,
        string customerPassword,
        bool emitHistoricalErpEvents,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workbookPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(customerPassword);

        var document = workbookReader.Read(workbookPath);

        if (!dbContext.Database.IsSqlServer())
        {
            throw new InvalidOperationException(
                "The demo Excel importer requires the SQL Server provider.");
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

            try
            {
                await ValidateUniqueDatabaseKeysAsync(document, cancellationToken);
                var importedOrderIds = document.Orders.Select(order => order.Id).ToArray();
                var existingImportedOrderCount = await dbContext.Orders
                    .CountAsync(order => importedOrderIds.Contains(order.Id), cancellationToken);

                if (existingImportedOrderCount != 0
                    && existingImportedOrderCount != document.Orders.Count)
                {
                    throw Conflict(
                        "Only part of the workbook order set already exists. Partial historical imports are not resumed.");
                }

                if (existingImportedOrderCount == document.Orders.Count)
                {
                    await ValidateCompleteExistingImportAsync(
                        document,
                        emitHistoricalErpEvents,
                        cancellationToken);
                    await transaction.CommitAsync(cancellationToken);

                    var existingResult = CreateResult(document, emitHistoricalErpEvents, true);
                    logger.LogInformation(
                        "Demo Excel import is already complete: {Products} products, {Customers} customers, {Orders} orders, {OutboxMessages} expected Outbox messages.",
                        existingResult.Products,
                        existingResult.Customers,
                        existingResult.Orders,
                        existingResult.ProductOutboxMessages
                        + existingResult.RecommendationOutboxMessages
                        + existingResult.ErpOutboxMessages);
                    return existingResult;
                }

                await RefusePartialImportArtifactsAsync(document, cancellationToken);
                await EnsureCustomerRoleAsync();
                await EnsureIdentityUsersAsync(document, customerPassword, cancellationToken);
                await AddOrValidateCatalogueAsync(document, cancellationToken);
                await AddOrValidateCustomersAsync(document, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);

                AddHistoricalOrdersAndStock(document);
                AddProductOutboxMessages(document);
                await dbContext.SaveChangesAsync(cancellationToken);

                AddRecommendationOutboxMessages(document);
                await dbContext.SaveChangesAsync(cancellationToken);

                if (emitHistoricalErpEvents)
                {
                    AddErpOutboxMessages(document);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);

                var result = CreateResult(document, emitHistoricalErpEvents, false);
                logger.LogInformation(
                    "Demo Excel import completed: {Categories} categories, {Brands} brands, {Products} products, {Customers} customers, {Orders} orders, {OrderItems} order items, {OutboxMessages} Outbox messages.",
                    result.Categories,
                    result.Brands,
                    result.Products,
                    result.Customers,
                    result.Orders,
                    result.OrderItems,
                    result.ProductOutboxMessages
                    + result.RecommendationOutboxMessages
                    + result.ErpOutboxMessages);
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                dbContext.ChangeTracker.Clear();
                throw;
            }
        });
    }

    private async Task ValidateUniqueDatabaseKeysAsync(
        DemoImportDocument document,
        CancellationToken cancellationToken)
    {
        var categoryIds = document.Categories.Select(row => row.Id).ToArray();
        var categorySlugs = document.Categories.Select(row => row.Slug).ToArray();
        var categoryConflicts = await dbContext.Categories
            .AsNoTracking()
            .Where(row => categorySlugs.Contains(row.Slug) && !categoryIds.Contains(row.Id))
            .Select(row => row.Slug)
            .ToListAsync(cancellationToken);
        if (categoryConflicts.Count > 0)
        {
            throw Conflict($"Category slug '{categoryConflicts[0]}' belongs to another database row.");
        }

        var brandIds = document.Brands.Select(row => row.Id).ToArray();
        var brandNames = document.Brands.Select(row => row.Name).ToArray();
        var brandSlugs = document.Brands.Select(row => row.Slug).ToArray();
        var brandConflict = await dbContext.Brands
            .AsNoTracking()
            .Where(row =>
                (brandNames.Contains(row.Name) || brandSlugs.Contains(row.Slug))
                && !brandIds.Contains(row.Id))
            .Select(row => row.Name)
            .FirstOrDefaultAsync(cancellationToken);
        if (brandConflict is not null)
        {
            throw Conflict($"Brand '{brandConflict}' belongs to another database row.");
        }

        var productIds = document.Products.Select(row => row.Id).ToArray();
        var skus = document.Products.Select(row => row.Sku).ToArray();
        var productSlugs = document.Products.Select(row => row.Slug).ToArray();
        var productConflict = await dbContext.Products
            .AsNoTracking()
            .Where(row =>
                (skus.Contains(row.Sku) || productSlugs.Contains(row.Slug))
                && !productIds.Contains(row.Id))
            .Select(row => row.Sku)
            .FirstOrDefaultAsync(cancellationToken);
        if (productConflict is not null)
        {
            throw Conflict($"Product key '{productConflict}' belongs to another database row.");
        }

        var orderIds = document.Orders.Select(row => row.Id).ToArray();
        var orderNumbers = document.Orders.Select(row => row.OrderNumber).ToArray();
        var orderConflict = await dbContext.Orders
            .AsNoTracking()
            .Where(row => orderNumbers.Contains(row.OrderNumber) && !orderIds.Contains(row.Id))
            .Select(row => row.OrderNumber)
            .FirstOrDefaultAsync(cancellationToken);
        if (orderConflict is not null)
        {
            throw Conflict($"Order number '{orderConflict}' belongs to another database row.");
        }

        var customerIds = document.Customers.Select(row => row.Id).ToArray();
        var userIds = document.Customers.Select(row => UserId(row.Id)).ToArray();
        var normalizedEmails = document.Customers
            .Select(row => userManager.NormalizeEmail(row.Email))
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();
        var userConflict = await dbContext.Users
            .AsNoTracking()
            .Where(row =>
                row.NormalizedEmail != null
                && normalizedEmails.Contains(row.NormalizedEmail)
                && !userIds.Contains(row.Id))
            .Select(row => row.Email)
            .FirstOrDefaultAsync(cancellationToken);
        if (userConflict is not null)
        {
            throw Conflict($"Customer email '{userConflict}' belongs to another Identity user.");
        }

        var addressIds = document.CustomerAddresses.Select(row => row.Id).ToArray();
        var addressConflict = await dbContext.CustomerAddresses
            .AsNoTracking()
            .Where(row =>
                customerIds.Contains(row.CustomerId)
                && row.IsActive
                && row.IsDefault
                && !addressIds.Contains(row.Id))
            .Select(row => row.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (addressConflict != Guid.Empty)
        {
            throw Conflict(
                $"Customer address '{addressConflict}' conflicts with the workbook default address.");
        }
    }

    private async Task RefusePartialImportArtifactsAsync(
        DemoImportDocument document,
        CancellationToken cancellationToken)
    {
        var sourceCartIds = document.Orders.Select(order => CartId(order.Id)).ToArray();
        if (await dbContext.Carts.AnyAsync(cart => sourceCartIds.Contains(cart.Id), cancellationToken))
        {
            throw Conflict("A deterministic workbook source cart already exists without the full order set.");
        }

        var orderIds = document.Orders.Select(order => order.Id).ToArray();
        if (await dbContext.StockMovements.AnyAsync(
                movement =>
                    movement.ReferenceType == StockReferenceType.Order
                    && movement.ReferenceId.HasValue
                    && orderIds.Contains(movement.ReferenceId.Value),
                cancellationToken))
        {
            throw Conflict("Workbook sale movements already exist without the full order set.");
        }

        var expectedEventIds = ExpectedEventIds(document, includeErp: true);
        if (await dbContext.OutboxMessages.AnyAsync(
                message => expectedEventIds.Contains(message.EventId),
                cancellationToken))
        {
            throw Conflict("Deterministic workbook Outbox rows already exist without the full order set.");
        }
    }

    private async Task EnsureCustomerRoleAsync()
    {
        if (await roleManager.RoleExistsAsync(CustomerRole))
        {
            return;
        }

        var result = await roleManager.CreateAsync(new IdentityRole<Guid>(CustomerRole));
        ThrowIfIdentityFailed(result, "Could not create the Customer role.");
    }

    private async Task EnsureIdentityUsersAsync(
        DemoImportDocument document,
        string customerPassword,
        CancellationToken cancellationToken)
    {
        foreach (var customer in document.Customers.OrderBy(row => row.Id))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var expectedUserId = UserId(customer.Id);
            var user = await userManager.FindByIdAsync(expectedUserId.ToString());
            var emailUser = await userManager.FindByEmailAsync(customer.Email);

            if (emailUser is not null && emailUser.Id != expectedUserId)
            {
                throw Conflict($"Customer email '{customer.Email}' belongs to another Identity user.");
            }

            if (user is null)
            {
                user = new ApplicationUser
                {
                    Id = expectedUserId,
                    UserName = customer.Email,
                    Email = customer.Email,
                    EmailConfirmed = true
                };

                var createResult = await userManager.CreateAsync(user, customerPassword);
                ThrowIfIdentityFailed(
                    createResult,
                    $"Could not create the Identity user for customer '{customer.Id}'.");
            }
            else if (!string.Equals(user.Email, customer.Email, StringComparison.OrdinalIgnoreCase)
                     || !string.Equals(user.UserName, customer.Email, StringComparison.OrdinalIgnoreCase))
            {
                throw Conflict($"Identity user '{user.Id}' conflicts with customer '{customer.Id}'.");
            }

            if (!await userManager.IsInRoleAsync(user, CustomerRole))
            {
                var roleResult = await userManager.AddToRoleAsync(user, CustomerRole);
                ThrowIfIdentityFailed(
                    roleResult,
                    $"Could not assign the Customer role for customer '{customer.Id}'.");
            }
        }
    }

    private async Task AddOrValidateCatalogueAsync(
        DemoImportDocument document,
        CancellationToken cancellationToken)
    {
        var categoryIds = document.Categories.Select(row => row.Id).ToArray();
        var existingCategories = await dbContext.Categories
            .Where(row => categoryIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        foreach (var row in document.Categories)
        {
            if (existingCategories.TryGetValue(row.Id, out var existing))
            {
                Require(
                    existing.ParentCategoryId == row.ParentCategoryId
                    && existing.Name == row.Name
                    && existing.Slug == row.Slug
                    && existing.IsActive,
                    $"Category '{row.Id}' conflicts with the workbook.");
                continue;
            }

            dbContext.Categories.Add(new Category
            {
                Id = row.Id,
                ParentCategoryId = row.ParentCategoryId,
                Name = row.Name,
                Slug = row.Slug,
                DisplayOrder = 0,
                IsActive = true,
                CreatedAtUtc = document.CatalogueOccurredAtUtc,
                UpdatedAtUtc = document.CatalogueOccurredAtUtc
            });
        }

        var brandIds = document.Brands.Select(row => row.Id).ToArray();
        var existingBrands = await dbContext.Brands
            .Where(row => brandIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        foreach (var row in document.Brands)
        {
            if (existingBrands.TryGetValue(row.Id, out var existing))
            {
                Require(
                    existing.Name == row.Name
                    && existing.Slug == row.Slug
                    && existing.IsActive,
                    $"Brand '{row.Id}' conflicts with the workbook.");
                continue;
            }

            dbContext.Brands.Add(new Brand
            {
                Id = row.Id,
                Name = row.Name,
                Slug = row.Slug,
                IsActive = true,
                CreatedAtUtc = document.CatalogueOccurredAtUtc,
                UpdatedAtUtc = document.CatalogueOccurredAtUtc
            });
        }

        var productIds = document.Products.Select(row => row.Id).ToArray();
        var existingProducts = await dbContext.Products
            .Where(row => productIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var existingStocks = await dbContext.Stocks
            .Where(row => productIds.Contains(row.ProductId))
            .ToDictionaryAsync(row => row.ProductId, cancellationToken);
        var existingMovements = await dbContext.StockMovements
            .Where(row => productIds.Contains(row.ProductId))
            .ToListAsync(cancellationToken);

        foreach (var row in document.Products)
        {
            if (existingProducts.TryGetValue(row.Id, out var existingProduct))
            {
                ValidateProduct(existingProduct, row);
                if (!existingStocks.TryGetValue(row.Id, out var existingStock))
                {
                    throw Conflict($"Product '{row.Sku}' has no stock row.");
                }

                Require(
                    existingStock.Quantity == row.InitialStock,
                    $"Product '{row.Sku}' stock is not at workbook InitialStock before import.");
            }
            else
            {
                if (existingStocks.ContainsKey(row.Id))
                {
                    throw Conflict($"Stock exists for missing workbook product '{row.Sku}'.");
                }

                dbContext.Products.Add(new Product
                {
                    Id = row.Id,
                    Sku = row.Sku,
                    Name = row.Name,
                    Slug = row.Slug,
                    CategoryId = row.CategoryId,
                    BrandId = row.BrandId,
                    Price = row.Price,
                    VatRate = row.VatRate,
                    NetContent = row.NetContent,
                    UnitType = row.UnitType,
                    IsActive = true,
                    CreatedAtUtc = document.CatalogueOccurredAtUtc,
                    UpdatedAtUtc = document.CatalogueOccurredAtUtc
                });
                var stock = new Stock
                {
                    ProductId = row.Id,
                    Quantity = row.InitialStock,
                    ReorderLevel = 0,
                    UpdatedAtUtc = document.CatalogueOccurredAtUtc
                };
                dbContext.Stocks.Add(stock);
                existingStocks[row.Id] = stock;
            }

            var movements = existingMovements
                .Where(movement => movement.ProductId == row.Id)
                .ToArray();
            if (movements.Any(movement => movement.MovementType != StockMovementType.Initial))
            {
                throw Conflict($"Product '{row.Sku}' already has non-initial stock movements.");
            }

            if (row.InitialStock == 0)
            {
                Require(
                    movements.Length == 0,
                    $"Zero-stock product '{row.Sku}' has an unexpected initial movement.");
                continue;
            }

            if (movements.Length == 0)
            {
                dbContext.StockMovements.Add(new StockMovement
                {
                    ProductId = row.Id,
                    MovementType = StockMovementType.Initial,
                    QuantityChange = row.InitialStock,
                    PreviousQuantity = 0,
                    NewQuantity = row.InitialStock,
                    ReferenceType = StockReferenceType.Seed,
                    Description = "Demo Excel Initial Balance",
                    CreatedAtUtc = document.CatalogueOccurredAtUtc
                });
            }
            else
            {
                var movement = AssertSingle(movements, $"initial movement for product '{row.Sku}'");
                Require(
                    movement.QuantityChange == row.InitialStock
                    && movement.PreviousQuantity == 0
                    && movement.NewQuantity == row.InitialStock
                    && movement.ReferenceType == StockReferenceType.Seed
                    && movement.ReferenceId is null,
                    $"Product '{row.Sku}' initial movement conflicts with InitialStock.");
            }
        }
    }

    private async Task AddOrValidateCustomersAsync(
        DemoImportDocument document,
        CancellationToken cancellationToken)
    {
        var customerIds = document.Customers.Select(row => row.Id).ToArray();
        var existingCustomers = await dbContext.Customers
            .Where(row => customerIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        foreach (var row in document.Customers)
        {
            if (existingCustomers.TryGetValue(row.Id, out var existing))
            {
                Require(
                    existing.UserId == UserId(row.Id)
                    && existing.FirstName == row.FirstName
                    && existing.LastName == row.LastName
                    && existing.IsActive,
                    $"Customer '{row.Id}' conflicts with the workbook.");
                continue;
            }

            dbContext.Customers.Add(new Customer
            {
                Id = row.Id,
                UserId = UserId(row.Id),
                FirstName = row.FirstName,
                LastName = row.LastName,
                IsActive = true,
                CreatedAtUtc = document.CatalogueOccurredAtUtc,
                UpdatedAtUtc = document.CatalogueOccurredAtUtc
            });
        }

        var addressIds = document.CustomerAddresses.Select(row => row.Id).ToArray();
        var existingAddresses = await dbContext.CustomerAddresses
            .Where(row => addressIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        foreach (var row in document.CustomerAddresses)
        {
            if (existingAddresses.TryGetValue(row.Id, out var existing))
            {
                ValidateCustomerAddress(existing, row);
                continue;
            }

            dbContext.CustomerAddresses.Add(new CustomerAddress
            {
                Id = row.Id,
                CustomerId = row.CustomerId,
                Title = AddressTitle,
                ContactName = row.RecipientName,
                PhoneNumber = row.PhoneNumber,
                AddressLine1 = row.AddressLine1,
                District = row.District,
                City = row.City,
                PostalCode = row.PostalCode,
                CountryCode = row.CountryCode,
                IsDefault = row.IsDefault,
                IsActive = true,
                CreatedAtUtc = document.CatalogueOccurredAtUtc,
                UpdatedAtUtc = document.CatalogueOccurredAtUtc
            });
        }
    }

    private void AddHistoricalOrdersAndStock(DemoImportDocument document)
    {
        var stockByProduct = dbContext.Stocks.Local.ToDictionary(row => row.ProductId);
        Require(
            stockByProduct.Count == document.Products.Count,
            "Every workbook product must have a tracked stock row before historical orders are applied.");

        var itemsByOrder = document.OrderItems.ToLookup(row => row.OrderId);
        var addressesByCustomer = document.CustomerAddresses.ToLookup(row => row.CustomerId);
        var runningStock = document.Products.ToDictionary(row => row.Id, row => row.InitialStock);

        foreach (var orderRow in document.Orders)
        {
            var orderItems = itemsByOrder[orderRow.Id].ToArray();
            var selectedAddress = addressesByCustomer[orderRow.CustomerId]
                .OrderByDescending(row => row.IsDefault)
                .ThenBy(row => row.Id)
                .First();
            var cartId = CartId(orderRow.Id);
            var correlationId = CorrelationId(orderRow.Id);

            var cart = new Cart
            {
                Id = cartId,
                CustomerId = orderRow.CustomerId,
                Status = CartStatus.Converted,
                CreatedAtUtc = orderRow.PlacedAtUtc,
                UpdatedAtUtc = orderRow.PlacedAtUtc,
                Items = orderItems.Select(item => new CartItem
                {
                    Id = CartItemId(orderRow.Id, item.ProductId),
                    CartId = cartId,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    LastKnownUnitPrice = item.UnitPrice,
                    CreatedAtUtc = orderRow.PlacedAtUtc,
                    UpdatedAtUtc = orderRow.PlacedAtUtc
                }).ToList()
            };

            var order = new Order
            {
                Id = orderRow.Id,
                OrderNumber = orderRow.OrderNumber,
                CustomerId = orderRow.CustomerId,
                SourceCartId = cartId,
                Status = OrderStatus.Confirmed,
                Subtotal = orderRow.Subtotal,
                VatTotal = orderRow.VatTotal,
                GrandTotal = orderRow.GrandTotal,
                Currency = Currency,
                CorrelationId = correlationId,
                PlacedAtUtc = orderRow.PlacedAtUtc,
                CreatedAtUtc = orderRow.PlacedAtUtc,
                AddressSnapshot = new OrderAddress
                {
                    OrderId = orderRow.Id,
                    RecipientName = selectedAddress.RecipientName,
                    PhoneNumber = selectedAddress.PhoneNumber,
                    AddressLine1 = selectedAddress.AddressLine1,
                    District = selectedAddress.District,
                    City = selectedAddress.City,
                    PostalCode = selectedAddress.PostalCode,
                    CountryCode = selectedAddress.CountryCode
                },
                Payment = new Payment
                {
                    Id = PaymentId(orderRow.Id),
                    OrderId = orderRow.Id,
                    Method = PaymentMethod.CashSimulation,
                    Status = PaymentStatus.Succeeded,
                    Amount = orderRow.GrandTotal,
                    SimulationReference = PaymentReference(orderRow.Id),
                    ProcessedAtUtc = orderRow.PlacedAtUtc,
                    CreatedAtUtc = orderRow.PlacedAtUtc
                },
                Items = orderItems.Select(item => new OrderItem
                {
                    Id = OrderItemId(orderRow.Id, item.ProductId),
                    OrderId = orderRow.Id,
                    ProductId = item.ProductId,
                    ProductNameSnapshot = item.ProductNameSnapshot,
                    SkuSnapshot = item.SkuSnapshot,
                    Quantity = item.Quantity,
                    UnitPrice = item.UnitPrice,
                    VatRate = item.VatRate,
                    NetLineAmount = item.NetLineAmount,
                    VatAmount = item.VatAmount,
                    LineTotal = item.LineTotal
                }).ToList()
            };

            dbContext.Carts.Add(cart);
            dbContext.Orders.Add(order);

            foreach (var item in orderItems)
            {
                var previousQuantity = runningStock[item.ProductId];
                var newQuantity = previousQuantity - item.Quantity;
                runningStock[item.ProductId] = newQuantity;
                dbContext.StockMovements.Add(new StockMovement
                {
                    ProductId = item.ProductId,
                    MovementType = StockMovementType.Sale,
                    QuantityChange = -item.Quantity,
                    PreviousQuantity = previousQuantity,
                    NewQuantity = newQuantity,
                    ReferenceType = StockReferenceType.Order,
                    ReferenceId = orderRow.Id,
                    Description = "Demo Excel Historical Sale",
                    CreatedAtUtc = orderRow.PlacedAtUtc
                });
            }
        }

        foreach (var product in document.Products)
        {
            var stock = stockByProduct[product.Id];
            stock.Quantity = document.FinalStockByProductId[product.Id];
            stock.UpdatedAtUtc = document.Orders
                .Where(order => itemsByOrder[order.Id].Any(item => item.ProductId == product.Id))
                .Select(order => order.PlacedAtUtc)
                .DefaultIfEmpty(document.CatalogueOccurredAtUtc)
                .Max();
        }
    }

    private void AddProductOutboxMessages(DemoImportDocument document)
    {
        var categories = document.Categories.ToDictionary(row => row.Id);
        var trackedProducts = dbContext.Products.Local.ToDictionary(row => row.Id);
        foreach (var productRow in document.Products)
        {
            var product = trackedProducts[productRow.Id];
            var integrationEvent = new ProductSnapshotChangedV1(
                ProductEventId(product.Id),
                document.CatalogueOccurredAtUtc,
                ProductCorrelationId(product.Id),
                product.Id,
                product.Sku,
                product.Name,
                product.Description,
                product.CategoryId,
                categories[product.CategoryId].ParentCategoryId,
                product.BrandId,
                product.Price,
                product.NetContent,
                product.UnitType,
                product.IsActive,
                document.FinalStockByProductId[product.Id] > 0,
                AsUtc(product.UpdatedAtUtc));

            dbContext.OutboxMessages.Add(OutboxMessageFactory.Create(
                integrationEvent,
                OutboxMessageFactory.RecommendationDestination,
                "Product",
                product.Id));
        }
    }

    private void AddRecommendationOutboxMessages(DemoImportDocument document)
    {
        var itemsByOrder = document.OrderItems.ToLookup(row => row.OrderId);
        foreach (var order in document.Orders)
        {
            var integrationEvent = new OrderConfirmedForRecommendationV1(
                RecommendationEventId(order.Id),
                order.PlacedAtUtc,
                CorrelationId(order.Id),
                order.Id,
                order.OrderNumber,
                order.CustomerId,
                itemsByOrder[order.Id]
                    .Select(item => new RecommendationOrderItemV1(item.ProductId, item.Quantity))
                    .ToArray());
            dbContext.OutboxMessages.Add(OutboxMessageFactory.Create(
                integrationEvent,
                OutboxMessageFactory.RecommendationDestination,
                "Order",
                order.Id));
        }
    }

    private void AddErpOutboxMessages(DemoImportDocument document)
    {
        var customers = document.Customers.ToDictionary(row => row.Id);
        var products = document.Products.ToDictionary(row => row.Id);
        var itemsByOrder = document.OrderItems.ToLookup(row => row.OrderId);
        var addressesByCustomer = document.CustomerAddresses.ToLookup(row => row.CustomerId);

        foreach (var order in document.Orders)
        {
            var customer = customers[order.CustomerId];
            var address = addressesByCustomer[order.CustomerId]
                .OrderByDescending(row => row.IsDefault)
                .ThenBy(row => row.Id)
                .First();
            var integrationEvent = new OrderReadyForErpV1(
                ErpEventId(order.Id),
                order.PlacedAtUtc,
                CorrelationId(order.Id),
                order.Id,
                order.OrderNumber,
                order.PlacedAtUtc,
                PaymentMethod.CashSimulation,
                new ErpOrderCustomerV1(
                    customer.Id,
                    customer.FirstName,
                    customer.LastName,
                    customer.Email),
                new ErpOrderAddressV1(
                    address.RecipientName,
                    address.PhoneNumber,
                    address.AddressLine1,
                    null,
                    address.District,
                    address.City,
                    address.PostalCode,
                    address.CountryCode),
                new ErpOrderTotalsV1(
                    order.Subtotal,
                    order.VatTotal,
                    order.GrandTotal,
                    Currency),
                itemsByOrder[order.Id].Select(item =>
                {
                    var product = products[item.ProductId];
                    return new ErpOrderItemV1(
                        item.ProductId,
                        item.SkuSnapshot,
                        item.ProductNameSnapshot,
                        item.Quantity,
                        item.UnitPrice,
                        item.VatRate,
                        item.NetLineAmount,
                        item.VatAmount,
                        item.LineTotal);
                }).ToArray());

            dbContext.OutboxMessages.Add(OutboxMessageFactory.Create(
                integrationEvent,
                OutboxMessageFactory.ErpIntegrationDestination,
                "Order",
                order.Id));
        }
    }

    private async Task ValidateCompleteExistingImportAsync(
        DemoImportDocument document,
        bool expectErpEvents,
        CancellationToken cancellationToken)
    {
        await ValidateCompleteIdentityAndCustomersAsync(document, cancellationToken);

        var categoryIds = document.Categories.Select(row => row.Id).ToArray();
        var categories = await dbContext.Categories
            .AsNoTracking()
            .Where(row => categoryIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        Require(categories.Count == document.Categories.Count, "Imported category set is incomplete.");
        foreach (var row in document.Categories)
        {
            var existing = categories[row.Id];
            Require(
                existing.ParentCategoryId == row.ParentCategoryId
                && existing.Name == row.Name
                && existing.Slug == row.Slug
                && existing.IsActive,
                $"Category '{row.Id}' conflicts with the workbook.");
        }

        var brandIds = document.Brands.Select(row => row.Id).ToArray();
        var brands = await dbContext.Brands
            .AsNoTracking()
            .Where(row => brandIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        Require(brands.Count == document.Brands.Count, "Imported brand set is incomplete.");
        foreach (var row in document.Brands)
        {
            var existing = brands[row.Id];
            Require(
                existing.Name == row.Name && existing.Slug == row.Slug && existing.IsActive,
                $"Brand '{row.Id}' conflicts with the workbook.");
        }

        var productIds = document.Products.Select(row => row.Id).ToArray();
        var products = await dbContext.Products
            .AsNoTracking()
            .Where(row => productIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        Require(products.Count == document.Products.Count, "Imported product set is incomplete.");
        foreach (var row in document.Products)
        {
            ValidateProduct(products[row.Id], row);
        }

        var stocks = await dbContext.Stocks
            .AsNoTracking()
            .Where(row => productIds.Contains(row.ProductId))
            .ToDictionaryAsync(row => row.ProductId, cancellationToken);
        Require(stocks.Count == document.Products.Count, "Imported stock set is incomplete.");
        foreach (var row in document.Products)
        {
            Require(
                stocks[row.Id].Quantity == document.FinalStockByProductId[row.Id],
                $"Product '{row.Sku}' final stock conflicts with the workbook history.");
        }

        await ValidateCompleteOrdersAsync(document, cancellationToken);
        await ValidateCompleteMovementsAsync(document, cancellationToken);
        await ValidateCompleteOutboxAsync(document, products, expectErpEvents, cancellationToken);
    }

    private async Task ValidateCompleteIdentityAndCustomersAsync(
        DemoImportDocument document,
        CancellationToken cancellationToken)
    {
        Require(await roleManager.RoleExistsAsync(CustomerRole), "Customer role is missing.");
        var customerIds = document.Customers.Select(row => row.Id).ToArray();
        var customers = await dbContext.Customers
            .AsNoTracking()
            .Where(row => customerIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        Require(customers.Count == document.Customers.Count, "Imported customer set is incomplete.");

        foreach (var row in document.Customers)
        {
            var user = await userManager.FindByIdAsync(UserId(row.Id).ToString());
            if (user is null)
            {
                throw Conflict($"Identity user for customer '{row.Id}' is missing.");
            }

            Require(
                string.Equals(user.Email, row.Email, StringComparison.OrdinalIgnoreCase),
                $"Identity user for customer '{row.Id}' conflicts with the workbook.");
            Require(
                await userManager.IsInRoleAsync(user, CustomerRole),
                $"Identity user for customer '{row.Id}' lacks the Customer role.");

            var customer = customers[row.Id];
            Require(
                customer.UserId == UserId(row.Id)
                && customer.FirstName == row.FirstName
                && customer.LastName == row.LastName
                && customer.IsActive,
                $"Customer '{row.Id}' conflicts with the workbook.");
        }

        var addressIds = document.CustomerAddresses.Select(row => row.Id).ToArray();
        var addresses = await dbContext.CustomerAddresses
            .AsNoTracking()
            .Where(row => addressIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        Require(
            addresses.Count == document.CustomerAddresses.Count,
            "Imported customer-address set is incomplete.");
        foreach (var row in document.CustomerAddresses)
        {
            ValidateCustomerAddress(addresses[row.Id], row);
        }
    }

    private async Task ValidateCompleteOrdersAsync(
        DemoImportDocument document,
        CancellationToken cancellationToken)
    {
        var orderIds = document.Orders.Select(row => row.Id).ToArray();
        var cartIds = orderIds.Select(OrderIdToCartId).ToArray();
        var orders = await dbContext.Orders
            .AsNoTracking()
            .Where(row => orderIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var carts = await dbContext.Carts
            .AsNoTracking()
            .Where(row => cartIds.Contains(row.Id))
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        var cartItems = await dbContext.CartItems
            .AsNoTracking()
            .Where(row => cartIds.Contains(row.CartId))
            .ToListAsync(cancellationToken);
        var orderItems = await dbContext.OrderItems
            .AsNoTracking()
            .Where(row => orderIds.Contains(row.OrderId))
            .ToListAsync(cancellationToken);
        var orderAddresses = await dbContext.OrderAddresses
            .AsNoTracking()
            .Where(row => orderIds.Contains(row.OrderId))
            .ToDictionaryAsync(row => row.OrderId, cancellationToken);
        var payments = await dbContext.Payments
            .AsNoTracking()
            .Where(row => orderIds.Contains(row.OrderId))
            .ToDictionaryAsync(row => row.OrderId, cancellationToken);
        var itemsByOrder = document.OrderItems.ToLookup(row => row.OrderId);
        var addressesByCustomer = document.CustomerAddresses.ToLookup(row => row.CustomerId);

        Require(orders.Count == document.Orders.Count, "Imported order set is incomplete.");
        Require(carts.Count == document.Orders.Count, "Imported converted-cart set is incomplete.");
        Require(orderAddresses.Count == document.Orders.Count, "Imported order-address set is incomplete.");
        Require(payments.Count == document.Orders.Count, "Imported payment set is incomplete.");
        Require(orderItems.Count == document.OrderItems.Count, "Imported order-item set is incomplete.");
        Require(cartItems.Count == document.OrderItems.Count, "Imported cart-item set is incomplete.");

        foreach (var row in document.Orders)
        {
            var order = orders[row.Id];
            Require(
                order.OrderNumber == row.OrderNumber
                && order.CustomerId == row.CustomerId
                && order.SourceCartId == CartId(row.Id)
                && order.Status == OrderStatus.Confirmed
                && order.Subtotal == row.Subtotal
                && order.VatTotal == row.VatTotal
                && order.GrandTotal == row.GrandTotal
                && order.Currency == Currency
                && order.CorrelationId == CorrelationId(row.Id)
                && order.PlacedAtUtc == row.PlacedAtUtc,
                $"Order '{row.OrderNumber}' conflicts with the workbook.");

            var cart = carts[CartId(row.Id)];
            Require(
                cart.CustomerId == row.CustomerId && cart.Status == CartStatus.Converted,
                $"Order '{row.OrderNumber}' source cart conflicts with the workbook.");

            var payment = payments[row.Id];
            Require(
                payment.Id == PaymentId(row.Id)
                && payment.Method == PaymentMethod.CashSimulation
                && payment.Status == PaymentStatus.Succeeded
                && payment.Amount == row.GrandTotal
                && payment.SimulationReference == PaymentReference(row.Id)
                && payment.ProcessedAtUtc == row.PlacedAtUtc,
                $"Order '{row.OrderNumber}' payment conflicts with the workbook.");

            var selectedAddress = addressesByCustomer[row.CustomerId]
                .OrderByDescending(address => address.IsDefault)
                .ThenBy(address => address.Id)
                .First();
            var snapshot = orderAddresses[row.Id];
            Require(
                snapshot.RecipientName == selectedAddress.RecipientName
                && snapshot.PhoneNumber == selectedAddress.PhoneNumber
                && snapshot.AddressLine1 == selectedAddress.AddressLine1
                && snapshot.AddressLine2 is null
                && snapshot.District == selectedAddress.District
                && snapshot.City == selectedAddress.City
                && snapshot.PostalCode == selectedAddress.PostalCode
                && snapshot.CountryCode == selectedAddress.CountryCode,
                $"Order '{row.OrderNumber}' address snapshot conflicts with the workbook.");

            foreach (var itemRow in itemsByOrder[row.Id])
            {
                var item = AssertSingle(
                    orderItems.Where(item =>
                        item.OrderId == row.Id && item.ProductId == itemRow.ProductId),
                    $"order item '{row.Id}/{itemRow.ProductId}'");
                Require(
                    item.Id == OrderItemId(row.Id, itemRow.ProductId)
                    && item.SkuSnapshot == itemRow.SkuSnapshot
                    && item.ProductNameSnapshot == itemRow.ProductNameSnapshot
                    && item.Quantity == itemRow.Quantity
                    && item.UnitPrice == itemRow.UnitPrice
                    && item.VatRate == itemRow.VatRate
                    && item.NetLineAmount == itemRow.NetLineAmount
                    && item.VatAmount == itemRow.VatAmount
                    && item.LineTotal == itemRow.LineTotal,
                    $"Order item '{row.Id}/{itemRow.ProductId}' conflicts with the workbook.");

                var cartItem = AssertSingle(
                    cartItems.Where(item =>
                        item.CartId == CartId(row.Id) && item.ProductId == itemRow.ProductId),
                    $"cart item '{row.Id}/{itemRow.ProductId}'");
                Require(
                    cartItem.Id == CartItemId(row.Id, itemRow.ProductId)
                    && cartItem.Quantity == itemRow.Quantity
                    && cartItem.LastKnownUnitPrice == itemRow.UnitPrice,
                    $"Cart item '{row.Id}/{itemRow.ProductId}' conflicts with the workbook.");
            }
        }
    }

    private async Task ValidateCompleteMovementsAsync(
        DemoImportDocument document,
        CancellationToken cancellationToken)
    {
        var productIds = document.Products.Select(row => row.Id).ToArray();
        var movements = await dbContext.StockMovements
            .AsNoTracking()
            .Where(row => productIds.Contains(row.ProductId))
            .ToListAsync(cancellationToken);
        var expectedCount = document.Products.Count(row => row.InitialStock > 0)
                            + document.OrderItems.Count;
        Require(movements.Count == expectedCount, "Imported stock-movement set conflicts with the workbook.");

        foreach (var product in document.Products.Where(row => row.InitialStock > 0))
        {
            var movement = AssertSingle(
                movements.Where(row =>
                    row.ProductId == product.Id
                    && row.MovementType == StockMovementType.Initial),
                $"initial movement for product '{product.Sku}'");
            Require(
                movement.QuantityChange == product.InitialStock
                && movement.PreviousQuantity == 0
                && movement.NewQuantity == product.InitialStock
                && movement.ReferenceType == StockReferenceType.Seed
                && movement.ReferenceId is null,
                $"Initial movement for product '{product.Sku}' conflicts with the workbook.");
        }

        var runningStock = document.Products.ToDictionary(row => row.Id, row => row.InitialStock);
        var itemsByOrder = document.OrderItems.ToLookup(row => row.OrderId);
        foreach (var order in document.Orders)
        {
            foreach (var item in itemsByOrder[order.Id])
            {
                var previous = runningStock[item.ProductId];
                var next = previous - item.Quantity;
                runningStock[item.ProductId] = next;
                var movement = AssertSingle(
                    movements.Where(row =>
                        row.ProductId == item.ProductId
                        && row.MovementType == StockMovementType.Sale
                        && row.ReferenceType == StockReferenceType.Order
                        && row.ReferenceId == order.Id),
                    $"sale movement for order/product '{order.Id}/{item.ProductId}'");
                Require(
                    movement.QuantityChange == -item.Quantity
                    && movement.PreviousQuantity == previous
                    && movement.NewQuantity == next
                    && movement.CreatedAtUtc == order.PlacedAtUtc,
                    $"Sale movement for order/product '{order.Id}/{item.ProductId}' conflicts with the workbook.");
            }
        }
    }

    private async Task ValidateCompleteOutboxAsync(
        DemoImportDocument document,
        IReadOnlyDictionary<Guid, Product> products,
        bool expectErpEvents,
        CancellationToken cancellationToken)
    {
        var expectedIds = ExpectedEventIds(document, expectErpEvents);
        var messages = await dbContext.OutboxMessages
            .AsNoTracking()
            .Where(row => expectedIds.Contains(row.EventId))
            .ToDictionaryAsync(row => row.EventId, cancellationToken);
        Require(messages.Count == expectedIds.Count, "Imported Outbox set is incomplete.");

        var categories = document.Categories.ToDictionary(row => row.Id);
        foreach (var row in document.Products)
        {
            var product = products[row.Id];
            var expectedEvent = new ProductSnapshotChangedV1(
                ProductEventId(row.Id),
                document.CatalogueOccurredAtUtc,
                ProductCorrelationId(row.Id),
                row.Id,
                row.Sku,
                row.Name,
                product.Description,
                row.CategoryId,
                categories[row.CategoryId].ParentCategoryId,
                row.BrandId,
                row.Price,
                row.NetContent,
                row.UnitType,
                true,
                document.FinalStockByProductId[row.Id] > 0,
                AsUtc(product.UpdatedAtUtc));
            ValidateOutbox(
                messages[expectedEvent.EventId],
                OutboxMessageFactory.Create(
                    expectedEvent,
                    OutboxMessageFactory.RecommendationDestination,
                    "Product",
                    row.Id));
        }

        var itemsByOrder = document.OrderItems.ToLookup(row => row.OrderId);
        var customers = document.Customers.ToDictionary(row => row.Id);
        var addressesByCustomer = document.CustomerAddresses.ToLookup(row => row.CustomerId);
        foreach (var order in document.Orders)
        {
            var recommendationEvent = new OrderConfirmedForRecommendationV1(
                RecommendationEventId(order.Id),
                order.PlacedAtUtc,
                CorrelationId(order.Id),
                order.Id,
                order.OrderNumber,
                order.CustomerId,
                itemsByOrder[order.Id]
                    .Select(item => new RecommendationOrderItemV1(item.ProductId, item.Quantity))
                    .ToArray());
            ValidateOutbox(
                messages[recommendationEvent.EventId],
                OutboxMessageFactory.Create(
                    recommendationEvent,
                    OutboxMessageFactory.RecommendationDestination,
                    "Order",
                    order.Id));

            if (!expectErpEvents)
            {
                continue;
            }

            var customer = customers[order.CustomerId];
            var address = addressesByCustomer[order.CustomerId]
                .OrderByDescending(row => row.IsDefault)
                .ThenBy(row => row.Id)
                .First();
            var erpEvent = new OrderReadyForErpV1(
                ErpEventId(order.Id),
                order.PlacedAtUtc,
                CorrelationId(order.Id),
                order.Id,
                order.OrderNumber,
                order.PlacedAtUtc,
                PaymentMethod.CashSimulation,
                new ErpOrderCustomerV1(
                    customer.Id,
                    customer.FirstName,
                    customer.LastName,
                    customer.Email),
                new ErpOrderAddressV1(
                    address.RecipientName,
                    address.PhoneNumber,
                    address.AddressLine1,
                    null,
                    address.District,
                    address.City,
                    address.PostalCode,
                    address.CountryCode),
                new ErpOrderTotalsV1(
                    order.Subtotal,
                    order.VatTotal,
                    order.GrandTotal,
                    Currency),
                itemsByOrder[order.Id].Select(item => new ErpOrderItemV1(
                    item.ProductId,
                    item.SkuSnapshot,
                    item.ProductNameSnapshot,
                    item.Quantity,
                    item.UnitPrice,
                    item.VatRate,
                    item.NetLineAmount,
                    item.VatAmount,
                    item.LineTotal)).ToArray());
            ValidateOutbox(
                messages[erpEvent.EventId],
                OutboxMessageFactory.Create(
                    erpEvent,
                    OutboxMessageFactory.ErpIntegrationDestination,
                    "Order",
                    order.Id));
        }

        var maximumProductId = messages.Values
            .Where(message => message.EventType == nameof(ProductSnapshotChangedV1))
            .Max(message => message.Id);
        var minimumRecommendationOrderId = messages.Values
            .Where(message => message.EventType == nameof(OrderConfirmedForRecommendationV1))
            .Min(message => message.Id);
        Require(
            maximumProductId < minimumRecommendationOrderId,
            "Product Outbox rows must precede recommendation order rows for the current Id-ordered claimant.");
    }

    private static void ValidateOutbox(OutboxMessage actual, OutboxMessage expected)
    {
        Require(
            actual.EventType == expected.EventType
            && actual.Destination == expected.Destination
            && actual.AggregateType == expected.AggregateType
            && actual.AggregateId == expected.AggregateId
            && actual.Payload == expected.Payload
            && actual.OccurredAtUtc == expected.OccurredAtUtc
            && actual.AvailableAtUtc == expected.AvailableAtUtc
            && actual.CorrelationId == expected.CorrelationId,
            $"Outbox event '{expected.EventId}' conflicts with the workbook.");
    }

    private static void ValidateProduct(Product existing, DemoProductRow row)
    {
        Require(
            existing.Sku == row.Sku
            && existing.Name == row.Name
            && existing.Slug == row.Slug
            && existing.CategoryId == row.CategoryId
            && existing.BrandId == row.BrandId
            && existing.Price == row.Price
            && existing.VatRate == row.VatRate
            && existing.NetContent == row.NetContent
            && existing.UnitType == row.UnitType
            && existing.IsActive,
            $"Product '{row.Sku}' conflicts with the workbook.");
    }

    private static void ValidateCustomerAddress(
        CustomerAddress existing,
        DemoCustomerAddressRow row)
    {
        Require(
            existing.CustomerId == row.CustomerId
            && existing.Title == AddressTitle
            && existing.ContactName == row.RecipientName
            && existing.PhoneNumber == row.PhoneNumber
            && existing.AddressLine1 == row.AddressLine1
            && existing.AddressLine2 is null
            && existing.District == row.District
            && existing.City == row.City
            && existing.PostalCode == row.PostalCode
            && existing.CountryCode == row.CountryCode
            && existing.IsDefault == row.IsDefault
            && existing.IsActive,
            $"Customer address '{row.Id}' conflicts with the workbook.");
    }

    private static DemoExcelImportResult CreateResult(
        DemoImportDocument document,
        bool emitHistoricalErpEvents,
        bool alreadyImported)
    {
        return new DemoExcelImportResult(
            alreadyImported,
            document.Categories.Count,
            document.Brands.Count,
            document.Products.Count,
            document.Customers.Count,
            document.CustomerAddresses.Count,
            document.Orders.Count,
            document.OrderItems.Count,
            document.Products.Count,
            document.Orders.Count,
            emitHistoricalErpEvents ? document.Orders.Count : 0);
    }

    private static HashSet<Guid> ExpectedEventIds(
        DemoImportDocument document,
        bool includeErp)
    {
        var ids = document.Products.Select(row => ProductEventId(row.Id))
            .Concat(document.Orders.Select(row => RecommendationEventId(row.Id)))
            .ToHashSet();
        if (includeErp)
        {
            ids.UnionWith(document.Orders.Select(row => ErpEventId(row.Id)));
        }

        return ids;
    }

    private static Guid UserId(Guid customerId) => DeterministicId("identity-user", customerId);
    private static Guid CartId(Guid orderId) => DeterministicId("source-cart", orderId);
    private static Guid OrderIdToCartId(Guid orderId) => CartId(orderId);
    private static Guid CartItemId(Guid orderId, Guid productId) =>
        DeterministicId("cart-item", orderId, productId);
    private static Guid OrderItemId(Guid orderId, Guid productId) =>
        DeterministicId("order-item", orderId, productId);
    private static Guid PaymentId(Guid orderId) => DeterministicId("payment", orderId);
    private static Guid CorrelationId(Guid orderId) => DeterministicId("correlation", orderId);
    private static Guid ProductCorrelationId(Guid productId) =>
        DeterministicId("product-correlation", productId);
    private static Guid ProductEventId(Guid productId) =>
        DeterministicId("product-event", productId);
    private static Guid RecommendationEventId(Guid orderId) =>
        DeterministicId("recommendation-order-event", orderId);
    private static Guid ErpEventId(Guid orderId) => DeterministicId("erp-order-event", orderId);
    private static string PaymentReference(Guid orderId) => $"DEMO-CASH-{orderId:N}";

    private static DateTime AsUtc(DateTime value)
    {
        return value.Kind == DateTimeKind.Utc
            ? value
            : DateTime.SpecifyKind(value, DateTimeKind.Utc);
    }

    private static Guid DeterministicId(string purpose, params Guid[] sourceIds)
    {
        var material = purpose + ":" + string.Join(':', sourceIds.Select(id => id.ToString("N")));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
        var bytes = hash[..16];
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50);
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80);
        return new Guid(bytes);
    }

    private static T AssertSingle<T>(IEnumerable<T> values, string description)
    {
        var items = values.Take(2).ToArray();
        if (items.Length != 1)
        {
            throw Conflict($"Expected exactly one {description}, found {items.Length}.");
        }

        return items[0];
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw Conflict(message);
        }
    }

    private static InvalidOperationException Conflict(string message)
    {
        return new InvalidOperationException($"Demo Excel import conflict: {message}");
    }

    private static void ThrowIfIdentityFailed(IdentityResult result, string message)
    {
        if (result.Succeeded)
        {
            return;
        }

        var codes = string.Join(", ", result.Errors.Select(error => error.Code));
        throw new InvalidOperationException($"{message} Identity codes: {codes}");
    }
}
