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

## Guvenlik Notu

Mapbox access token dosyada acik tutulmamali, ortama gore guvenli sekilde set edilmelidir.

