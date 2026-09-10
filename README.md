# GroundStationRoute

Unity tabanli bir yer istasyonu ve Digital Twin projesidir.  
Mapbox harita, rota planlama, survey (tarama) gorevi, drone/rover telemetri izleme ve JSON tabanli canli twin senkronizasyonu icerir.

## Ozellikler

- Canlı çevre haritası için ayrı TCP alıcı, renkli 3B nokta bulutu görünümü ve PLY dışa aktarımı. RealSense/RTAB-Map ROS 2 bağlantı dosyaları: [Canlı dijital ikiz rehberi](CANLI-DIJITAL-IKIZ.md). Yerel aktarım/gösterim test edildi; gerçek Jetson/kamera/uçuş doğrulaması tamamlanmadı.

- Mapbox ile harita goruntuleme (uydu/sokak/3D modlar)
- Waypoint tabanli rota olusturma ve gorsellestirme
- Survey mapping paneli (polygon secimi, overlap, transect vb.)
- Drone hiz/irtifa kontrol panelleri
- Digital Twin (2D + 3D) gorunum
- UAV + Rover icin JSON mesaj isleme
- Mission engine:
  - Faz takibi (scan / joint_operation / dynamic_replan / complete)
  - Obstacle/target/voxel delta guncelleme
  - Mesh durum/trend takibi
  - Adaptif akis modu (Hybrid / TwinOnly / EmergencyTwinOnly)
- UDP ingress + ACK/NACK
- Operation kayit ve replay

## Proje Yapisi

- `Assets/Scripts/DigitalTwin/`
  - `DigitalTwinJsonPoseBridge.cs`: JSON ingest, validation, apply, ack
  - `DigitalTwinMissionEngine.cs`: faz, olay, delta isleme, replan
  - `DigitalTwinUdpIngress.cs`: UDP dinleyici ve ACK gonderimi
  - `DigitalTwinAdaptiveFlowController.cs`: adaptif throughput kontrolu
  - `DigitalTwinOperationRecorder.cs`: log kayit/replay
  - `DigitalTwinRoverAdapter.cs`: rover poz senkronizasyonu
  - `DigitalTwinPresenter.cs`: twin panel telemetri ve UI baglantilari

## Hizli Baslangic

1. Projeyi Unity ile ac.
2. Sahnedeki temel objeleri dogrula:
   - `Map` (`AbstractMap`)
   - `DigitalTwinJsonPoseBridge`
   - `DigitalTwinBridge`
   - `DigitalTwinRover`
3. `DigitalTwinBridge` uzerinde su componentlerin oldugunu kontrol et:
   - `DigitalTwinRemoteState`
   - `DigitalTwinMissionEngine`
   - `DigitalTwinUdpIngress`
   - `DigitalTwinAdaptiveFlowController`
   - `DigitalTwinOperationRecorder`
   - `DigitalTwinAutoBootstrap`
4. Gerekirse `DigitalTwinAutoBootstrap` uzerinden `Ensure Digital Twin Setup` calistir.
5. Play moduna gec ve UDP mesajlarini gonder.

## UDP Test

Varsayilan portlar:

- Ingress: `19090`
- ACK: `19091`

Canlı UDP girişinde kimlik doğrulaması zorunludur. Göndericideki token, köprüdeki `expectedAuthToken` ve komut kanalındaki `authToken` ile eşleşmelidir. Varsayılan geliştirme değeri:

```json
"authToken": "simurgh-2026"
```

Mesajlar `schemaVersion: "1.0"`, `sourceId`, `vehicleType`, pozitif `sequenceId` ve güncel Unix milisaniye `timestampMs` içermelidir. Beş saniyeden eski paketler ve kimliksiz eski poz biçimi reddedilir. İHA/Rover ve farklı kaynakların sayaçları, batarya ve telemetri durumları ayrıdır.

## Kontrol, kayıt ve dışa aktarma

Kontrol düğmelerinin üstündeki mod satırı simülasyon ile canlı İHA kontrolünü ayırır. İlk doğrulanmış canlı İHA verisi otomatik olarak canlı gösterime geçer; açıkça seçilen simülasyon modu korunur. Canlı modda veri kesildiğinde modelden uydurma telemetri gösterilmez. Bağlantı olmadan seçilen canlı modda komutlar gönderilmez.

- **Başlat:** coğrafi rotayı araca yükler; araç `applied` onayı verince görev başlatma komutu gönderilir.
- **Dur:** canlı modda `hold` komutu gönderir ve bekleyen görev başlatmasını iptal eder.
- **Hız / irtifa:** canlı telemetriyi temel alarak onay takibi olan komut gönderir.
- **JSON Export:** zaman damgalı JSON dosyasını `Application.persistentDataPath` içine yazar. Kaydedilen ad durum satırında görünür; satıra tıklamak klasörü açar.
- **Kayıt:** `DigitalTwinOperationRecorder` bileşeninin kayıt/kaydet/oynat bağlam menüleri kullanılır. Oynatma özgün zaman aralıklarını korur; uygulanan ve reddedilen mesajları sayar. `KAYITTAN ÇIK` ile canlı duruma ve oynatma öncesi rotaya dönülür. Kayıt oynatırken araç komutu gönderilmez.
- **Polygon survey:** tüm rota parçaları seçili alanın içinde kalır. İçbükey boşluklar içeriden dolaşılır; polygon modunda sınır dışına turnaround uzatması uygulanmaz. Kendini kesen alan mevcut rotayı değiştirmez.

