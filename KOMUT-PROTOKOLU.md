# Yer istasyonu komut ve onay sözleşmesi

Yer istasyonu canlı JSON/UDP telemetrisini 19090 portundan alır. Komutlar yalnızca son beş saniye içinde doğrulanmış, seçili araç/kaynak eşleşmesinin IP adresine ve yapılandırılan komut portuna (varsayılan 19092) gider. Rover paketleri İHA hedefini değiştirmez. Hatalı paketlerin gönderici adresleri sadece kendi NACK yanıtlarında kullanılır.

Yer istasyonu kodu uçuş kontrolcüsüne doğrudan MAVLink bağlantısı açmaz. Araç bilgisayarındaki alıcı bu sözleşmeyi kendi uçuş kontrolcüsüne uyarlamalı ve aşağıdaki onayları göndermelidir. Bu depoda araç yazılımı veya bağlı uçuş kontrolcüsü bulunmadığından gerçek donanımda komut uygulaması doğrulanmamıştır. Yerel UDP testi yalnızca bu deponun gönderim, adresleme ve onay takibini doğrular.

## Komut

```json
{
  "schemaVersion": "1.0",
  "type": "command",
  "commandId": "her-komutta-benzersiz-kimlik",
  "command": "hold",
  "vehicleType": "uav",
  "targetSourceId": "aracin-telemetri-kaynak-kimligi",
  "timestampMs": 0,
  "authToken": "ortak-yapilandirilan-token",
  "value": 0,
  "altitudeReference": "relative_home",
  "route": null
}
```

Örnekteki `timestampMs: 0` yer tutucudur; gerçek paket güncel Unix milisaniye zamanını taşır.

| Komut | İçerik |
| --- | --- |
| `upload_route` | `route.waypoints`: `index`, `latitude`, `longitude`, `altitudeM`, `speedMps`, `holdSeconds`, `action` |
| `start_mission` | Önceden yüklenmiş görevi başlatma isteği |
| `hold` | Görevi bekletme / araçta konum tutma isteği |
| `set_speed` | `value`, m/s (1–50) |
| `set_altitude` | `value`, metre (1–500), ev konumuna göre irtifa |
| `rtl` | Eve dönme isteği |
| `emergency_stop` | Araç yazılımının tanımladığı acil durdurma işlemi; alıcı desteklemiyorsa reddetmeli |

Canlı telemetride `altitudeM`, irtifa düğmeleri kullanılacaksa aynı `relative_home` referansını kullanmalıdır. Araç alıcısı kendi izinlerini, uçuş modunu, fiziksel sınırlarını ve komut desteğini kontrol eder; desteklemediği komutu sessizce kabul etmez. Rota yüklemede kamera eylemi ve bekleme/hız alanları da alıcı tarafından karşılanmalı veya açıkça reddedilmelidir.

## Araç onayı

Onay, yer istasyonunun **19090** dinleme portuna gönderilir; komutun geçici gönderici portuna yanıt vermek yeterli değildir.

```json
{
  "schemaVersion": "1.0",
  "type": "command_ack",
  "commandId": "komuttaki-ayni-kimlik",
  "vehicleType": "uav",
  "sourceId": "aracin-telemetri-kaynak-kimligi",
  "timestampMs": 0,
  "authToken": "ortak-yapilandirilan-token",
  "status": "applied",
  "message": ""
}
```

`accepted` yalnızca alındığını; `applied` uçuş kontrolcüsünde uygulandığını; `rejected` reddedildiğini ifade eder. Yer istasyonu IP, araç, kaynak, token, komut kimliği ve onay zamanını denetler. On saniye içinde uygulama sonucu gelmezse sonuç **bilinmiyor** gösterilir. Gönderim en çok üç kez, aynı kimlik ve aynı içerikle denenir; `accepted` geldikten sonra yeniden gönderim durur. Alıcı `commandId` için tekrarları ayıklamalı, aynı komutu yeniden uygulamadan önceki sonucu döndürmelidir.

