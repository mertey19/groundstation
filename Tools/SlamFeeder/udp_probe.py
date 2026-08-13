"""Yer istasyonu yerine gecen UDP dinleyici — hat testi ve kanit uretimi icin.

Unity acik degilken besleyicinin dogru mesaj urettigini dogrulamak icin:

    python udp_probe.py                  # 19090'i dinler, GT/SLAM sapmasini yazar
    python udp_probe.py --port 19091     # Unity'nin gonderdigi ACK'leri dinler

Cikti, yer istasyonundaki HUD ile ayni buyuklukleri (anlik sapma, ortalama, RMSE)
bagimsiz olarak hesaplar; iki taraf tutuyorsa hat dogrudur.
"""

from __future__ import annotations

import argparse
import json
import math
import socket
import sys

from trajectory import haversine_m

REQUIRED_POSE_FIELDS = ("latitude", "longitude", "altitudeM", "yawDeg")


def validate(msg: dict) -> list:
    """Yer istasyonunun (DigitalTwinJsonPoseBridge) bekledigi alanlari denetler."""
    problems = []
    if msg.get("schemaVersion") not in (None, "", "1.0"):
        problems.append(f"schemaVersion beklenmiyor: {msg.get('schemaVersion')}")
    if not msg.get("vehicleType"):
        problems.append("vehicleType bos (yer istasyonu 'uav' varsayar)")
    for block in ("pose", "slamPose"):
        if block not in msg:
            continue
        for field in REQUIRED_POSE_FIELDS:
            if field not in msg[block]:
                problems.append(f"{block}.{field} eksik")
        lat, lon = msg[block].get("latitude"), msg[block].get("longitude")
        if lat is not None and not (-90.0 <= lat <= 90.0):
            problems.append(f"{block}.latitude aralik disi: {lat}")
        if lon is not None and not (-180.0 <= lon <= 180.0):
            problems.append(f"{block}.longitude aralik disi: {lon}")
    if "pose" not in msg and "slamPose" not in msg:
        problems.append("ne pose ne slamPose var")
    return problems


def main(argv=None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--host", default="0.0.0.0")
    ap.add_argument("--port", type=int, default=19090)
    ap.add_argument("--every", type=int, default=10, help="Kac mesajda bir satir yazilsin")
    ap.add_argument("--quiet", action="store_true", help="Sadece ozet")
    ap.add_argument("--max-messages", type=int, default=0, help="Bu kadar mesaj sonra ozet yazip cik (0 = sinirsiz)")
    ap.add_argument("--timeout", type=float, default=0.0, help="Bu kadar saniye sessizlikten sonra cik (0 = bekle)")
    args = ap.parse_args(argv)

    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        sock.bind((args.host, args.port))
    except OSError as exc:
        print(f"[dinleyici] Port {args.port} acilamadi: {exc}\n"
              "            Unity calisiyorsa portu o tutuyordur (bu normaldir).", file=sys.stderr)
        return 1
    sock.settimeout(1.0)
    print(f"[dinleyici] udp://{args.host}:{args.port} dinleniyor. Ctrl+C ile cikis.")

    n = n_slam = n_bad = 0
    dev_sum = dev_sq = dev_max = 0.0
    idle = 0.0
    try:
        while True:
            try:
                data, _ = sock.recvfrom(65535)
                idle = 0.0
            except socket.timeout:
                idle += 1.0
                if args.timeout > 0.0 and n > 0 and idle >= args.timeout:
                    print(f"[dinleyici] {args.timeout:.0f} sn sessizlik, cikiliyor.")
                    break
                continue
            n += 1
            try:
                msg = json.loads(data.decode("utf-8"))
            except (UnicodeDecodeError, json.JSONDecodeError) as exc:
                n_bad += 1
                print(f"[dinleyici] JSON hatasi: {exc}")
                continue
            problems = validate(msg)
            if problems:
                n_bad += 1
                print(f"[dinleyici] #{n} sema uyarisi: {'; '.join(problems)}")

            pose, slam = msg.get("pose"), msg.get("slamPose")
            dev = None
            if pose and slam:
                dev = haversine_m(pose["latitude"], pose["longitude"], slam["latitude"], slam["longitude"])
                n_slam += 1
                dev_sum += dev
                dev_sq += dev * dev
                dev_max = max(dev_max, dev)
            if not args.quiet and n % max(1, args.every) == 0:
                line = f"#{n:5d} seq={msg.get('sequenceId')}"
                if pose:
                    line += f"  GT {pose['latitude']:.7f},{pose['longitude']:.7f}"
                if slam:
                    line += f"  SLAM sapma {dev:6.2f} m  guven {slam.get('confidence', -1):.2f}"
                else:
                    line += "  SLAM pozu yok"
                print(line)
            if args.max_messages and n >= args.max_messages:
                break
    except KeyboardInterrupt:
        pass
    finally:
        sock.close()

    rmse = math.sqrt(dev_sq / n_slam) if n_slam else 0.0
    print(
        f"\n[dinleyici] Ozet: {n} mesaj, {n_slam} tanesi GT+SLAM cifti, {n_bad} sorunlu.\n"
        f"            Sapma: ort {dev_sum / n_slam if n_slam else 0:.3f} m · "
        f"maks {dev_max:.3f} m · RMSE {rmse:.3f} m"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
