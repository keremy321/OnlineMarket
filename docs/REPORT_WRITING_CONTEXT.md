# Online Market Projesi — Rapor Yazım Bağlamı

> Bu dosya doğrudan teslim edilecek proje raporu değildir. Raporu yazacak kişi
> veya yapay zekâ aracı için güncel uygulama bağlamı, kanıt yolları, terminoloji
> ve doğrulanmış sonuçları sağlar.

Bu belgedeki tüm iddialar **2026-08-06** tarihinde depodaki çalıştırılabilir
kaynak koddan doğrulanmıştır. Bir iddiayı rapora aktarmadan önce belirtilen
kanıt dosyasını açıp teyit edin.

---

## 1. Raporun Amacı

Nihai Türkçe staj raporu en az şu başlıkları kapsamalıdır:

- **Görev Dağılımı** — ekip üyelerinin sorumluluk alanları.
- **Amaç ve Kapsam** — çözülen iş problemi ve sınırlar.
- **Süreçler** — geliştirme akışı, dal/commit düzeni, doğrulama adımları.
- **Analiz ve Gereksinimler** — fonksiyonel ve fonksiyonel olmayan
  gereksinimler.
- **Kullanılan Yöntem ve Teknolojiler** — mimari kararlar ve teknoloji yığını.
- **Yapay zekâ / öneri yöntemleri** — TF-IDF, ALS, hibrit sıralama,
  birliktelik kuralları, popülerlik.
- **Testler** — birim, entegrasyon, uçtan uca doğrulama.
- **Sonuç ve değerlendirme** — elde edilenler, sınırlar, gelecek çalışmalar.

Rapor **uygulanan sistemi** anlatmalı; eski planlama belgelerindeki niyetleri
uygulanmış gibi sunmamalıdır.

---

## 2. Proje Kimliği

| Alan | Değer | Kanıt |
|---|---|---|
| Proje adı | Online Market — Öneri ve ERP Entegrasyon Sistemi | [README.md](../README.md) |
| Bağlam | Uyumsoft stajyerlik projesi | [AGENTS.md](../AGENTS.md) |
| Ekip | Grup 16 — Kerem Yılmaz, Mert Danacı, Z. Yudum Meral | Onaylı proje belgeleri |
| Çözüm dosyası | `OnlineMarket.slnx` | [OnlineMarket.slnx](../OnlineMarket.slnx) |
| Çalışma zamanı | .NET 10 (`net10.0`), SDK 10.0.300 | [global.json](../global.json) |

**Doğrulanamayan alanlar (rapora yazmadan önce teyit edin):**

- Proje dönemi / tarih aralığı — depoda kayıtlı değildir.
- Teslim ve demo tarihi — depoda kayıtlı değildir.
- Ekip üyelerinin yüzdesel katkı oranları — depoda kayıtlı değildir; **yüzde
  uydurmayın**. `docs/ai/WORK_PLAN.md` yalnızca planlanan iş bölümünü içerir,
  gerçekleşen katkı oranını değil.

---

## 3. Özgün Problem Tanımı

Stajın başında tanımlanan gereksinimler:

1. Alışveriş davranışına dayalı ürün önerileri üretmek.
2. Sepet içeriğine göre sepet tamamlama önerileri sunmak.
3. Onaylanan siparişleri bir ERP sistemine aktarmak.
4. Müşteri, sipariş, stok ve muhasebe kayıtlarını ERP tarafında otomatik
   oluşturmak.

**Özgün gereksinim ile nihai uygulama arasındaki fark:**

| Özgün gereksinim | Nihai uygulama |
|---|---|
| "ERP entegrasyonu" | Gerçek Uyumsoft ERP'ye bağlanılmadı. ERP davranışı `MockErp.Api` ile **simüle edildi**; entegrasyon akışı (`ErpIntegration.Api`) gerçek bir ERP adaptörü takılabilecek şekilde ayrı tutuldu. |
| "Öneri sistemi" | Beş öneri türü uygulandı; ek olarak Python tabanlı TF-IDF + ALS + hibrit sıralama ve çevrimdışı değerlendirme eklendi. |
| (Planlanmamıştı) | Mağaza içi **yapay zekâ asistanı** eklendi. Ürün önerileri yine `Recommendation.Api`'den gelir; LLM yalnızca açıklama üretir. |
| (Planlanmıştı) | **Sentetik veri üretici servis** nihai çalışma zamanının parçası değildir. Demo verisi `scripts/seed/` altındaki sabit dosyalardan gelir. |

---

## 4. Nihai Uygulanan Kapsam — Özellik Matrisi

| Alan | Uygulandı | Kanıt | Notlar |
|---|---|---|---|
| Mağaza (MVC) | Evet | `src/OnlineMarket.Web/Controllers/`, `src/OnlineMarket.Web/Views/` | Katalog, sepet, checkout, sipariş geçmişi, admin |
| Kimlik doğrulama | Evet | `src/OnlineMarket.Web/Program.cs`, `src/OnlineMarket.Web/Controllers/AccountController.cs` | ASP.NET Core Identity, `Customer`/`Admin` rolleri |
| Katalog | Evet | `src/OnlineMarket.Web/Application/Services/CatalogService.cs` | Kategori, marka, arama, filtre, sıralama |
| Sepet | Evet | `src/OnlineMarket.Web/Application/Services/CartService.cs` | Sunucu tarafında tutulur |
| Checkout | Evet | `src/OnlineMarket.Web/Application/Services/CheckoutService.cs` | Tek transaction, execution strategy ile |
| Outbox | Evet | `src/OnlineMarket.Web/Infrastructure/Persistence/SqlServerOutboxStore.cs`, `.../Workers/OutboxBackgroundWorker.cs` | Maksimum 5 deneme |
| Öneri olay yükleme | Evet | `src/Recommendation.Api/Controllers/RecommendationEventsController.cs` | `/api/v1/events/products`, `/api/v1/events/orders` |
| Popüler öneriler | Evet | `src/Recommendation.Api/Application/Services/PopularityRecommendationService.cs` | SQL tabanlı, elle yeniden hesaplanır |
| FBT | Evet | `src/Recommendation.Api/Application/Services/FrequentlyBoughtTogetherRecommendationService.cs` | Support/confidence/lift eşikleri |
| Sepet tamamlama | Evet | `src/Recommendation.Api/Application/Services/CartCompletionRecommendationService.cs` | Ağırlıklı skor |
| Benzer ürünler | Evet | `src/Recommendation.Api/Application/Services/SimilarRecommendationService.cs` | TF-IDF tabanlı |
| Kişiselleştirilmiş | Evet | `src/Recommendation.Api/Application/Services/PersonalizedRecommendationService.cs` | `ModelStrategy`: `Als` veya `Hybrid` |
| TF-IDF | Evet | `src/Recommendation.ModelService/app/features.py` | Python |
| ALS | Evet | `src/Recommendation.ModelService/app/als.py` | `implicit` 0.7.3 |
| Hibrit sıralama | Evet | `src/Recommendation.ModelService/app/hybrid.py` | Yapılandırılabilir ağırlıklar |
| Model değerlendirme | Evet | `src/Recommendation.ModelService/app/evaluation.py`, `src/Recommendation.Api/Application/Services/RecommendationModelEvaluationService.cs` | Kronolojik holdout |
| Otomatik yeniden eğitim | **Hayır** | — | Zamanlayıcı yoktur; elle tetiklenir |
| Sentetik veri üretici | **Hayır** | — | Nihai çalışma zamanında yoktur |
| AI Asistanı | Evet | `src/OnlineMarket.Web/Application/Services/AiSupportService.cs`, `.../AiRecommendationOrchestrator.cs`, `.../AiIntentRouter.cs` | Öneriler yalnızca Recommendation.Api'den |
| ERP entegrasyonu | Evet | `src/ErpIntegration.Api/Infrastructure/Workers/IntegrationWorker.cs`, `.../Application/Services/IntegrationStepProcessor.cs` | Dört adımlı durum makinesi |
| Mock ERP | Evet (simülasyon) | `src/MockErp.Api/Controllers/` | Idempotent, hız sınırlı |
| API dokümantasyonu | Evet (3 API) | `src/*/Program.cs` (`MapScalarApiReference`) | Yalnızca Development; `OnlineMarket.Web` hariç |
| Testler | Evet | `tests/` (4 proje) + `src/Recommendation.ModelService/tests/` | Bkz. bölüm 14 |
| Docker / yerel geliştirme | Evet | `deploy/docker-compose.*.yml`, `scripts/database/*.ps1` | SQL Server ve ModelService konteynerleri |

---

## 5. Mimari Kararlar ve Gerekçeleri

Her karar için: **problem → karar → fayda → ödünleşim**.

### 5.1 Seçici mikroservis (hibrit) mimarisi

- **Problem:** Öneri hesaplama, ERP aktarımı ve mağaza aynı süreçte çalışırsa
  biri diğerini yavaşlatır veya çökertir.
- **Karar:** Yalnızca farklı ölçekleme/başarısızlık profiline sahip yetenekler
  ayrı servis yapıldı. Her şey mikroservise bölünmedi.
- **Fayda:** Bağımsız dağıtım ve arıza izolasyonu; ekip üyeleri paralel
  çalışabildi.
