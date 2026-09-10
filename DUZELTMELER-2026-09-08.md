# Yer istasyonu düzeltmeleri — 8 Eylül 2026

İncelemede yeniden üretilen dokuz sorun giderildi. Yer istasyonunun canlı komut gönderimi ve araç onayı takibi tamamlandı; SQLite Windows x64 yükleme hatası da düzeltildi.

| Bulgu | Düzeltilen davranış |
| --- | --- |
| Hatalı paket komut hedefini değiştiriyordu | Hedef, doğrulanmış araç/kaynak ve IP eşleşmesinden seçilir; İHA ve Rover ayrı tutulur. Bağlantı beş saniye sonra eski sayılır. |
| Rover telemetrisi İHA bataryasını eziyordu | Her araç ve kaynak için ayrı telemetri, batarya, mesh ve güncellik durumu tutulur. Uçuş güvenliği paneli İHA durumunu gösterir. |
| Ana HUD yerel modeli canlı veri gibi gösteriyordu | Canlı yükseklik, hız, mod ve waypoint gerçek mesajdan okunur. Veri kesilince değerler bilinmiyor olarak gösterilir. |
| Legacy poz kimlik doğrulamayı atlıyordu | Canlı girişte V1 şeması ve token zorunlu; eski düz poz girişleri kapalı. Örnek oynatıcılar ayrı yerel API kullanır. |
| Eski ilk paket yeni telemetri sayılıyordu | Mutlak paket yaşı, gelecek zaman, sıra ve içerik kontrol edilir. Eksik/geçersiz koordinatlar ve sayısal değerler reddedilir. |
| Survey rotası içbükey alanın dışına çıkıyordu | Tarama çizgileri polygon ile kesilir; hatlar alan içindeki bağlantılarla birleştirilir. Bütün rota parçaları denetlenir. Geçersiz alan mevcut planı bozmaz. |
| Boş replace eski rotayı bırakıyordu | Boş replace rotayı temizler; boş append mevcut rotayı korur. Hatalı rota geçerli sıra numarasını tüketmez. |
| Kayıt reddedilirken başarılı gösteriliyordu | Oynatma ayrı sıra ve veri durumunda, özgün zaman aralıklarıyla yürür. Kabul/red sayıları gösterilir. Çıkışta canlı veri ve önceki plan geri gelir. |
| JSON Export dosya oluşturmuyordu | Düğme zaman damgalı JSON dosyası oluşturur; adı durum satırında gösterilir, tıklanınca klasörü açılır. |

Canlı moddaki Başlat önce rotayı yükler, araç `applied` onayı verince görevi başlatır. Dur, hız, irtifa, RTL ve acil dur komutlarının gönderim ve uygulama durumları ayrı izlenir. Yanlış araçtan, kaynaktan veya IP’den gelen, tokeni yanlış ve eski onaylar kabul edilmez. Onay gelmezse “araç sonucu bilinmiyor” gösterilir. Bekle ve mod değişikliği sonrasında geç gelen rota onayı görevi başlatmaz.

Gerçek sahnedeki düğmelerin hem Inspector hem kod üzerinden iki kez bağlandığı da bulundu ve giderildi. Tek JSON Export tıklaması artık tek dosya üretir. Mod ve işlem satırı kontrol düğmelerinin üstüne hizalandı; düğmelerle çakışmadığı çalışan sahnede kontrol edildi.

SQLite DLL’si Windows x64 için resmi 3.53.4 dağıtımıyla değiştirildi; indirme özeti doğrulandı. Unity sürücüsüyle dosyaya yazma, okuma ve bağlantıyı yeniden açma kontrolleri geçti. Kaynak ve dosya özeti [komut ve altyapı notunda](KOMUT-PROTOKOLU.md) kayıtlıdır.

## Kahverengi harita blokları

Sorun çalışan Unity sahnesinde yeniden üretildi: iki parçanın uydu dokusu yüklenmiş görünürken zemin geometrisi sıfır köşeden oluşuyordu. Yükseklik indirmesi başarısız olduğunda `ElevatedTerrainStrategy.ResetToFlatMesh` oluşturduğu geometrinin dönüş değerini kullanmıyordu. Önceki görsel yükleme testleri bu durumu yakalamıyordu.

Yükseklik verisi beklenirken artık uydu dokusunun çizilebildiği düz bir zemin hazırlanıyor. İndirme başarısız olsa da bu zemin korunuyor; veri geldiğinde gerçek yükseklikler uygulanıyor. Hata durumu başarılı indirme olarak işaretlenmiyor. Yeniden kullanılan parçanın eski yükseklik verisi temizleniyor ve tekrar kullanımda aynı önbellek anahtarının eklenmesi hatası giderildi.

Görüntü sürekliliği kontrolü artık yalnızca indirme bayrağına bakmıyor: istenen parçanın varlığını, çizilebilir geometrisini, etkin renderer'ını ve bağlı dokusunu da denetliyor. Eksik bir kare son eksiksiz görüntünün üzerine yazılmıyor.

Yeni testler ilk yükseklik indirmesinin başarısız olmasını, bekleyen veriyi, üç yeniden kullanım döngüsünü ve render edilen dört renk bölgesini kapsıyor. Uydu, sokak, 3D harita, dijital ikizden dönüş ve yakınlaştırma ayrıca Unity ekranında kontrol edildi. Son görüntü: `Logs/map-raster-loading-preview.png`; hata öncesi tanı: `Logs/map-terrain-before.txt`; piksel testi: `Logs/map-terrain-fallback-render.png`.

Bu düzeltmenin kod yedeği: `C:/Users/mert/Downloads/groundstation-map-terrain-backup-20260908`.

## Doğrulama

- Yer istasyonu: **87** kontrol.
- Mevcut HUD: **14** kontrol.
- Çalışan sahnede kontrol satırı ve gerçek dışa aktarma düğmesi: **5** kontrol.
- Harita yükleme / kahverengi boşluklar: **38** kontrol (15 yeni kontrol).
- Dijital ikiz: **51** kontrol.
- Python SLAM besleyici: **16** test.

Toplam **211** kontrol. Komut testleri izole nesneler ve bilgisayarın kendi UDP test alıcısıyla yapıldı. Gerçek bir araca uçuş komutu gönderilmedi. Araç bilgisayarındaki JSON → uçuş kontrolcüsü uyarlaması ve donanımda komut uygulaması bu depoda doğrulanamaz; gerekli sözleşme [KOMUT-PROTOKOLU.md](KOMUT-PROTOKOLU.md) içindedir.

Test çıktıları: `Logs/groundstation-regression-checks.txt`, `Logs/hud-regression.txt`, `Logs/groundstation-ui-checks.txt`, `Logs/map-raster-loading-checks.txt`, `Logs/digital-twin-checks.txt`, `Logs/groundstation-full-validation.txt`.

Önceki kod ve bozuk DLL yedeği: `C:/Users/mert/Downloads/groundstation-defects-backup-20260908`.