Araç tarafı komut/ACK sözleşmesi ve donanım doğrulama sınırı: [KOMUT-PROTOKOLU.md](KOMUT-PROTOKOLU.md).

## Düzeltme doğrulaması

`Tools > Simurgh > Yer Istasyonu Regresyon Testleri` menüsü Play kapalıyken çalışır. Testler izole nesneleri ve yalnızca yerel UDP test alıcısını kullanır; gerçek araca komut göndermez. Açık editörde bütün kontrolleri çalıştırmak için `Temp/groundstation-test-request.txt` dosyasına `all` yazılabilir. Sonuçlar `Logs/groundstation-regression-checks.txt`, `Logs/map-raster-loading-checks.txt`, `Logs/digital-twin-checks.txt` ve `Logs/groundstation-full-validation.txt` dosyalarındadır.

## Gercek Konum (Ground Truth) ve SLAM Izi

Arac ilerledikce GERCEK konum mavi, SLAM'in tahmin ettigi konum turuncu cizgi olarak
haritada birikerek cizilir; butona gerek yoktur, her poz mesajinda izler uzar. Anlik sapma,
ortalama/maksimum sapma, RMSE ve SLAM guven degeri sol HUD panelinde gosterilir
(`DigitalTwinTrajectoryComparison`), `CSV Kaydet` ile disa aktarilir.

Iki kullanim yolu:

1. **Harici besleyici (ORB-SLAM3 hatti).** `Tools/SlamFeeder/` altindaki Python araci EuRoC
   ground truth ile ORB-SLAM3 cikti yorungesini okur, zaman damgalarina gore eslestirir,
   Sim(3)/SE(3) ile hizalar, ATE/RMSE raporlar ve UDP 19090'a `pose` (gercek) + `slamPose`
   (kestirim) olarak gonderir:

   ```bash
   cd Tools/SlamFeeder
   python slam_feeder.py --gt data/MH01_GT.txt --slam f_dataset-MH01_monoi.txt --align se3
   ```

   ORB-SLAM3 ciktisi henuz yoksa `--simulate-slam` ile ayni bicimde gercekci bir kestirim
   uretilir; gercek cikti gelince yalnizca `--slam` yolu degisir. Ayrinti: `Tools/SlamFeeder/README.md`.

2. **Yer istasyonu ici oynatici (harici surec gerekmez).** Sahneye
   `DigitalTwinTrajectoryDatasetPlayer` ekleyin (menu: `Tools > Simurgh > ORB-SLAM3 Veri Kumesi
   Oynaticisi Ekle`). `Assets/StreamingAssets/SlamDataset/` altindaki gercek konum ve SLAM
   dosyalarini okur, hizalar ve Play'de dogrudan kopruye besler.

Veri kumeleri oda olcegindedir (~30 m) ve sapma tipik olarak desimetre altidir. Bu yuzden
izlerin kalinligi METRE cinsinden verilir ve harita olcegine gore dunya birimine cevrilir
(`lineWidthMeters`, varsayilan 0.6 m); SLAM izi daha ince ve ustte cizilir, boylece izler
ust uste bindiginde gercek konum kaybolmaz. Cok kucuk sapmalari ayirt etmek icin kalinligi
dusurun (or. 0.15 m) veya besleyicideki `--scale` buyutmesini kullanin (bu durumda sapma
degerleri de ayni oranda buyur).

### Duman testi (editorsuz dogrulama)

`Tools > Simurgh > Yorunge Duman Testi (Play + Rapor)` sahneyi acar, oynaticiyi kurar, Play
moduna gecer ve izlerin GERCEKTEN cizildigini olcup rapor + kanit karesi yazar (vertex sayisi,
iz uzunlugu, dikey sacilma, sapma/RMSE, harita durumu, konsol hatalari). Komut satirindan:

```bash
Unity.exe -projectPath . -executeMethod TrajectorySmokeTest.Run -simurghSmokeQuit -logFile smoke.log
```

Cikti klasoru `SIMURGH_SMOKE_DIR` ortam degiskeniyle, yoksa proje kokundeki `SmokeTest/`.

## Guvenlik Notu

Mapbox access token dosyada acik tutulmamali, ortama gore guvenli sekilde set edilmelidir.


## Sade arayüz