- **Ödünleşim:** Ağ çağrısı, sözleşme yönetimi ve dağıtık hata ayıklama
  maliyeti arttı.

### 5.2 Ayrı veritabanları

- **Problem:** Ortak şema, servisleri birbirine kilitler; bir migration diğer
  servisi bozar.
- **Karar:** Dört ayrı veritabanı; cross-database FK, join, view ve procedure
  yasak.
- **Fayda:** Bağımsız şema evrimi; net sahiplik.
- **Ödünleşim:** Veri çoğaltma (snapshot) ve nihai tutarlılık kabul edildi.

### 5.3 HTTP/JSON + olay tabanlı iletişim

- **Problem:** Servisler arası doğrudan veritabanı erişimi bağımlılık yaratır.
- **Karar:** Sürümlü JSON sözleşmeleri (`...V1`) üzerinden HTTP.
- **Fayda:** Sözleşme testi yazılabilir; gerçek ERP adaptörü ileride aynı
  arayüzü uygulayabilir.
- **Ödünleşim:** Serileştirme ve sürüm yönetimi ek iş getirir.

### 5.4 Checkout'un ERP'yi senkron çağırmaması

- **Problem:** Checkout içinde ERP çağrısı yapılırsa ERP yavaşladığında
  transaction uzar, kilitlenme ve zaman aşımı riski doğar; ERP kapalıysa
  müşteri sipariş veremez.
- **Karar:** Checkout transaction'ı içinde **hiçbir dış HTTP çağrısı yok**;
  niyet Outbox satırı olarak yazılır.
- **Fayda:** Sipariş her hâlükârda kaydedilir; ERP arızası müşteriyi
  etkilemez.
- **Ödünleşim:** ERP kaydı nihai tutarlıdır (anında değil); durum sorgulaması
  ayrı uç noktadan yapılır.
- **Kanıt:** `src/OnlineMarket.Web/Application/Services/CheckoutService.cs`

### 5.5 Transactional Outbox seçimi

- **Problem:** "Veritabanına yaz + mesaj gönder" iki ayrı işlemdir; arada çöküş
  olursa ya sipariş kaybolur ya mükerrer mesaj gider (dual-write problemi).
- **Karar:** Mesaj, iş verisiyle **aynı transaction'da** aynı veritabanına
  yazılır; ayrı bir işçi teslim eder.
- **Fayda:** En az bir kez teslim garantisi; mesaj broker altyapısı
  gerektirmez.
- **Ödünleşim:** İşçi yoklaması (polling) gecikme ekler; tüketiciler idempotent
  olmak zorundadır.
- **Kanıt:** `src/OnlineMarket.Web/Common/Messaging/OutboxMessageFactory.cs`,
  `src/OnlineMarket.Web/Infrastructure/Persistence/SqlServerOutboxStore.cs`

### 5.6 Mock ERP'nin ayrı servis olması

- **Problem:** Gerçek ERP erişimi staj kapsamında mevcut değildi; simülasyonu
  entegrasyon servisinin içine gömmek gerçek adaptör geçişini imkânsız
  kılardı.
- **Karar:** Simülasyon ayrı bir HTTP servisi (`MockErp.Api`) ve ayrı
  veritabanı olarak izole edildi.
- **Fayda:** `ErpIntegration.Api` gerçek bir ERP'ye bakıyormuş gibi yazıldı;
  taban adresi değiştirilerek gerçek adaptöre geçilebilir.
- **Ödünleşim:** Ek servis, ek veritabanı ve ek dağıtım yükü.

### 5.7 ModelService'in Recommendation.Api arkasında olması

- **Problem:** Python servisi doğrudan açılırsa kimlik doğrulama, katalog
  doğrulama ve PII sınırları ikinci kez uygulanmak zorunda kalır.
- **Karar:** Python servisi **dahili** tutuldu; yalnızca `Recommendation.Api`
  çağırır, veritabanı sürücüsü yoktur.
- **Fayda:** Tek güvenlik sınırı; Python yalnızca takma adlaştırılmış veri
  görür; C# tarafı nihai uygunluk kontrolünü yapar.
- **Ödünleşim:** Ek ağ atlaması ve devre kesici gereksinimi.
- **Kanıt:** `docs/recommendation-model-service.md`,
  `src/Recommendation.Api/Infrastructure/Http/RecommendationModelClient.cs`

### 5.8 AI asistanının ürün seçememesi

- **Problem:** LLM'ler var olmayan ürün, fiyat ve stok uydurur
  (halüsinasyon); bu bir e-ticaret sitesinde yanlış bilgilendirmedir.
- **Karar:** Ürün seçimi ve sıralaması **tamamen** `Recommendation.Api`'ye
  bırakıldı; LLM yalnızca verilen bağlamı cümleye döker. Yapılandırılmış ürün
  kartları sunucu tarafında üretilir.
- **Fayda:** Gösterilen her ürün gerçek, aktif ve stokta; LLM olmasa da sistem
  çalışır.
- **Ödünleşim:** Yanıtlar daha az "serbest"; niyet yönlendirici için deterministik
  Türkçe kural listesi bakımı gerekir.
- **Kanıt:**
  `src/OnlineMarket.Web/Application/Services/AiRecommendationOrchestrator.cs`

---

## 6. Bileşen Sorumlulukları

### OnlineMarket.Web

ASP.NET Core MVC mağaza. `OnlineMarketDb` sahibidir. Identity ile kimlik
doğrulama, katalog/sepet/checkout iş akışları, sipariş geçmişi ve admin
paneli. Outbox mesajlarını hem **üretir** (checkout) hem **teslim eder**
(`OutboxBackgroundWorker`). Öneri sonuçlarını `IRecommendationClient` ile
okur ve göstermeden önce kendi kataloğundan doğrular. AI asistan uç noktasını
barındırır. Tarayıcının konuştuğu tek servistir.

### Recommendation.Api

`RecommendationDb` sahibi ve tek genel öneri API'si. Outbox olaylarını alır,
`ProductSnapshot`/`OrderSnapshot` kayıtlarına dönüştürür, `SubjectId` türetir.
Popülerlik, FBT ve sepet tamamlama hesaplarını SQL üzerinde yapar; TF-IDF/ALS/
hibrit işleri için Python servisini çağırır. `X-Api-Key` ile korunur.

### Recommendation.ModelService

Dahili Python FastAPI servisi. Veritabanı sürücüsü ve bağlantı dizesi yoktur.
TF-IDF içerik vektörleri, implicit ALS ve hibrit sıralama eğitir; artifact'leri
diske yazar ve yeniden başlatmada en son geçerli olanı yükler. Çevrimdışı
değerlendirme raporu üretir. `/health` dışındaki tüm uç noktalar `X-Api-Key`
ister.

### ErpIntegration.Api

`IntegrationDb` sahibi. `OrderReadyForErpV1` olayını alır, `IntegrationBatch`
ve dört `IntegrationStep` oluşturur. `IntegrationWorker` adımları kilit altında
sırayla yürütür, her denemeyi `IntegrationAttempt` olarak yazar, geçici/kalıcı
hatayı ayırır ve devre kesici uygular. Elle yeniden deneme uç noktası sunar.
**Şu anda API anahtarı doğrulaması uygulamaz.**

### MockErp.Api

`MockErpDb` sahibi. Gerçek ERP'yi taklit eder: müşteri upsert, sipariş girişi,
stok hareketi ve muhasebe fişi. Tüm yazma uç noktaları `Idempotency-Key`
başlığıyla idempotenttir. `X-Api-Key` ve dakikada 120 istek hız sınırı
uygulanır. **Gerçek Uyumsoft ERP değildir.**

---

## 7. Veritabanı Bağlamı

### OnlineMarketDb — `OnlineMarket.Web`

- **Ana varlıklar:** `ApplicationUser`, `Customer`, `CustomerAddress`,
  `Category`, `Brand`, `Product`, `Stock`, `StockMovement`, `Cart`,
  `CartItem`, `Order`, `OrderItem`, `OrderAddress`, `Payment`,
  `OutboxMessage`.
- **Veri kaynağı:** Kullanıcı etkileşimi ve `scripts/seed/catalog.v1.json`.
- **Transaction sorumluluğu:** Checkout'un tamamı (sipariş, ödeme, stok, outbox)
  tek transaction.
- **Idempotency/benzersizlik:** `OutboxMessage.EventId`, sipariş numarası
  üreteci, satır sürümü (`RowVersion`) ile eşzamanlılık kontrolü.
- **Yetki:** Katalog, stok ve siparişin **doğruluk kaynağıdır** (source of
  truth).
- **Kanıt:** `src/OnlineMarket.Web/Domain/Entities/`,
  `src/OnlineMarket.Web/Infrastructure/Persistence/Configurations/`,
  `src/OnlineMarket.Web/Infrastructure/Persistence/Migrations/`

### RecommendationDb — `Recommendation.Api`

- **Ana varlıklar:** ürün anlık görüntüleri, sipariş anlık görüntüleri ve
  kalemleri, `SubjectId` eşlemesi, popülerlik/FBT/sepet tamamlama çıktıları,
  model ve değerlendirme kayıtları.
