# SlamFeeder — Ground truth + ORB-SLAM3 yörüngesini yer istasyonuna besler

Amaç: **gerçek konum** (ground truth) ile **SLAM'in tahmin ettiği konum** aynı anda yer
istasyonuna gönderilsin, haritada **farklı renklerde iki çizgi** olarak birikerek çizilsin.
Butona gerek yok — her poz mesajında izler kendiliğinden uzar.

```
EuRoC verisi (görüntü + IMU + ground truth)
        │
        ├── ORB-SLAM3  ──►  f_dataset-MH01_mono.txt   (SLAM kestirimi, TUM biçimi)
        │                              │
        └── MH01_GT.txt (gerçek konum) ┘
                        │
                  slam_feeder.py   ← zaman eşleştirme + Sim(3) hizalama + ATE/RMSE
                        │            + yerel metre → lat/lon
                        │  UDP 19090  {"pose": GT, "slamPose": SLAM+confidence}
                        ▼
        DigitalTwinUdpIngress → DigitalTwinJsonPoseBridge
                        │  OnUavGpsPose / OnUavSlamPose
                        ▼
        DigitalTwinTrajectoryComparison → mavi iz (gerçek) + turuncu iz (SLAM) + sapma HUD
```

## Hızlı başlangıç

Yer istasyonunu Unity'de Play moduna alın (sahnede `DigitalTwinUdpIngress` 19090'ı dinler), sonra:

```bash
python slam_feeder.py --gt data/MH01_GT.txt --simulate-slam
```

Unity açık değilken hattı test etmek için ikinci bir terminalde:

```bash
python udp_probe.py --every 20
```

Testler:

```bash
python test_slam_feeder.py
```

## Veri

`data/MH01_GT.txt` depoda hazır: EuRoC MAV **MH_01_easy** dizisinin gerçek konumu
(Vicon/Leica referansı, sol kamera zaman damgalarına örneklenmiş, 3638 poz / 181.8 s).
Kaynak: ORB-SLAM3 deposunun `evaluation/Ground_truth/EuRoC_left_cam` klasörü.

Başka diziler ve ORB-SLAM3'ü çalıştırmak için gereken tam veri:

```bash
python fetch_data.py --gt V101 MH02          # küçük GT dosyaları
python fetch_data.py --dataset MH_01_easy    # tam dizi (~1.4 GB: cam0, cam1, imu0, GT)
```

## Gerçek ORB-SLAM3 çıktısıyla kullanım

ORB-SLAM3 Linux'ta derlenir (Pangolin, OpenCV, Eigen3, DBoW2/g2o). Bu makinede WSL dağıtımı
ve Docker kurulu olmadığı için burada **çalıştırılmadı**; `--simulate-slam` ile üretilen
kestirim, gerçek çıktının yerini tutan aynı biçimde (TUM) bir dosyadır. Gerçek çıktı hazır
olduğunda tek değişiklik `--slam` yolunu vermektir; yer istasyonu tarafında hiçbir şey değişmez.

```bash
# 1) Derleme (Ubuntu / WSL2 / Docker)
git clone https://github.com/UZ-SLAMLab/ORB_SLAM3.git
cd ORB_SLAM3 && ./build.sh

# 2) MH01 üzerinde koşturma (monoküler-inertial örneği)
./Examples/Monocular-Inertial/mono_inertial_euroc \
    Vocabulary/ORBvoc.txt Examples/Monocular-Inertial/EuRoC.yaml \
    /veri/MH_01_easy Examples/Monocular-Inertial/EuRoC_TimeStamps/MH01.txt dataset-MH01_monoi
# çıktı: f_dataset-MH01_monoi.txt   (timestamp tx ty tz qx qy qz qw)

# 3) Yer istasyonuna besle
python slam_feeder.py --gt data/MH01_GT.txt --slam f_dataset-MH01_monoi.txt --align se3
```

`--align` seçimi: **mono** çıktısı ölçek belirsizdir → `sim3`; **stereo / mono-inertial /
stereo-inertial** çıktısında ölçek gözlemlenebilir → `se3` (ölçeği zorlamadan hizalar,
böylece ölçek hatası da sapmaya yansır ve saklanmaz).

## Seçenekler

| Seçenek | Varsayılan | Açıklama |
|---|---|---|
| `--gt` | `data/MH01_GT.txt` | Gerçek konum dosyası (EuRoC virgüllü veya TUM boşluklu) |
| `--slam` | — | ORB-SLAM3 çıktısı; verilmezse `--simulate-slam` devreye girer |
| `--align` | `sim3` | `sim3` (mono) / `se3` (stereo-IMU) / `none` |
| `--host` `--port` | `127.0.0.1` `19090` | `DigitalTwinUdpIngress` adresi |
| `--speed` | `1.0` | Oynatma hızı çarpanı (veri kümesi zamanlaması korunur) |
| `--rate` | `20` | Saniyedeki en fazla mesaj |
| `--loop` | kapalı | Bitince baştan başla |
| `--anchor` | `39.8719,32.7302` | Yerel çerçevenin oturtulacağı enlem,boylam |
| `--scale` | `1.0` | Yörüngeyi büyütme çarpanı — **sapma değerleri de aynı oranda büyür** |
| `--altitude-offset` | `30` | İrtifaya eklenen sabit (m) |
| `--auth-token` | `simurgh-2026` | Köprüde token doğrulaması açıksa gerekir |
| `--save-slam` | — | Simüle yörüngeyi TUM olarak kaydet |
| `--save-geo-csv` | — | Gönderilen lat/lon çiftlerini CSV'ye yaz (sunum kanıtı) |
| `--dry-run` | kapalı | UDP göndermeden yalnız hizalama/ATE raporu |

## Ölçek ve görünürlük

EuRoC dizileri oda/hangar ölçeğindedir (~30 m) ve gerçek ile SLAM arasındaki fark tipik olarak
**santimetre–desimetre** düzeyindedir. Haritada iki çizgiyi ayrı görmek için:

* yer istasyonunda **zoom 20+** kullanın (izler ayrışır, sayılar gerçek kalır) — tercih edilen yol;
* ya da `--scale 10` gibi bir büyütme verin. Bu durumda HUD'daki sapma da 10 katına çıkar,
  yani hakem sunumunda **gerçek metrik değildir**; besleyici bu durumda uyarı yazar.

## Doğrulanan davranış

`python test_slam_feeder.py` — 16 test:

* EuRoC (virgüllü, `qw` önce) ve TUM (boşluklu, `qw` sonra) kolon sıraları, ns→s dönüşümü;
* Umeyama Sim(3)'ün bilinen bir dönüşümü geri kazanması (numpy'siz 2B yedek dâhil);
* zaman eşleştirme toleransı; ENU↔lat/lon gidiş-dönüşü ve haversine ile tutarlılık;
* simüle SLAM'in hizalama **öncesi** büyük, **sonrası** sürüklenme düzeyinde hata vermesi;
* mesajların yer istasyonu şemasına uyması, izleme kaybında `slamPose` bloğunun
  **hiç gönderilmemesi** (aksi halde SLAM izi (0,0)'a sıçrardı);
* uçtan uca: gerçek soket üzerinden gönderilen mesajların sıralı ve geçerli olması.

Gerçek MH01 verisiyle uçtan uca ölçüm (besleyici ↔ bağımsız dinleyici):
`RMSE 0.190 m · ort 0.163 m · maks 0.483 m`, 283 mesaj, 0 şema hatası.
