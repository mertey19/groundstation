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

Rota yüklemesi `applied` olmadan `start_mission` gönderilmez. Bekle/RTL/acil dur, mod değişikliği veya planı temizleme bekleyen başlatma zincirini iptal eder; geç gelen rota onayı görevi yeniden başlatmaz. İptal, araca daha önce ulaşmış bir komutun fiziksel olarak geri alındığını ifade etmez.

## SQLite kaynağı

Windows x64 önbellek eklentisi [resmi SQLite indirmesinden](https://www.sqlite.org/download.html) alınan 3.53.4 DLL ile yenilendi. ZIP SHA3-256: `deddee963c810d1eeac3ce5e15c7c41da21a1c54d7a39cf54fbf577d2f50de3a`. Yerel yükleme ve Unity SQLite sürücüsüyle disk yazma, okuma, kapatıp yeniden açma test edildi. Eski DLL ve metadata proje dışındaki düzeltme yedeğinde saklandı.
