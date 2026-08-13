# Test verisi — kaynak ve lisans

## MH01_GT.txt — gerçek konum (ground truth)

EuRoC MAV veri kümesi **MH_01_easy** dizisinin Vicon/Leica referans konumu, sol kamera
zaman damgalarına örneklenmiş hâli (3638 poz, 181.8 s).

- Veri kümesi: *The EuRoC micro aerial vehicle datasets*, Burri et al., IJRR 2016 —
  ETH Zürich ASL. Lisans: **CC BY 3.0** (atıfla dağıtılabilir).
- Bu dosyanın alındığı yer: ORB-SLAM3 deposu, `evaluation/Ground_truth/EuRoC_left_cam/MH01_GT.txt`
  (https://github.com/UZ-SLAMLab/ORB_SLAM3).

Biçim: `timestamp_ns,px,py,pz,qw,qx,qy,qz` (virgül ayraçlı, EuRoC kolon sırası).

## MH01_SLAM_sim.txt — SİMÜLE SLAM kestirimi

**Bu dosya gerçek ORB-SLAM3 çıktısı DEĞİLDİR.** `simulate.py` ile MH01 ground truth'undan
üretilmiştir: keyfi referans çerçevesi, monoküler ölçek hatası (0.78), sürüklenme, iki loop
closure ve bir izleme kaybı aralığı içerir. Amacı, ORB-SLAM3 derlenip koşturulana kadar yer
istasyonu hattının uçtan uca çalıştığını göstermektir.

Gerçek çıktı hazır olduğunda bu dosyanın yerine ORB-SLAM3'ün ürettiği
`f_dataset-MH01_*.txt` verilir; biçim aynıdır (TUM: `timestamp tx ty tz qx qy qz qw`).

## Başka diziler

```bash
python fetch_data.py --gt V101 MH02          # küçük GT dosyaları
python fetch_data.py --dataset MH_01_easy    # tam dizi (görüntü + IMU + GT, ~1.4 GB)
```