- **Veri kaynağı:** Yalnızca Outbox olayları. Market veritabanı okunmaz.
- **Transaction sorumluluğu:** Olay yükleme ve yeniden hesaplama çalışmaları.
- **Idempotency:** Olay kimliğine göre tekilleştirme; `SourceUpdatedAtUtc`
  karşılaştırmasıyla eski olayın yeni anlık görüntüyü ezmesi engellenir.
- **Yetki:** **Anlık görüntüdür (snapshot)**, doğruluk kaynağı değildir.
  Nihai aktiflik/stok kontrolü `OnlineMarket.Web`'e aittir.
- **Kanıt:** `src/Recommendation.Api/Infrastructure/Persistence/Migrations/`

### IntegrationDb — `ErpIntegration.Api`

- **Ana varlıklar:** `IntegrationBatch`, `IntegrationStep`,
  `IntegrationAttempt`, `IntegrationOrderSnapshot`, `IntegrationOrderLine`,
  `ErpCustomerLink`, `ProcessedEvent`.
- **Veri kaynağı:** `OrderReadyForErpV1` olayı.
- **Transaction sorumluluğu:** Batch/adım oluşturma ve her adım sonucu.
- **Idempotency:** `ProcessedEvent` ile olay tekilleştirme; adım bazında
  kararlı `Idempotency-Key`; kanonik yük hash'i (`CanonicalPayloadHasher`) ile
  çakışma tespiti.
- **Yetki:** ERP aktarım **durumunun** doğruluk kaynağıdır; siparişin kendisi
  değildir.
- **Kanıt:** `src/ErpIntegration.Api/Domain/Entities/`,
  `src/ErpIntegration.Api/Application/Services/CanonicalPayloadHasher.cs`

### MockErpDb — `MockErp.Api`

- **Ana varlıklar:** ERP müşterileri, siparişler ve satırları, stok kartları ve
  hareketleri, muhasebe fişi başlık/satırları, idempotency kayıtları.
- **Veri kaynağı:** `ErpIntegration.Api` çağrıları ve
  `--seed-demo-stocks` komutu.
- **Idempotency:** Uç nokta + `Idempotency-Key` benzersizliği; aynı anahtar
  farklı gövdeyle gelirse çakışma.
- **Yetki:** **Simülasyondur**; gerçek muhasebe kaydı değildir.
- **Kanıt:** `src/MockErp.Api/Infrastructure/Persistence/Migrations/`

---

## 8. Checkout ve Outbox Anlatısı (rapora hazır)

`CheckoutService`, SQL Server'ın geçici hata yeniden deneme stratejisiyle
çalışır. EF Core'un `EnableRetryOnFailure` ayarı etkinken elle açılan bir
transaction doğrudan kullanılamaz; bu yüzden servis
`Database.CreateExecutionStrategy()` ile bir strateji alır ve **tüm
transaction'ı stratejinin `ExecuteAsync` bloğu içinde** çalıştırır. Böylece
geçici bir bağlantı hatasında yalnızca tek bir sorgu değil, **transaction'ın
tamamı** baştan denenir ve yarım kalmış bir işlem oluşmaz.

Transaction sınırı içinde şunlar atomik olarak commit edilir: `Order`, sipariş
adres anlık görüntüsü, fiyat ve KDV anlık görüntülü `OrderItem` kayıtları,
başarılı ödeme simülasyonu, atomik stok düşümü, `StockMovement` kayıtları,
sepetin `Converted` durumuna geçmesi ve **iki `OutboxMessage`**:
`OrderConfirmedForRecommendationV1` ile `OrderReadyForErpV1`. Bu sınır içinde
**hiçbir dış HTTP çağrısı yapılmaz** — bu, ERP veya öneri servisinin
yavaşlamasının sipariş alımını kilitlememesini sağlayan temel karardır.

`OutboxBackgroundWorker` düzenli aralıklarla `SqlServerOutboxStore.ClaimAsync`
çağırır. Talep (claim) işlemi, mesajları bir işçi kimliğiyle kilitleyerek
`Processing` durumuna alır; böylece birden fazla örnek aynı mesajı işlemez.
Durumlar: `Pending` → `Processing` → `Processed`, hata hâlinde `Retrying`,
tükendiğinde `FailedPermanent`.

`HttpOutboxDispatcher` hedefe göre yönlendirir:
`ProductSnapshotChangedV1` → `POST /api/v1/events/products`,
`OrderConfirmedForRecommendationV1` → `POST /api/v1/events/orders`
(her ikisi `Recommendation.Api`), `OrderReadyForErpV1` →
`POST /api/v1/integration/orders` (`ErpIntegration.Api`).

Yeniden deneme sonucu geçici (retryable) ise mesaj gecikmeyle tekrar denenir.
**Maksimum deneme sayısı 5'tir** (`SqlServerOutboxStore.MaximumAttempts`);
aşıldığında mesaj `LastErrorCode = "Delivery.RetryExhausted"` ile kalıcı
hataya alınır ve otomatik olarak tekrar denenmez. Hata metinleri maskelenerek
saklanır.

**Elle kurtarma:** Admin panelindeki Outbox ekranı mesajları listeler ve
*Process Outbox Now* işlemi işçiyi elle tetikler
(`src/OnlineMarket.Web/Controllers/AdminController.cs`). Aşağı akış servisi
düzeltildikten sonra kurtarma bu yolla yapılır; veritabanı durumunu doğrudan
SQL ile değiştirmek önerilmez.

**Aşağı akış bağımsızlığı:** Recommendation ve ERP teslimatları ayrı Outbox
satırlarıdır. Biri başarısız olurken diğeri başarıyla işlenebilir; sipariş her
iki durumda da `OnlineMarketDb` içinde eksiksiz durur.

> **Kanıt notu:** Uçtan uca canlı doğrulama iddiası eklenecekse, tarih ve
> gözlemlenen kayıtlar (Outbox satırı, `OrderSnapshot`, `IntegrationBatch`,
> Mock ERP kayıtları) ile birlikte belgelenmelidir. Bölüm 16'daki demo kanıt
> haritasını kullanın.

---

## 9. Öneri Yöntemleri

### 9.1 Popülerlik

- **Girdi:** Yapılandırılan pencere içindeki onaylanmış sipariş anlık
  görüntüleri (`Recommendation:Popularity:WindowDays`, varsayılan 30 gün).
- **Algoritma:** Satılan miktar, farklı sipariş sayısı ve güncellik
  bileşenlerinin ağırlıklı toplamı (varsayılan ağırlıklar 0.50 / 0.30 / 0.20).
- **Çıktı:** Sıralı ürün listesi ve skor.
- **Kullanım:** Ana sayfa, katalog, misafir kullanıcı için AI asistanı, diğer
  yöntemlerin geri dönüşü.
- **Geri dönüş:** Kendisi son geri dönüş katmanıdır.
- **Sınır:** Kişiselleştirilmiş değildir; değerlendirmede en düşük katalog
  kapsamına sahiptir.
- **Kanıt:**
  `src/Recommendation.Api/Application/Services/PopularityRecommendationService.cs`,
  `src/Recommendation.Api/Application/Options/PopularityRecommendationOptions.cs`

### 9.2 Birliktelik / FBT

- **Girdi:** Aynı siparişte birlikte geçen ürün çiftleri.
- **Algoritma:** Birliktelik kuralı metrikleri — support, confidence ve lift.
  Eşikler yapılandırmadan gelir (`MinimumPairOrderCount` 2, `MinimumSupport`
  0.01, `MinimumConfidence` 0.05, `MinimumLift` 1.0).
- **Çıktı:** Kaynak ürüne göre sıralı tamamlayıcı ürünler.
- **Kullanım:** Ürün detay sayfası, AI asistanı FBT niyeti.
- **Geri dönüş:** Eşiği geçen çift yoksa boş döner; uydurma yapılmaz.
- **Sınır:** Seyrek veride sonuç üretmez; çevrimdışı değerlendirmede
  `NotEvaluated`.
- **Kanıt:**
  `src/Recommendation.Api/Application/Services/FrequentlyBoughtTogetherRecommendationService.cs`

### 9.3 Sepet tamamlama

- **Girdi:** Sepetteki ürün kimlikleri.
- **Algoritma:** Yakınlık skoru, confidence, lift ve destekleyen ürün sayısının
  ağırlıklı birleşimi (varsayılan 0.45 / 0.25 / 0.15 / 0.15).
- **Çıktı:** Sepetteki ürünler hariç tutulmuş tamamlayıcı öneriler.
- **Kullanım:** Sepet sayfası, ana sayfa, AI asistanı sepet niyeti.
- **Geri dönüş:** İstemci tarafında (`RecommendationApiClient`) sonuç boşsa
  popülerliğe düşer ve sepetteki ürünleri eler.
- **Sınır:** Boş sepette çalışmaz; giriş yapılmış olmasını gerektirir.
- **Kanıt:**
  `src/Recommendation.Api/Application/Services/CartCompletionRecommendationService.cs`

### 9.4 TF-IDF (içerik tabanlı)

- **Girdi:** Ürün adı, açıklama, kategori ve marka metinleri.
- **Algoritma:** TF-IDF vektörleştirme ve kosinüs benzerliğiyle ürün-ürün
  komşulukları.
