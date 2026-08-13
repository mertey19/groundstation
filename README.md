# GroundStationRoute

Unity tabanli bir yer istasyonu ve Digital Twin projesidir.  
Mapbox harita, rota planlama, survey (tarama) gorevi, drone/rover telemetri izleme ve JSON tabanli canli twin senkronizasyonu icerir.

## Ozellikler

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

JSON tarafinda (opsiyonel) token kontrolu aciksa su alan gonderilmelidir:

```json
"authToken": "simurgh-2026"
```

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