Ana görünümde telemetri, uçuş güvenliği ve rota kontrolleri bulunur. Sol taraftaki
**Ayrıntılar** alanından Yörünge, Ağ, Kamera, Hedefler, Fotoğraflar veya Demo açılır.
Aynı sekmeye tekrar tıklamak veya Escape tuşu ayrıntıyı kapatır. Bir seferde yalnızca
bir ayrıntı paneli açılır; paneller kapalıyken veri toplama ve yörünge çizimi sürer.
Görev özeti aynı alandaki düğmeden tekrar açılabilir.

**WP Ekle** açıldığında waypoint irtifa girişi görünür. Arayüz üzerinde tıklama,
sürükleme ve kaydırma haritada nokta eklemez veya kamerayı kaydırmaz. Hız ve irtifa
kontrolleri sağ üstte iki satır halinde yer alır.

Telemetri gelmeden veya güncelliğini yitirdiğinde güvenlik paneli normal durum
bildirmez. Saha kontrolü sahnedeki başlangıç modellerini değil, alınan güncel araç
koordinatlarını kullanır. RTL/acil dur düğmeleri her kesintisiz basışta bir kez komut
üretir; yeniden göndermek için düğmenin bırakılması gerekir.

Doğrulama: Unity menüsünde `Tools > Simurgh > HUD Regresyon Kontrolu`.
Sonuç `Logs/hud-regression.txt` dosyasına yazılır. Kontrol gerçek araca komut göndermez.


### Dijital ikiz saha görünümü

Dijital ikiz tek bir tam ekran ortofoto görünümü kullanır. Eski beyaz Image kutuları, üst üste telemetri/yazılar ve harita kontrolleri bu görünümde gizlenir; haritaya dönünce önceki kontroller geri gelir. Kamera araç çubuğu: sahaya dön, üstten bak, İHA’ya odaklan, yakınlaştır/uzaklaştır. Sol sürükleme döndürür, sağ sürükleme zeminde kaydırır; panel üzerindeki hareketler kamerayı etkilemez.

Ortofoto doğal renklerle çizilir. Bina taslakları ve ağaç modelleri isteğe bağlıdır. Bu veri paketinde DSM yoktur; JSON nesne yükseklikleri doğrulanmış yüzey ölçümü değildir. Araç işaretleri zemine izdüşürülür; telemetri yüksekliği ayrıca gösterilir. 3B arazi yüksekliği elde etmek için gerçek DSM/mesh ve tanımlı irtifa referansı gerekir.

Konum dönüşümü `source.ortho_meta.crs` ve `bounds` üzerinden WGS84 → UTM ile yapılır. Bu paketin gerçek raster merkezi 41.30422962584914, -81.75230772357038’dir; `georef.origin_lat/lon` içindeki farklı yaklaşık konum kullanılmaz. Saha dışındaki araçlar yanlış yerde çizilmez. Beş saniyeden eski pozlar gizlenir; test/kayıt kaynakları ayrı etiketlenir. İHA ve rover bağımsız takip edilir; İHA’nın saha içindeki izi gösterilir.

Doğrulama: Play modunda **Tools > Simurgh > Digital Twin Kontrolu (Play)**. Koordinat referansları bağımsız PROJ çıktılarıyla karşılaştırılır; aç/kapa döngüleri, kamera/çözünürlük, eski grafiklerin gizlenmesi, katmanlar, araç pozları, tekrar mesajları ve zaman aşımı test edilir. Bu kontrol yalnızca yerel test pozları kullanır; araç komutu göndermez. Sonuç `Logs/digital-twin-checks.txt`, görünüm `Logs/digital-twin-preview.png` dosyasına yazılır. Testten sonra Play’i durdurmak geçici test durumunu temizler.


### Harita görüntüsü yükleme koruması

`MapForceLoadFix`, harita kamerasına `MapImageContinuity` ekler. Eksik görüntüler yüklenirken son eksiksiz kamera görüntüsü korunur ve kısa bir durum mesajı gösterilir. İlk açılışta hazır görüntü yoksa koyu bir yükleme zemini kullanılır. Görüntü beklerken haritaya konum ekleme kapalıdır; kamera ve görünüm kontrolleri kullanılabilir.

Mapbox `ImageDataFetcher`, `MapImageFactory` ve `UnityTile` dosyalarında yerel düzeltmeler vardır: doğrulanmış görüntüler için oturum başına en fazla 128 parça / 32 MiB önbellek, aynı bölgenin üst ölçek görüntüsünden geçici detay, eski istekleri iptal etme ve 15 saniyelik zaman aşımıyla en fazla dört deneme (1/2/4 saniye aralıklar). İnternet yoksa önceden yüklenmemiş bölgeler üretilemez; dört deneme de başarısız olursa bağlantı geri geldikten sonra Uydu/Sokak görünümünü yeniden seçmek yeni bir yükleme başlatır. SDK güncellemesinde bu düzeltmeleri koruyun.

Play modunda `Tools > Simurgh > Harita Yukleme Kontrolu (Play)` kesinti, gecikme, yanlış/eski yanıt, önbellek, parça yeniden kullanımı ve ekranda boşluk oluşmaması kontrollerini çalıştırır. Sonuç: `Logs/map-raster-loading-checks.txt`.