- **Çıktı:** Benzer ürün kimlikleri ve benzerlik skorları.
- **Kullanım:** Benzer ürünler bölümü; hibrit sıralamanın içerik bileşeni.
- **Geri dönüş:** ALS yetersizken aktif kalır.
- **Sınır:** Soğuk başlangıçta iyi, davranışsal sinyali yoktur; çevrimdışı
  değerlendirmede `NotEvaluated` (ayrı bir öğe-öğe protokolü gerekir).
- **Kanıt:** `src/Recommendation.ModelService/app/features.py`

### 9.5 Implicit ALS

- **Girdi:** `SubjectId` × ürün etkileşim matrisi (satın alma miktarı ve tekrar
  sayısından türetilmiş örtük ağırlıklar).
- **Algoritma:** `implicit` kütüphanesiyle Alternating Least Squares matris
  ayrıştırması. Varsayılan hiperparametreler: `factors=32`,
  `regularization=0.05`, `iterations=20`, `alpha=20`, `randomSeed=42`.
- **Çıktı:** Subject başına sıralı ürün önerileri.
- **Kullanım:** Kişiselleştirilmiş öneriler; hibrit sıralamanın ALS bileşeni.
- **Geri dönüş:** Minimum subject/ürün/etkileşim eşikleri karşılanmazsa
  `InsufficientData` bildirilir ve diğer bileşenler devreye girer.
- **Sınır:** Soğuk başlangıç problemi; yeni kullanıcı ve yeni ürün için zayıf.
- **Kanıt:** `src/Recommendation.ModelService/app/als.py`

### 9.6 Hibrit sıralama

- **Girdi:** ALS, içerik (TF-IDF), birliktelik ve popülerlik bileşen skorları.
- **Algoritma:** Bileşen skorları normalize edilir ve yapılandırılabilir
  ağırlıklarla birleştirilir. Kişiselleştirilmiş varsayılan ağırlıklar:
  ALS 0.50, içerik 0.20, birliktelik 0.15, popülerlik 0.15. Benzer ürün
  varsayılanları: içerik 0.70, birliktelik 0.20, popülerlik 0.10.
- **Çıktı:** Nihai sıralı liste, bileşen skorları ve gerekçe (reason) meta
  verisi.
- **Kullanım:** `Recommendation:Personalized:ModelStrategy = Hybrid` iken
  (Development varsayılanı).
- **Sınır:** Ağırlıklar sabit yapılandırma girdileridir; mevcut holdout
  üzerinde **ayarlanmamıştır**.
- **Kanıt:** `src/Recommendation.ModelService/app/hybrid.py`,
  `deploy/docker-compose.development.yml` (`RECOMMENDATION_HYBRID_*`)

### 9.7 Kişiselleştirilmiş öneriler (uçtan uca)

`PersonalizedRecommendationService`, `ModelStrategy` ayarına göre Python
servisinden ALS veya hibrit sonuç ister. Servis erişilemezse veya sonuç boşsa
popülerlik geri dönüşü kullanılır. `OnlineMarket.Web` sonucu
`Recommendations:Ui:PersonalizedDisplayLimit` kadar keser ve yalnızca aktif ve
stokta olan ürünleri gösterir.

### 9.8 Skorlar ve gerekçeler

Her öneri öğesi bir skor ve `ReasonCode`/`ReasonText` taşır. **Rapora formül
uydurmayın**: ağırlıkların sayısal değerleri yapılandırma dosyalarından,
bileşenlerin nasıl birleştiği ise `app/hybrid.py` ve ilgili C# servis
dosyalarından alıntılanmalıdır.

---

## 10. Model Eğitimi ve Değerlendirme

**Anlık görüntü verisi.** Eğitim yalnızca `RecommendationDb` içindeki ürün ve
onaylanmış sipariş anlık görüntülerinden beslenir. Market veritabanı okunmaz.

**SubjectId.** `HmacRecommendationSubjectIdDeriver`, market `CustomerId`
değerinden HMAC-SHA256 ile sürümlü ve kararlı bir takma ad üretir. Python
servisi türetme anahtarını ve doğrudan kimliği **hiç görmez**. Mevcut kayıtlar
için `POST /api/v1/recommendations/subjects/backfill` uç noktası vardır.
Sözleşme: `docs/recommendation-subject-id.md`.

**Eğitim akışı.** `POST /api/v1/recommendations/recalculate-models` →
`RecommendationModelOrchestrationService` → Python `POST /api/v1/models/train`.
Eşzamanlı eğitim engellenir. Popülerlik ve FBT için ayrı SQL yeniden hesaplama
uç noktaları vardır (`recalculate`, `recalculate-fbt`); ikinci eşzamanlı çağrı
**409 Conflict** döner.

