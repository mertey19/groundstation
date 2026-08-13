"""Test verisi indirici.

    python fetch_data.py --gt MH01 V101        # kucuk ground truth dosyalari (KB'lar)
    python fetch_data.py --dataset MH_01_easy  # tam EuRoC dizisi (~1.4 GB, ORB-SLAM3 icin)

Ground truth dosyalari ORB-SLAM3 deposunun ``evaluation/Ground_truth`` klasorunden
gelir (EuRoC sol kamera zaman damgalarina orneklenmis, TUM/EuRoC bicimi). Tam dizi
ise ASL sunucusundan iner ve icinde ORB-SLAM3'un ihtiyac duydugu her sey vardir:
``cam0/`` + ``cam1/`` goruntuleri, ``imu0/data.csv``, ``state_groundtruth_estimate0/data.csv``.
"""

from __future__ import annotations

import argparse
import os
import sys
import urllib.request

GT_BASE = "https://raw.githubusercontent.com/UZ-SLAMLab/ORB_SLAM3/master/evaluation/Ground_truth/EuRoC_left_cam"
GT_SEQUENCES = ["MH01", "MH02", "MH03", "MH04", "MH05", "V101", "V102", "V103", "V201", "V202", "V203"]
ASL_BASE = "http://robotics.ethz.ch/~asl-datasets/ijrr_euroc_mav_dataset"
DATASET_PATHS = {
    "MH_01_easy": "machine_hall/MH_01_easy/MH_01_easy.zip",
    "MH_02_easy": "machine_hall/MH_02_easy/MH_02_easy.zip",
    "MH_03_medium": "machine_hall/MH_03_medium/MH_03_medium.zip",
    "V1_01_easy": "vicon_room1/V1_01_easy/V1_01_easy.zip",
    "V1_02_medium": "vicon_room1/V1_02_medium/V1_02_medium.zip",
    "V2_01_easy": "vicon_room2/V2_01_easy/V2_01_easy.zip",
}


def _progress(block_num, block_size, total_size):
    if total_size <= 0:
        return
    done = min(100.0, 100.0 * block_num * block_size / total_size)
    mb = block_num * block_size / 1e6
    sys.stdout.write(f"\r    {done:5.1f}%  ({mb:.1f} / {total_size / 1e6:.1f} MB)")
    sys.stdout.flush()


def download(url: str, dest: str) -> bool:
    os.makedirs(os.path.dirname(os.path.abspath(dest)), exist_ok=True)
    print(f"  -> {url}")
    try:
        urllib.request.urlretrieve(url, dest, _progress)
        print(f"\n     kaydedildi: {dest} ({os.path.getsize(dest) / 1e6:.2f} MB)")
        return True
    except Exception as exc:  # ag hatasi / 404
        print(f"\n     HATA: {exc}")
        return False


def main(argv=None) -> int:
    here = os.path.dirname(os.path.abspath(__file__))
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--gt", nargs="*", metavar="DIZI", help=f"Indirilecek GT dizileri: {', '.join(GT_SEQUENCES)}")
    ap.add_argument("--dataset", nargs="*", metavar="DIZI", help=f"Tam EuRoC zip: {', '.join(DATASET_PATHS)}")
    ap.add_argument("--out", default=os.path.join(here, "data"), help="Hedef klasor")
    args = ap.parse_args(argv)

    if not args.gt and not args.dataset:
        ap.print_help()
        return 0

    ok = True
    for name in args.gt or []:
        key = name.upper().replace("_", "")
        if key not in GT_SEQUENCES:
            print(f"[indirici] Bilinmeyen GT dizisi: {name} (secenekler: {', '.join(GT_SEQUENCES)})")
            ok = False
            continue
        print(f"[indirici] Ground truth: {key}")
        ok &= download(f"{GT_BASE}/{key}_GT.txt", os.path.join(args.out, f"{key}_GT.txt"))

    for name in args.dataset or []:
        if name not in DATASET_PATHS:
            print(f"[indirici] Bilinmeyen dizi: {name} (secenekler: {', '.join(DATASET_PATHS)})")
            ok = False
            continue
        print(f"[indirici] Tam dizi: {name}  (buyuk dosya, birkac dakika surebilir)")
        ok &= download(f"{ASL_BASE}/{DATASET_PATHS[name]}", os.path.join(args.out, f"{name}.zip"))

    return 0 if ok else 1


if __name__ == "__main__":
    raise SystemExit(main())
