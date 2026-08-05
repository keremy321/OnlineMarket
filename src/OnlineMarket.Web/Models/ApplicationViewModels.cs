using System.ComponentModel.DataAnnotations;
using OnlineMarket.Web.Application.Models;
using OnlineMarket.Web.Domain.Enums;

namespace OnlineMarket.Web.Models;

public class LoginViewModel
{
    [Required(ErrorMessage = "E-posta adresi zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre zorunludur.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    public bool RememberMe { get; set; }
    public string? ReturnUrl { get; set; }
}

public class RegisterViewModel
{
    [Required(ErrorMessage = "Ad zorunludur.")]
    public string FirstName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Soyad zorunludur.")]
    public string LastName { get; set; } = string.Empty;

    [Required(ErrorMessage = "E-posta adresi zorunludur.")]
    [EmailAddress(ErrorMessage = "Geçerli bir e-posta adresi giriniz.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre zorunludur.")]
    [MinLength(8, ErrorMessage = "Şifre en az 8 karakter olmalıdır.")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Şifre tekrarı zorunludur.")]
    [Compare("Password", ErrorMessage = "Şifreler uyuşmuyor.")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class AddressFormViewModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "Adres başlığı zorunludur. (Örn: Ev, İş)")]
    public string Title { get; set; } = string.Empty;

    [Required(ErrorMessage = "Teslim alacak kişi adı zorunludur.")]
    public string ContactName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Telefon numarası zorunludur.")]
    [Phone(ErrorMessage = "Geçerli bir telefon numarası giriniz.")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required(ErrorMessage = "Adres satırı zorunludur.")]
    public string AddressLine1 { get; set; } = string.Empty;

    public string? AddressLine2 { get; set; }

    [Required(ErrorMessage = "İlçe zorunludur.")]
    public string District { get; set; } = string.Empty;

    [Required(ErrorMessage = "İl zorunludur.")]
    public string City { get; set; } = string.Empty;

    public string? PostalCode { get; set; }

    public bool IsDefault { get; set; }
}

public class HomeViewModel
{
    public List<CategoryDto> Categories { get; set; } = new();
    public List<ProductDto> FeaturedProducts { get; set; } = new();
    public List<RecommendationItemDto> PopularRecommendations { get; set; } = new();
    public List<RecommendationItemDto> PersonalizedRecommendations { get; set; } = new();
    public List<RecommendationItemDto> FrequentlyBoughtTogether { get; set; } = new();
    public List<RecommendationItemDto> SimilarProducts { get; set; } = new();
    public List<RecommendationItemDto> CartCompletionRecommendations { get; set; } = new();
}

public class CatalogIndexViewModel
{
    public List<ProductDto> Products { get; set; } = new();
    public List<CategoryDto> Categories { get; set; } = new();
    public List<BrandDto> Brands { get; set; } = new();
    public List<RecommendationItemDto> PopularRecommendations { get; set; } = new();
    public List<RecommendationItemDto> PersonalizedRecommendations { get; set; } = new();

    public Guid? SelectedCategoryId { get; set; }
    public Guid? SelectedBrandId { get; set; }
    public string? SearchQuery { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public bool InStockOnly { get; set; }
    public string SortBy { get; set; } = "newest";
}

public class ProductDetailViewModel
{
    public ProductDto Product { get; set; } = null!;
    public List<RecommendationItemDto> FrequentlyBoughtTogether { get; set; } = new();
    public List<RecommendationItemDto> SimilarProducts { get; set; } = new();
}

public class CartIndexViewModel
{
    public CartDto Cart { get; set; } = null!;
    public List<RecommendationItemDto> CartCompletionRecommendations { get; set; } = new();
}

public class CheckoutIndexViewModel
{
    public CartDto Cart { get; set; } = null!;
    public List<CustomerAddressDto> Addresses { get; set; } = new();
    public Guid SelectedAddressId { get; set; }
    public string CardHolderName { get; set; } = "Test Müşteri";
    public string CardNumberMasked { get; set; } = "**** **** **** 1234";
    public bool SimulateSuccess { get; set; } = true;
    public string? ErrorMessage { get; set; }
}

public class OrderSuccessViewModel
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
}

public class AdminDashboardViewModel
{
    public int TotalProducts { get; set; }
    public int OutOfStockProducts { get; set; }
    public int TotalOrders { get; set; }
    public int PendingOutboxMessages { get; set; }
    public List<OrderDto> RecentOrders { get; set; } = new();
}

public sealed class AdminOutboxMessageViewModel
{
    public long Id { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public Guid AggregateId { get; set; }
    public OutboxStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime? ProcessedAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
    public string? MaskedLastError { get; set; }
}

public class ProductFormViewModel
{
    public Guid? Id { get; set; }

    [Required(ErrorMessage = "SKU zorunludur.")]
    [StringLength(64)]
    public string Sku { get; set; } = string.Empty;

    [Required(ErrorMessage = "Ürün adı zorunludur.")]
    [StringLength(200)]
    public string Name { get; set; } = string.Empty;

    [StringLength(2000)]
    public string? Description { get; set; }

    [Required(ErrorMessage = "Kategori seçiniz.")]
    public Guid CategoryId { get; set; }

    [Required(ErrorMessage = "Marka seçiniz.")]
    public Guid BrandId { get; set; }

    [Required(ErrorMessage = "Fiyat zorunludur.")]
    [Range(0.01, 100000, ErrorMessage = "Fiyat 0'dan büyük olmalıdır.")]
    public decimal Price { get; set; }

    [Required(ErrorMessage = "KDV Oranı zorunludur.")]
    public decimal VatRate { get; set; } = 20.00m;

    [Required(ErrorMessage = "Net miktar zorunludur.")]
    public decimal NetContent { get; set; } = 1.000m;

    public UnitType UnitType { get; set; } = UnitType.Piece;

    [StringLength(500)]
    public string? ImageUrl { get; set; }

    public Microsoft.AspNetCore.Http.IFormFile? ImageFile { get; set; }

    public bool IsActive { get; set; } = true;

    [Range(0, 100000, ErrorMessage = "Stok 0 veya daha fazla olmalıdır.")]
    public int InitialStock { get; set; } = 100;

    public List<CategoryDto> Categories { get; set; } = new();
    public List<BrandDto> Brands { get; set; } = new();
}

public class StockAdjustViewModel
{
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int CurrentStock { get; set; }

    [Required(ErrorMessage = "Stok değişim miktarı zorunludur.")]
    public int QuantityChange { get; set; }

    [Required(ErrorMessage = "Açıklama/Neden zorunludur.")]
    [StringLength(500)]
    public string Reason { get; set; } = string.Empty;
}