Rota yüklemesi `applied` olmadan `start_mission` gönderilmez. Görev karşılaştırması araç/kaynak kimliği, irtifa referansı, waypoint sırası/sayısı, koordinat, irtifa, hız, bekleme ve action alanlarını kapsar; `commandId` ve yeniden gönderim zamanı görev değişikliği sayılmaz. Yerel görev bu anlık görüntüden saparsa — kullanıcı eski değerlere dönse bile — geç gelen `applied` onayı `start_mission` göndermez. `accepted` tek başına başlatmaz. Bekle/RTL/acil dur, timeout, mod değişikliği, kaynak/oturum değişimi veya planı temizleme bekleyen başlatma zincirini iptal eder; geç gelen rota onayı görevi yeniden başlatmaz. İptal, araca daha önce ulaşmış bir komutun fiziksel olarak geri alındığını ifade etmez.

Onay zaman damgası komutun kendi zamanına ve yer istasyonunun `unscaledTime` gecikmesine göre yorumlanır. Komuttan çok eski bir onay yok sayılır; sistem saati sıçraması tek başına zamanında gelen onayı düşürmez.

## Kayıt dayanıklılığı

`DigitalTwinOperationRecorder` kabul edilen mesajları JSONL dosyasına artımlı yazar. Bellekte en fazla 500 satır tutulur; asıl kayıt disktir. `StopRecording` yazma kuyruğunu boşaltır ve dosyayı kapatır. `SaveRecording` RAM önizlemesini tam oturum diye yazmaz; aktif kaydı finalize eder veya mevcut oturum yolunu döndürür. Dosya boyutu sınırında oturum sıralı `*_pNNN.jsonl` parçalarına ayrılır ve `*.session.json` bildirimi tek oturum olarak yeniden okunur. Yaklaşık bir saniyelik flush aralığı vardır. Güç kaybında son flush edilmemiş satır kaybolabilir; önceki tam satırlar okunabilir kalır. `authToken` diske yazılmadan çıkarılır. Oynatma canlı kimlik doğrulamasını gevşetmez; ayrı replay bağlamı kullanır.

## Yerel araç emülatörü

`Tools/VehicleEmulator/vehicle_emulator.py` yalnızca 127.0.0.1 üzerinde dinler. Simüle kaynak kimliği ve `SIM-EMU` kipi gerçek araç gibi gizlenmez. Telemetri üretir, komut sözleşmesini ayrıştırır, `accepted`/`applied`/`rejected` döner, aynı `commandId` tekrarlarını ayıklar ve farklı gövdeyi reddeder. Desteklenmeyen, boş rota, geçersiz hız veya geçersiz zaman damgası `applied` üretmez. Daha önce `rejected` olan kimlik sahte `applied` dönmez. `[]` ve bozuk paket dinleyiciyi düşürmez. Emülatör testi uçuş kontrolcüsü veya donanım entegrasyonu değildir.

Tekrar önbelleği en fazla 4096 komut kimliği tutar; evict FIFO'dur. `applied` yalnızca emülatörün simüle durum değişikliği tamamlandıktan sonra üretilir.

Birim test:

```text
python Tools/VehicleEmulator/test_vehicle_emulator.py
```

Düzenli simüle telemetri (loopback; gerçek araç değildir):

```text
python Tools/VehicleEmulator/vehicle_emulator.py --host 127.0.0.1 --command-port 19092 --telemetry-port 19090 --telemetry-hz 2 --vehicle uav --source sim-emu-uav --token test-token
```

## SQLite kaynağı

Windows x64 önbellek eklentisi [resmi SQLite indirmesinden](https://www.sqlite.org/download.html) alınan 3.53.4 DLL ile yenilendi. ZIP SHA3-256: `deddee963c810d1eeac3ce5e15c7c41da21a1c54d7a39cf54fbf577d2f50de3a`. Yerel yükleme ve Unity SQLite sürücüsüyle disk yazma, okuma, kapatıp yeniden açma test edildi. Eski DLL ve metadata proje dışındaki düzeltme yedeğinde saklandı.