**Artifact saklama ve sürümleme.** Eğitim çıktısı `/app/artifacts` altına
yazılır (`recommendation_model_artifacts` volume'ü). Her artifact bir
`ModelVersion`, `TrainedAtUtc`, `InputHash` ve bileşen durumları taşır. Servis
yeniden başladığında `load_latest_valid` ile en son geçerli artifact'i yükler.
`GET /api/v1/models/current` yüklü modelin meta verisini döndürür.

**Değerlendirme.** `POST /api/v1/recommendations/evaluate-models` →
Python `POST /api/v1/models/evaluate`. Protokol **kronolojik (temporal)
holdout**'tur: her subject'in son siparişleri test kümesine ayrılır
(`RECOMMENDATION_EVALUATION_HOLDOUT_ORDER_COUNT`, varsayılan 1), önceki
siparişler eğitim kümesinde kalır. Metrikler: Precision@K, Recall@K,
HitRate@K, NDCG@K ve katalog kapsamı; K varsayılanı 5.

**Doğrulanmış sonuçlar** — kaynak:
`src/Recommendation.ModelService/evaluation-reports/evaluation-temporal-v1-20260805123728301-2ac82ad6e0ba4cce8c32a8eda193d668.json`,
`evaluatedAtUtc = 2026-08-05T12:37:28Z`.
Veri kümesi: 502 sipariş, 110 subject, 205 ürün, 1505 etkileşim.

| Model | Precision@5 | Recall@5 | HitRate@5 | NDCG@5 | Katalog kapsamı | Durum |
|---|---|---|---|---|---|---|
| Hybrid | 0.1000 | 0.1585 | 0.4000 | 0.1383 | 0.8537 | Evaluated |
| ALS | 0.0873 | 0.1456 | 0.3727 | 0.1266 | 0.8927 | Evaluated |
| Popularity | 0.0018 | 0.0018 | 0.0091 | 0.0019 | 0.0390 | Evaluated |
| TF-IDF | — | — | — | — | — | **NotEvaluated** |
| FBT | — | — | — | — | — | **NotEvaluated** |

Raporun kendi belirttiği sınırlar (`limitations` alanı):

1. Zamanlama ölçümleri çalışma ortamına bağlıdır ve deterministik
   karşılaştırmadan çıkarılmıştır.
2. Kronolojik holdout, nedensel iş etkisini değil çevrimdışı sıralama
   kalitesini ölçer.
3. TF-IDF Similar ve FBT, modele uygun protokoller olmadan
   değerlendirilmemiştir.
4. Bu değerlendirmede güncellik (recency) ağırlığı uygulanmamıştır.
5. Hibrit ağırlıklar sabit yapılandırma girdileridir ve bu holdout üzerinde
   ayarlanmamıştır.

> **Yazım uyarısı:** Hibrit modelin ALS'ye göre üstünlüğü bu veri kümesinde
> küçüktür (HitRate@5 farkı ≈ +0.027) ve ALS katalog kapsamında biraz
> **daha iyidir**. "Hibrit her açıdan üstündür" demeyin; rakamları farkla
> birlikte verin.

**Sentetik veri üretici servis nihai kapsamda değildir.** Demo verisi
`scripts/seed/catalog.v1.json` ve `scripts/seed/demo_erp_veritabani.xlsx`
dosyalarından gelir; çalışma zamanında veri üreten bir servis yoktur.

---

## 11. Yapay Zekâ Asistanı

**Nihai davranış.** Widget, `AiAssistant:Enabled` true olduğunda mağaza
sayfalarının sağ alt köşesinde görünür. Görünürlük **kimlik doğrulamaya veya
OpenAI anahtarına bağlı değildir**. Admin sayfalarında bilinçli olarak
gösterilmez. Misafir ve giriş yapmış kullanıcı farkı yalnızca hangi öneri uç
noktasının çağrıldığında ortaya çıkar.

**Deterministik niyet yönlendirici.** `AiIntentRouter`, Türkçe mesajı
normalize eder (Türkçe karakterler ve noktalı/noktasız "i" katlanır) ve
öncelik sıralı anahtar kelime kurallarıyla kapalı bir enum'a eşler. LLM
yalnızca hiçbir kural eşleşmediğinde ve sağlayıcı yapılandırılmışsa
sınıflandırmaya yardım edebilir; döndürdüğü değer onaylı enum kümesine karşı
doğrulanır, dışındaki her şey `Unknown` sayılır.

**Niyet → uç nokta eşlemesi:**

| Niyet | Recommendation.Api uç noktası |
|---|---|
| `GeneralRecommendation` (giriş yapmış) | `GET /api/v1/recommendations/customers/{customerId}` |
| `GeneralRecommendation` (misafir) | `GET /api/v1/recommendations/popular` |
| `Popular` | `GET /api/v1/recommendations/popular` |
| `Similar` | `GET /api/v1/recommendations/similar/{productId}` |
| `FrequentlyBoughtTogether` | `GET /api/v1/recommendations/fbt/{productId}` |
| `CartCompletion` | `POST /api/v1/recommendations/cart` |
| Diğer (kargo, ödeme, iade, sipariş, selamlama) | Çağrı yapılmaz |

**Topraklama (grounding).** Ürün listesi `AiRecommendationOrchestrator`
tarafından üretilir: öneri motorundan gelen kimlikler `ICatalogService` ile
yeniden okunur, aktif/stokta/fiyatı geçerli olmayanlar elenir ve sıralama
öneri motorunun döndürdüğü hâliyle korunur. Kartlar (`id`, `name`, `price`,
`imageUrl`, `detailsUrl`, `reason`, `categoryName`, `brandName`) tamamen
sunucu tarafında oluşturulur. **LLM bu listeye ürün ekleyemez, çıkaramaz veya
yeniden sıralayamaz.** Öneri motoru boş dönerse sağlayıcı hiç çağrılmaz ve
deterministik "uygun öneri bulunamadı" metni kullanılır.

**Geri dönüş.** OpenAI anahtarı yoksa, sağlayıcı hata verirse veya boş içerik
dönerse ürün listesi korunur ve deterministik Türkçe açıklama kullanılır;
yanıttaki `usedFallback` alanı bunu belirtir.

**Gizlilik.** Sağlayıcıya yalnızca ürün adı, fiyat, kategori, marka ve öneri
gerekçesi gönderilir. `CustomerId`, `SubjectId`, e-posta, adres, telefon,
sipariş yükü, model skorları ve API anahtarları gönderilmez. Sohbet geçmişi de
sağlayıcıya iletilmez.

**Güvenlik.** Uç nokta antiforgery doğrular, istek gövdesi boyutu sınırlıdır,
mesaj uzunluğu sunucu tarafında sınırlanır, sağlayıcı hata detayları tarayıcıya
sızdırılmaz ve sağlayıcı metni işaretleme karakterlerinden temizlenip düz metin
olarak render edilir. Sepet içeriği ve müşteri kimliği **daima sunucu
tarafında** çözülür; tarayıcıdan gelen ürün kimliği yalnızca katalogdan yeniden
okuma amacıyla kullanılır.

**Operasyonel sınır.** `IRecommendationClient` yalnızca beş okuma işlemi
tanımlar; model yeniden hesaplama, değerlendirme, olay yükleme ve subject
backfill uç noktaları sohbet üzerinden erişilemez.

**Sınırlar.** Niyet yönlendirici anahtar kelime tabanlıdır; kapsam dışı Türkçe
ifadeler `Unknown` niyetine düşer. Belirsiz ürün isteklerinde asistan tahmin
etmek yerine açıklama ister.

> **Geliştirme tarihçesi (yalnızca "Karşılaşılan Problemler" bölümü için
> gerekiyorsa):** Widget bir dönem yalnızca giriş yapmış kullanıcılara
> render edildiği için misafir kullanıcılarda görünmüyordu; düzeltmede
> görünürlük `AiAssistantWidgetPolicy` üzerinden yalnızca
> `AiAssistant:Enabled` ayarına bağlandı. Bunu bir hata günlüğü olarak değil,
> "yapılandırma ile kimlik doğrulamayı karıştırmama" dersi olarak yazın.

**Kanıt:**
`src/OnlineMarket.Web/Controllers/AiSupportController.cs`,
`src/OnlineMarket.Web/Application/Services/AiSupportService.cs`,
`src/OnlineMarket.Web/Application/Services/AiRecommendationOrchestrator.cs`,
`src/OnlineMarket.Web/Application/Services/AiIntentRouter.cs`,
`src/OnlineMarket.Web/Application/Services/AiAssistantWidgetPolicy.cs`,
`tests/OnlineMarket.Web.Tests/AiRecommendationOrchestratorTests.cs`,
`tests/OnlineMarket.Web.Tests/AiSupportServiceTests.cs`,
`tests/OnlineMarket.Web.Tests/AiAssistantWidgetVisibilityTests.cs`

---

## 12. ERP İş Akışı

**`OrderReadyForErpV1`.** Checkout transaction'ında yazılır. Müşteri, teslimat
adresi, toplamlar, kalem anlık görüntüleri ve **hassas olmayan `PaymentMethod`
enum'unu** taşır. Kart numarası, CVV, son kullanma tarihi, ödeme token'ı veya
sağlayıcı kimlik bilgisi **taşımaz**.

**`IntegrationBatch` ve `IntegrationStep`.** Olay alındığında bir batch ve dört
adım oluşturulur. Batch durumları: `Pending`, `InProgress`,
`PartiallySucceeded`, `Succeeded`, `WaitingManualRetry`, `FailedPermanent`.
Adım durumları: `Pending`, `InProgress`, `Retrying`, `Succeeded`,
`FailedPermanent`, `WaitingManualRetry`.

**Dört adım (sıra zorunludur):**

1. `EnsureCustomer` → `POST /api/v1/customers/ensure`
2. `CreateOrder` → `POST /api/v1/orders`
3. `CreateStockMovement` → `POST /api/v1/stock-movements`
4. `CreateAccountingEntry` → `POST /api/v1/accounting-entries`

Muhasebe fişi tek bir satış fişi başlığı ve şu hesap kodlu satırları içerir:
`120` Alıcılar (borç, `GrandTotal`), `600` Yurt İçi Satışlar (alacak,
`Subtotal`), `391` Hesaplanan KDV (alacak, `VatTotal`). Toplam borç toplam
alacağa eşit olmalıdır.

**İşçi davranışı.** `IntegrationWorker` ilk tamamlanmamış adımdan devam eder,
başarılı bir adımı asla yeniden çalıştırmaz, yeniden denemelerde **aynı
idempotency anahtarını** korur, her denemeyi `IntegrationAttempt` olarak yazar
ve geçici/kalıcı hatayı ayırır. Varsayılanlar: yoklama 3 sn, kilit 2 dk, temel
gecikme 5 sn, maksimum gecikme 5 dk, batch boyutu 2, paralellik 2.

**Yeniden deneme ve backoff.** `IntegrationRetryPolicy` üstel gecikme
hesaplar. `MockErpClient` ayrıca kısa "fast retry" uygular
(varsayılan 1 deneme, 100 ms).

**Idempotency ve yük çakışması.** Her adım için kararlı bir `Idempotency-Key`
üretilir. `CanonicalPayloadHasher` gövdenin kanonik hash'ini hesaplar; aynı
anahtar farklı gövdeyle gönderilirse Mock ERP çakışma döndürür ve işlem
mükerrer kayıt oluşturmaz.

**SQL kilitleme.** Adımlar veritabanı kilidiyle talep edilir; süresi dolmuş
kilitler geri alınarak takılı kalmış adımlar kurtarılır.

**Devre kesici.** `MockErpCircuitBreaker` varsayılan olarak 5 ardışık hatadan
sonra 30 saniye devreyi açar ve bu sürede Mock ERP çağrılarını denemez.

**Correlation ID.** Sipariş, olay, batch ve adım kayıtları aynı korelasyon
kimliğini taşır; uçtan uca izleme bu alanla yapılır.

**Elle yeniden deneme.**
`POST /api/v1/integration/orders/{orderId}/retry`.

**Kanıt:**
`src/ErpIntegration.Api/Infrastructure/Workers/IntegrationWorker.cs`,
`src/ErpIntegration.Api/Application/Services/IntegrationStepProcessor.cs`,
`src/ErpIntegration.Api/Application/Services/IntegrationRetryPolicy.cs`,
`src/ErpIntegration.Api/Application/Services/CanonicalPayloadHasher.cs`,
`src/ErpIntegration.Api/Infrastructure/Http/MockErpCircuitBreaker.cs`,
`src/MockErp.Api/Infrastructure/Http/IdempotencyKeyFilter.cs`

---

## 13. Güvenlik

| Risk | Uygulanan kontrol | Kanıt | Kalan sınırlama |
|---|---|---|---|
| Gizli bilgilerin depoya sızması | `appsettings.json` boş yer tutucu; user-secrets / ortam değişkeni; `deploy/.env` yok sayılır | `src/*/appsettings.json`, `deploy/.env.example`, `.gitignore` | Geliştirici makinesinde açığa çıkan anahtar elle rotasyon gerektirir |
| Yetkisiz API erişimi | `X-Api-Key` doğrulaması + fallback yetkilendirme politikası | `src/Recommendation.Api/Program.cs`, `src/MockErp.Api/Infrastructure/Security/ApiKeyAuthentication.cs` | **`ErpIntegration.Api` anahtar doğrulaması yapmaz**; güvenilir iç ağ varsayımı |
| Kullanıcı kimlik doğrulama | ASP.NET Core Identity, çerez tabanlı, rol bazlı admin | `src/OnlineMarket.Web/Program.cs`, `Controllers/AdminController.cs` | Çok faktörlü doğrulama yok |
| CSRF | `ValidateAntiForgeryToken` (formlar ve AI asistan POST'u) | `src/OnlineMarket.Web/Controllers/AiSupportController.cs` | — |
| Beklenmeyen/zararlı JSON | `JsonUnmappedMemberHandling.Disallow`, model doğrulama, standart hata gövdesi | `src/*/Program.cs` | — |
| SQL injection | EF Core parametreli sorgular; ham SQL birleştirmesi yok | `src/*/Infrastructure/Persistence/` | — |
| Mükerrer işlem | Outbox `EventId`, `ProcessedEvent`, `Idempotency-Key`, kanonik yük hash'i | `src/ErpIntegration.Api/Application/Services/CanonicalPayloadHasher.cs` | — |
| Kişisel verinin üçüncü tarafa gitmesi | Python'a yalnızca `SubjectId`; OpenAI'ye yalnızca ürün alanları | `docs/recommendation-model-service.md`, `src/OnlineMarket.Web/Application/Services/AiSupportService.cs` | OpenAI kullanılıyorsa kullanıcının serbest metni sağlayıcıya gider |
| Ödeme verisi | `Payment` varlığı yalnızca yöntem, durum, tutar ve simülasyon referansı tutar; hiçbir kart alanı kalıcı değildir; ERP olayına yalnızca `PaymentMethod` enum'u konur | `src/OnlineMarket.Web/Domain/Entities/Payment.cs`, `src/OnlineMarket.Web/Application/Services/CheckoutService.cs` | Ödeme yalnızca simülasyondur |
| Log sızıntısı | Hata metinleri maskelenir; API anahtarı ve Authorization başlığı loglanmaz | `src/OnlineMarket.Web/Infrastructure/Persistence/SqlServerOutboxStore.cs`, `src/OnlineMarket.Web/Program.cs` | — |
| Servis aşırı yükü | `MockErp.Api` hız sınırı (120 istek/dk); sınırlı zaman aşımları; devre kesiciler | `src/MockErp.Api/Program.cs` | Diğer servislerde hız sınırı yok |
| Savunmasız paket | `dotnet package list --vulnerable`; `TreatWarningsAsErrors` | `Directory.Build.props` | Elle çalıştırılır; CI yoktur |
| Dokümantasyon sızıntısı | .NET API'lerinde Scalar/OpenAPI yalnızca Development | `src/*/Program.cs` | FastAPI `/docs` ortamdan bağımsız açıktır |

---

## 14. Test Stratejisi

**Birim testleri.** Servis ve yardımcı sınıflar sahte (stub/fake) bağımlılıklarla
test edilir: niyet yönlendirici, öneri orkestratörü, seçenek doğrulama,
yeniden deneme politikası, kanonik hash, yük normalleştirme.

**Entegrasyon testleri.** `Testcontainers.MsSql` ile **gerçek bir SQL Server
2022 konteyneri** başlatılır, migration'lar uygulanır ve gerçek veritabanı
davranışı (transaction, benzersiz kısıt, satır kilidi, eşzamanlılık) doğrulanır.
Bu, in-memory sağlayıcının gizlediği hataları yakalar.

**API testleri.** `Microsoft.AspNetCore.Mvc.Testing` ile
`Recommendation.Api`, `ErpIntegration.Api` ve `MockErp.Api` uçtan uca HTTP
seviyesinde test edilir (kimlik doğrulama, doğrulama hataları, idempotency,
sayfalama).

**Sözleşme/doküman testleri.** OpenAPI belgesinin üretilebildiği ve güvenlik
şemasının doğru tanımlandığı doğrulanır.

**Python testleri.** `pytest` ile ALS, TF-IDF özellikleri, hibrit sıralama,
artifact yükleme/kaydetme, yapılandırma doğrulama ve FastAPI uç noktaları
(kimlik doğrulama dâhil) test edilir.

**Uçtan uca doğrulama.** Checkout → Outbox → Recommendation/ERP → Mock ERP
zinciri elle demo senaryosuyla doğrulanır (bkz. bölüm 16).

**Doğrulanmış sonuç — 2026-08-06:**

| Test paketi | Sonuç |
|---|---|
| `tests/OnlineMarket.Web.Tests` | 208/208 başarılı |
| `tests/Recommendation.Api.Tests` | 122/122 başarılı |
| `tests/ErpIntegration.Api.Tests` | 72/72 başarılı |
| `tests/MockErp.Api.Tests` | 67/67 başarılı |
| **.NET toplam** | **469/469 başarılı** |
| `src/Recommendation.ModelService/tests` (pytest) | 70/70 başarılı |

Aynı tarihte: `dotnet build -c Release` → 0 uyarı / 0 hata;
`dotnet package list --vulnerable` → sekiz projenin hiçbirinde savunmasız paket
yok; `git diff --check` → temiz.

> **Bu sayıları eski belgelerden kopyalamayın.** Rapor yazımından hemen önce
> komutları tekrar çalıştırın ve tarihi güncelleyin.

---

## 15. Doğrulanmış Uygulama Kanıtı — Kontrol Listesi

Rapora bir iddia yazmadan önce ilgili yolu açıp doğrulayın:

**Program.cs kayıtları (DI, seçenekler, kimlik doğrulama, doküman):**
- `src/OnlineMarket.Web/Program.cs`
- `src/Recommendation.Api/Program.cs`
- `src/ErpIntegration.Api/Program.cs`
- `src/MockErp.Api/Program.cs`
- `src/Recommendation.ModelService/app/main.py`

**Seçenek (options) sınıfları — yapılandırma adlarının doğruluk kaynağı:**
- `src/OnlineMarket.Web/Application/Options/AiAssistantOptions.cs`
- `src/OnlineMarket.Web/Application/Options/RecommendationUiOptions.cs`
- `src/OnlineMarket.Web/Infrastructure/Http/RecommendationOutboxOptions.cs`
- `src/OnlineMarket.Web/Infrastructure/Http/RecommendationApiClientOptions.cs`
- `src/Recommendation.Api/Application/Options/` (7 dosya)
- `src/ErpIntegration.Api/Infrastructure/Http/MockErpClientOptions.cs`
- `src/ErpIntegration.Api/Application/Models/IntegrationWorkerModels.cs`
- `src/Recommendation.ModelService/app/config.py`

**Controller'lar — gerçek rotaların doğruluk kaynağı:**
- `src/OnlineMarket.Web/Controllers/`
- `src/Recommendation.Api/Controllers/`
- `src/ErpIntegration.Api/Controllers/`
- `src/MockErp.Api/Controllers/`

**Servisler:**
- `src/OnlineMarket.Web/Application/Services/`
- `src/Recommendation.Api/Application/Services/`
- `src/ErpIntegration.Api/Application/Services/`

**Arka plan işçileri:**
- `src/OnlineMarket.Web/Infrastructure/Workers/OutboxBackgroundWorker.cs`
- `src/ErpIntegration.Api/Infrastructure/Workers/IntegrationWorker.cs`

**Migration'lar — şemanın doğruluk kaynağı:**
- `src/OnlineMarket.Web/Infrastructure/Persistence/Migrations/`
- `src/Recommendation.Api/Infrastructure/Persistence/Migrations/`
- `src/ErpIntegration.Api/Infrastructure/Persistence/Migrations/`
- `src/MockErp.Api/Infrastructure/Persistence/Migrations/`

**Testler:** `tests/` (4 proje), `src/Recommendation.ModelService/tests/`

**Docker / yerel geliştirme:**
- `deploy/docker-compose.database.yml`
- `deploy/docker-compose.development.yml`
- `deploy/.env.example`
- `scripts/database/*.ps1`

**Seed dosyaları:**
- `scripts/seed/catalog.v1.json`
- `scripts/seed/demo_erp_veritabani.xlsx`

**Model servisi kodu:** `src/Recommendation.ModelService/app/`

---

## 16. Demo Kanıt Haritası

| Demo adımı | Beklenen görünür sonuç | Veritabanı / API kanıtı |
|---|---|---|
| Kayıt / giriş | Kullanıcı oturumu açılır, "Hesabım" görünür | `OnlineMarketDb`: `AspNetUsers`, `Customers` |
| Katalogda gezinme | Ürün kartları ve görseller yüklenir | `OnlineMarketDb.Products.ImageUrl` = `/uploads/products/...` |
| Ana sayfa önerileri | Popüler ve kişisel öneri bölümleri dolu | `GET /api/v1/recommendations/popular`, `.../customers/{id}` |
| Ürün detayı | "Benzer" ve "Birlikte alınanlar" bölümleri | `GET /api/v1/recommendations/similar/{id}`, `.../fbt/{id}` |
| Sepete ekleme | Sepet rozeti artar | `OnlineMarketDb`: `Carts`, `CartItems` |
| AI asistanı — "En popüler ürünler neler?" | Metin + ürün kartları; `intent = Popular` | Kart kimlikleri `popular` yanıtındaki kimliklerle **birebir aynı** olmalı |
| AI asistanı — "Sepetimi tamamla" | Sepetteki ürünler hariç öneriler | `POST /api/v1/recommendations/cart` gövdesindeki `productIds` sepetle eşleşmeli |
| Checkout tamamlama | Sipariş onay sayfası ve sipariş numarası | `OnlineMarketDb`: `Orders`, `OrderItems`, `Payments`, `StockMovements` |
| Outbox satırı | Admin → Outbox ekranında iki yeni satır | `OutboxMessages`: `OrderConfirmedForRecommendationV1` + `OrderReadyForErpV1`, `Status = Processed` |
| Öneri anlık görüntüsü | — | `RecommendationDb`: yeni `OrderSnapshot` ve kalemleri |
| ERP batch'i | `GET /api/v1/integration/jobs` listede yeni batch | `IntegrationDb`: 1 `IntegrationBatch` + **4** `IntegrationStep` (`Succeeded`) |
| ERP denemeleri | — | `IntegrationDb.IntegrationAttempts`: adım başına en az bir kayıt |
| Mock ERP kayıtları | — | `MockErpDb`: ERP müşterisi, sipariş + satırlar, stok hareketi, muhasebe fişi (borç = alacak) |
| Model yeniden hesaplama | 200 yanıt ve yeni `ModelVersion` | `GET /api/v1/models/current` çıktısındaki `trainedAtUtc` güncellenir |
| Kişisel önerilerin değişmesi | Ana sayfa yenilendiğinde liste güncellenir | `.../customers/{id}` yanıtı değişir |
| Scalar / FastAPI dokümanı | Etkileşimli dokümantasyon açılır | `:5008/scalar/v1`, `:5046/scalar/v1`, `:5140/scalar/v1`, `:8085/docs` |
| Test çıktısı | 469 .NET + 70 Python testi başarılı | `dotnet test`, `pytest` çıktısı |

---

## 17. Anlatmaya Değer Geliştirme Deneyimleri

Bunları kronolojik hata günlüğü olarak değil, **mühendislik dersi** olarak
yazın. Her biri için: karşılaşılan problem → kök neden → uygulanan çözüm →
öğrenilen ders.

1. **Transaction ve yeniden deneme stratejisi çakışması.** EF Core'un
   `EnableRetryOnFailure` ayarı etkinken elle açılan transaction desteklenmez.
   Çözüm: transaction'ın tamamını `CreateExecutionStrategy().ExecuteAsync`
   içine almak. *Ders:* dayanıklılık özellikleri transaction sınırlarıyla
   birlikte tasarlanmalıdır.
   (`src/OnlineMarket.Web/Application/Services/CheckoutService.cs`)

2. **Outbox teslimat hataları ve deneme tükenmesi.** Aşağı akış servisi
   kapalıyken mesajlar 5 denemeden sonra `Delivery.RetryExhausted` ile kalıcı
   hataya düştü. *Ders:* "en az bir kez teslim" garantisi, sonlu deneme
   bütçesi ve **elle kurtarma yolu** olmadan tamamlanmış sayılmaz.

3. **API anahtarı hizalaması.** Üç ayrı `X-Api-Key` çifti (Web↔Recommendation,
   Integration↔MockErp, Recommendation↔ModelService) yanlış eşleştiğinde hata
   401 olarak geç fark edildi. *Ders:* servisler arası kimlik bilgisi
   eşleşmeleri belgelenmeli ve başlangıçta doğrulanmalıdır.

4. **OpenAPI paket sürümü güvenlik düzeltmesi.** `Microsoft.OpenApi` güvenli
   bir sürüme sabitlendi ve derlemede yüksek önem dereceli NuGet güvenlik
   uyarıları hata sayılacak şekilde açıldı. *Ders:* bağımlılık denetimi
   sürekli bir iştir; `TreatWarningsAsErrors` bunu zorunlu kılar.

5. **Ürün görseli URL'lerinin seed/backfill ile atanması.** Katalog seed
   dosyası görsel taşımadığı için ürünler görselsiz kaldı; dosya adı
   eşleştiren bir backfill adımı eklendi. *Ders:* içerik varlıkları
   (asset) ile veri seed'i ayrı yaşam döngülerine sahiptir.

6. **Etkileşimli API dokümantasyonu (Scalar).** Üç .NET API'sine Scalar ve
   OpenAPI eklendi; güvenlik şeması `X-Api-Key` olarak tanımlandı ve
   dokümantasyon yalnızca Development ortamına haritalandı. *Ders:*
   dokümantasyon da bir saldırı yüzeyidir.

7. **AI asistanının topraklanması.** LLM'in ürün uydurmasını engellemek için
   ürün seçimi tamamen öneri motoruna bırakıldı ve kartlar sunucu tarafında
   yeniden inşa edildi. *Ders:* üretken modeller **açıklama katmanı** olarak
   kullanıldığında güvenilir, **karar katmanı** olarak kullanıldığında
   risklidir.

8. **FBT okuma zaman aşımı hizalaması.** FBT çağrısı diğer okumalardan farklı,
   sabit kodlanmış çok kısa bir bütçe kullandığı için yerel ortamda sık sık
   boş dönüyordu. Bütçe yapılandırılabilir hâle getirildi ve çağıranın iptali
   ile dahili zaman aşımı birbirinden ayrıldı. *Ders:* zaman aşımı değerleri
   tek bir yerden yönetilmeli; iptal ile zaman aşımı aynı şey değildir.
   (`src/OnlineMarket.Web/Infrastructure/Http/RecommendationApiClientOptions.cs`)

9. **Docker ve port sahipliği dersi.** Meşgul bir portu yalnızca port
   numarasına bakarak sonlandırmak, Docker Desktop'ın proxy sürecini
   öldürebilir. *Ders:* bir süreci durdurmadan önce adını ve komut satırını
   doğrulayın.

---

## 18. Sınırlamalar ve Gelecek Çalışmalar

### 18.1 Uygulanmış sistemin mevcut sınırları

- `MockErp.Api` bir simülasyondur; gerçek Uyumsoft ERP entegrasyonu yoktur.
- `ErpIntegration.Api` API anahtarı doğrulaması uygulamaz; güvenilir iç ağ
  varsayımına dayanır. Servisler **aynı kimlik doğrulama politikasını
  paylaşmaz**.
- Model yeniden hesaplama ve değerlendirme elle tetiklenir; zamanlayıcı yoktur.
- TF-IDF (Similar) ve FBT için çevrimdışı değerlendirme protokolü yoktur.
- Hibrit ağırlıklar ayarlanmamış sabit yapılandırma değerleridir.
- Popular ve CartCompletion okumaları hâlâ kendi kısa 500 ms bütçelerini
  kullanır; Similar/Personalized/FBT yapılandırılabilir ortak bütçeyi kullanır.
- Öneri kalitesi mevcut sipariş geçmişinin hacmine bağlıdır.
- Yerel geliştirmede dört veritabanı tek SQL Server örneğini paylaşır
  (mantıksal ayrım korunur, fiziksel izolasyon yoktur).
- Seed, Excel içe aktarma ve demo stok komutları yalnızca Development
  ortamında çalışır.
- Görsel eşleştirme mevcut `ImageUrl` değerlerinin üzerine yazar.
- FastAPI dokümantasyonu ortamdan bağımsız olarak açıktır.
- Sürekli entegrasyon (CI) hattı yoktur; doğrulama komutları elle çalıştırılır.
- Ödeme tamamen simülasyondur; gerçek ödeme sağlayıcısı entegrasyonu yoktur.

### 18.2 Gelecek çalışmalar (**henüz uygulanmadı**)

- Gerçek Uyumsoft ERP adaptörünün `IMockErpClient` arayüzü yerine geçmesi.
- Üretim seviyesinde kimlik doğrulama ve ağ sertleştirmesi (özellikle
  `ErpIntegration.Api` için API anahtarı veya mTLS).
- Otomatik model yeniden eğitim zamanlaması ve model sürümü geri alma.
- Gözlemlenebilirlik: metrik, sağlık panosu ve merkezî loglama.
- OpenTelemetry ile dağıtık izleme (correlation ID'nin uçtan uca izlenmesi).
- Azure Key Vault / AWS Secrets Manager gibi bir gizli bilgi yöneticisi.
- Üretim dağıtımı (konteyner orkestrasyonu, CI/CD hattı).
- Daha büyük ve gerçek bir veri kümesiyle yeniden değerlendirme.
- Öneri stratejileri için A/B testi altyapısı.
- Tıklama/satın alma geri bildirim döngüsünün modele beslenmesi.
- TF-IDF ve FBT için modele uygun çevrimdışı değerlendirme protokolleri.

---

## 19. Raporda Kaçınılması Gereken İddialar

Aşağıdaki cümleler **yanlıştır** ve rapora yazılmamalıdır:

1. ❌ "Proje gerçek Uyumsoft ERP ile entegre çalışmaktadır."
   → `MockErp.Api` bir simülasyondur.
2. ❌ "Ürün önerilerini OpenAI/yapay zekâ üretmektedir."
   → Öneriler `Recommendation.Api`'den gelir; OpenAI yalnızca açıklama yazar.
3. ❌ "Tüm servisler tek bir veritabanını paylaşır."
   → Dört ayrı veritabanı ve dört ayrı `DbContext` vardır.
4. ❌ "Checkout, ERP kaydının tamamlanmasını bekler."
   → Checkout transaction'ında hiçbir dış HTTP çağrısı yoktur; akış asenkrondur.
5. ❌ "Model yeniden eğitimi tamamen otomatiktir."
   → Zamanlayıcı yoktur; `recalculate-models` elle çağrılır.
6. ❌ "Müşteri kişisel verileri Python/OpenAI servisine gönderilir."
   → Python yalnızca `SubjectId` görür; OpenAI'ye yalnızca ürün alanları gider.
7. ❌ "Sentetik veri üretim servisi sistemin bir parçasıdır."
   → Nihai çalışma zamanında böyle bir servis yoktur.
8. ❌ "Tüm API'ler aynı kimlik doğrulama politikasını kullanır."
   → `ErpIntegration.Api` anahtar doğrulaması yapmaz; her servisi ayrı yazın.
9. ❌ Kanıtsız performans/başarım iddiaları ("%95 doğruluk", "3 kat hızlı",
   "binlerce eşzamanlı kullanıcı").
   → Yalnızca bölüm 10'daki tarihli değerlendirme raporundan alıntı yapın.
10. ❌ "Sistem üretim ortamında çalışmaktadır."
    → Yalnızca yerel geliştirme ortamı doğrulanmıştır.

---

## 20. Önerilen Nihai Rapor Taslağı

### 1. Kapak
- Proje adı, kurum (Uyumsoft), staj dönemi.
- Grup 16 ve üç ekip üyesinin adı.
- Teslim tarihi ve danışman bilgisi.

### 2. Özet
- Problem, çözüm ve sonucun 150–250 kelimelik özeti.
- Beş öneri türü, ERP simülasyonu ve Outbox'ın tek cümlelik özeti.
- Doğrulanmış test sonucu (tarihiyle).

### 3. Görev Dağılımı
- Her üyenin sorumlu olduğu modüller.
- Ortak çalışılan alanlar.
- Kullanılan iş birliği araçları (Git dalları, PR akışı).
- **Yüzdesel katkı oranı yazmayın** (doğrulanabilir kayıt yok).

### 4. Projenin Amacı ve Kapsamı
- İş problemi: kişiselleştirilmiş öneri + ERP aktarımı.
- Kapsam içi olanlar (bölüm 4 matrisi).
- Kapsam dışı bırakılanlar ve gerekçesi (gerçek ERP, otomatik yeniden eğitim,
  sentetik veri üreteci).
- Hedef kullanıcı: müşteri, mağaza yöneticisi.

### 5. Gereksinim Analizi
- Fonksiyonel gereksinimler (katalog, sepet, checkout, öneri, ERP aktarımı).
- Fonksiyonel olmayan gereksinimler (dayanıklılık, idempotency, güvenlik,
  gizlilik).
- Kısıtlar (gerçek ERP erişimi yok, staj süresi, yerel altyapı).
- Kabul kriterleri ve bunların nasıl doğrulandığı.

### 6. Sistem Mimarisi
- Seçici mikroservis yaklaşımı ve gerekçesi (bölüm 5).
- Beş uygulamanın sorumlulukları (bölüm 6).
- Mermaid bileşen diyagramı (README bölüm 3'ten).
- İletişim kuralları: HTTP/JSON, olaylar, paylaşılmayan veritabanı.
- Tarayıcının yalnızca `OnlineMarket.Web` ile konuşması.

### 7. Veritabanı Tasarımı
- Dört veritabanı ve sahipleri (bölüm 7).
- Ana varlıklar ve ilişkiler; ER diyagramı.
- Cross-database FK/join olmaması kuralı ve gerekçesi.
- Anlık görüntü (snapshot) ile doğruluk kaynağı ayrımı.
- EF Core migration'larının şema doğruluk kaynağı olması.

### 8. Online Market Uygulaması
- Kimlik doğrulama ve yetkilendirme.
- Katalog, arama ve filtreleme.
- Sepet ve checkout akışı; transaction sınırı.
- Sipariş geçmişi ve ERP durum gösterimi.
- Admin paneli ve Outbox izleme.
- Ekran görüntüleri (numaralı şekiller).

### 9. Öneri Sistemi
- Veri girişi: Outbox olayları ve anlık görüntüler.
- `SubjectId` takma adlaştırması.
- Beş öneri türü ve algoritmaları (bölüm 9).
- TF-IDF, ALS ve hibrit sıralama.
- Geri dönüş zinciri ve nihai katalog doğrulaması.
- Değerlendirme protokolü ve tarihli sonuçlar (bölüm 10).

### 10. Yapay Zekâ Destekli Asistan
- Amaç ve kullanıcı deneyimi.
- Deterministik niyet yönlendirici ve niyet→uç nokta eşlemesi.
- Topraklama stratejisi: neden LLM ürün seçmiyor.
- Anahtarsız deterministik geri dönüş.
- Gizlilik ve güvenlik kararları.

### 11. ERP Entegrasyonu ve Mock ERP
- `OrderReadyForErpV1` olayı ve içeriği.
- `IntegrationBatch` + dört `IntegrationStep` durum makinesi.
- Yeniden deneme, kilitleme, idempotency, devre kesici.
- Mock ERP'nin kapsamı ve muhasebe fişi kuralları (120/600/391).
- Gerçek ERP'ye geçiş yolu.

### 12. Güvenlik ve Dayanıklılık
- Güvenlik kontrolleri tablosu (bölüm 13).
- Transactional Outbox'ın dayanıklılık rolü.
- Zaman aşımı, devre kesici ve backoff politikaları.
- Açıkça belirtilen güvenlik sınırı: `ErpIntegration.Api`.

### 13. Test ve Doğrulama
- Test piramidi: birim, entegrasyon, API, uçtan uca.
- Testcontainers ile gerçek SQL Server kullanımı ve gerekçesi.
- Python test kapsamı.
- Doğrulanmış sayılar ve tarih (bölüm 14).
- Güvenlik denetimi sonucu.

### 14. Karşılaşılan Problemler ve Çözümler
- Bölüm 17'deki dokuz konuyu problem→neden→çözüm→ders biçiminde anlatın.
- Hata günlüğü değil, mühendislik kararı anlatısı olsun.

### 15. Sonuç
- Hangi gereksinimlerin karşılandığı.
- Ölçülebilir çıktı: uygulama sayısı, öneri türü sayısı, test sayısı.
- Mevcut sınırların dürüst özeti.

### 16. Gelecek Çalışmalar
- Bölüm 18.2'deki maddeler.
- Her maddeyi **yapılmadı** olarak açıkça işaretleyin.

### 17. Kaynakça
- .NET, EF Core, ASP.NET Core resmî dokümantasyonu.
- `implicit` (ALS) ve scikit-learn (TF-IDF) dokümantasyonu ve makaleleri.
- Transactional Outbox ve idempotency deseni kaynakları.
- OWASP güvenlik referansları.

### 18. Ekler
- Yapılandırma anahtarı tabloları (README bölüm 11).
- API uç nokta listesi veya Scalar ekran görüntüleri.
- Değerlendirme raporu çıktısı.
- Kurulum adımları (README bölüm 12).

---

## 21. Kanıt Gösterme Kuralı

Rapordaki her teknik iddia bir kanıta bağlanmalıdır. Şu basit kuralı kullanın:

| Kanıt türü | Biçim | Örnek |
|---|---|---|
| Kaynak kod | `src/.../Dosya.cs` | `src/OnlineMarket.Web/Application/Services/CheckoutService.cs` |
| Test | `tests/.../TestDosyasi.cs` | `tests/ErpIntegration.Api.Tests/IntegrationWorkerMockErpEndToEndTests.cs` |
| Veritabanı şeması | migration / entity / configuration yolu | `src/Recommendation.Api/Infrastructure/Persistence/Migrations/20260805085430_AddRecommendationSubjectId.cs` |
| Yapılandırma | options sınıfı veya `appsettings.json` yolu | `src/OnlineMarket.Web/Application/Options/AiAssistantOptions.cs` |
| Çalışma zamanı gözlemi | komut + tarih + gözlemlenen durum | `dotnet test .\OnlineMarket.slnx -c Release --no-build` — 2026-08-06 — 469/469 başarılı |
| Değerlendirme metriği | rapor dosyası + `evaluatedAtUtc` | `evaluation-temporal-v1-...json` — 2026-08-05T12:37:28Z |
| Ekran görüntüsü | numaralı şekil + servis/sayfa adı | *Şekil 7 — OnlineMarket.Web, ürün detay sayfası, benzer ürünler bölümü* |

**Kurallar:**

- Yol adlarını **depo köküne göre** yazın; `C:\Users\...` gibi mutlak yollar
  kullanmayın.
- Bir özelliğin var olduğunu kanıtlamak için **eski planlama belgelerini
  kaynak göstermeyin**. `docs/ai/` altındaki dosyalar planlama ve bağlam
  içindir; çalıştırılabilir kaynak kod değildir.
- Kaynaklar çelişirse şu öncelik sırasını uygulayın:
  1. Çalıştırılabilir kaynak kod
  2. Güncel testler
  3. EF Core migration'ları
  4. `appsettings` şablonları ve options sınıfları
  5. Deploy ve script dosyaları
  6. Güncel yetkili dokümanlar
  7. Eski planlama belgeleri
- Çalışma zamanı iddialarında **tarih vermek zorunludur**; sayılar kod
  değiştikçe eskir.
