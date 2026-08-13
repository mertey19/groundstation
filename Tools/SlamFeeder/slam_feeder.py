"""ORB-SLAM3 / ground truth yorunge besleyicisi -> Simurgh yer istasyonu (UDP JSON).

Ne yapar
--------
1. Ground truth yorungesini (EuRoC/TUM) ve SLAM kestirim yorungesini okur.
2. Zaman damgalarina gore eslestirir, SLAM'i GT'ye Sim(3)/SE(3) ile hizalar (ORB-SLAM3
   monoculer ciktisi keyfi cerceve ve olcektedir), ATE/RMSE raporlar.
3. Yerel ENU metrelerini secilen cografi capa etrafinda lat/lon'a cevirir.
4. Her ornegi ``schemaVersion 1.0`` mesaji olarak UDP ile yer istasyonuna gonderir:
   ``pose`` = gercek konum (GT),  ``slamPose`` = SLAM kestirimi (+confidence).

Yer istasyonu tarafinda ``DigitalTwinTrajectoryComparison`` bu iki olayi dinler ve
GT izini mavi, SLAM izini turuncu cizer; sapma / RMSE HUD'da guncellenir. Buton yok,
her poz mesajinda cizgi kendiliginden uzar.

Ornekler
--------
    # Gercek EuRoC GT + simule SLAM (ORB-SLAM3 henuz derlenmediyse)
    python slam_feeder.py --gt data/MH01_GT.txt --simulate-slam

    # Gercek ORB-SLAM3 ciktisi ile (mono: olcek serbest)
    python slam_feeder.py --gt data/MH01_GT.txt --slam f_dataset-MH01_mono.txt --align sim3

    # Stereo/IMU ciktisi (olcek gozlemlenebilir) + 4x hizli oynatma
    python slam_feeder.py --gt data/MH01_GT.txt --slam f_MH01_stereo_inertial.txt \
        --align se3 --speed 4
"""

from __future__ import annotations

import argparse
import json
import math
import socket
import sys
import time
from typing import List, Optional, Sequence, Tuple

from trajectory import (
    Pose,
    apply_sim3,
    associate,
    ate_stats,
    enu_to_geo,
    heading_from_motion,
    load_trajectory,
    save_tum,
    umeyama,
    yaw_from_quaternion,
)

SCHEMA_VERSION = "1.0"
DEFAULT_ANCHOR = (39.8719, 32.7302)  # yer istasyonunun varsayilan harita merkezi


# --------------------------------------------------------------------- yardimci


def _axis_map(spec: str):
    """"xyz" gibi bir eslemeyi (dogu, kuzey, yukari) secicisine cevirir.

    EuRoC dunya cercevesi z-yukari oldugu icin varsayilan "xyz" dogrudur; farkli
    veri kumeleri icin or. "xzy" veya "-yxz" verilebilir.
    """
    spec = spec.strip().lower()
    tokens: List[Tuple[int, float]] = []
    i = 0
    while i < len(spec) and len(tokens) < 3:
        sign = 1.0
        if spec[i] in "+-":
            sign = -1.0 if spec[i] == "-" else 1.0
            i += 1
        if i >= len(spec) or spec[i] not in "xyz":
            raise argparse.ArgumentTypeError(f"Gecersiz eksen eslemesi: {spec}")
        tokens.append(("xyz".index(spec[i]), sign))
        i += 1
    if len(tokens) != 3:
        raise argparse.ArgumentTypeError("Eksen eslemesi 3 bilesen icermeli, or. xyz veya -yxz")
    return tokens


def _to_enu(p: Pose, axes, origin: Pose, scale: float) -> Tuple[float, float, float]:
    local = (p.x - origin.x, p.y - origin.y, p.z - origin.z)
    return tuple(local[idx] * sign * scale for idx, sign in axes)  # type: ignore[return-value]


def _parse_anchor(text: str) -> Tuple[float, float]:
    parts = [p for p in text.replace(";", ",").split(",") if p.strip()]
    if len(parts) != 2:
        raise argparse.ArgumentTypeError("Capa 'enlem,boylam' seklinde olmali")
    return float(parts[0]), float(parts[1])


class ConfidenceEstimator:
    """SLAM guven degerini YALNIZ SLAM akisinin ic tutarliligindan turetir.

    Gercek ORB-SLAM3 kullanilirken bunun yerine izlenen harita noktasi sayisi /
    tracking state ("OK", "LOST") kullanilmalidir; burada GT'ye bakilmaz, cunku
    guven degeri gercek ucusta bilinmeyen bir referansa dayanamaz.
    """

    def __init__(self) -> None:
        self._prev: Optional[Tuple[float, float, float, float]] = None  # t, e, n, u
        self._prev_speed: Optional[float] = None
        self._conf = 0.9

    def update(self, t: float, e: float, n: float, u: float) -> float:
        if self._prev is None:
            self._prev = (t, e, n, u)
            return self._conf
        dt = max(1e-3, t - self._prev[0])
        dist = math.dist((e, n, u), self._prev[1:])
        speed = dist / dt
        # Bosluk (izleme kaybi) -> yeniden yerellesme, guven duser.
        if dt > 0.3:
            self._conf = min(self._conf, 0.35)
        # Ani hiz sicramasi -> kotu eslesme isareti. Esik, 20 Hz'lik bir akista olcum
        # gurultusunun urettigi tipik jerk'in (~7 m/s^2) cok uzerinde secildi; aksi halde
        # guven surekli baskilanir ve HUD her karede "DUSUK" gosterir.
        if self._prev_speed is not None:
            jerk = abs(speed - self._prev_speed) / dt
            if jerk > 25.0:
                self._conf = min(self._conf, 0.6)
        # Ustel toparlanma: ~3 s icinde 0.95'e yaklasir.
        self._conf += (0.95 - self._conf) * min(1.0, dt / 3.0)
        self._prev = (t, e, n, u)
        self._prev_speed = speed
        return max(0.05, min(0.99, self._conf))


# ------------------------------------------------------------------ mesaj kurma


def build_message(
    seq: int,
    gt_geo: Tuple[float, float, float],
    gt_yaw: float,
    slam_geo: Optional[Tuple[float, float, float]],
    slam_yaw: Optional[float],
    confidence: Optional[float],
    speed_mps: float,
    source_id: str,
    auth_token: str,
    vehicle_type: str,
    mission_phase: str,
    battery_percent: float,
    dataset_time: Optional[float] = None,
) -> dict:
    ts_ms = int(dataset_time * 1000) if dataset_time else int(time.time() * 1000)
    msg = {
        "schemaVersion": SCHEMA_VERSION,
        "sequenceId": seq,
        "timestampMs": ts_ms,
        "sourceId": source_id,
        "authToken": auth_token,
        "vehicleType": vehicle_type,
        "missionPhase": mission_phase,
        "pose": {
            "latitude": round(gt_geo[0], 9),
            "longitude": round(gt_geo[1], 9),
            "altitudeM": round(gt_geo[2], 3),
            "yawDeg": round(gt_yaw, 2),
            "pitchDeg": 0.0,
            "rollDeg": 0.0,
        },
        "telemetry": {
            "altitudeM": round(gt_geo[2], 2),
            "speedMps": round(speed_mps, 2),
            "mode": "DATASET",
            "waypointIndex": 0,
            "hopCount": 1,
            "signalDbm": -57.0,
            "snrDb": 21.0,
            "latencyMs": 38.0,
            "packetLossPercent": 0.4,
            "batteryPercent": round(battery_percent, 1),
            "batteryVoltage": round(22.2 + 2.4 * battery_percent / 100.0, 2),
        },
        "ackRequested": False,
    }
    if slam_geo is not None:
        msg["slamPose"] = {
            "latitude": round(slam_geo[0], 9),
            "longitude": round(slam_geo[1], 9),
            "altitudeM": round(slam_geo[2], 3),
            "yawDeg": round(slam_yaw if slam_yaw is not None else gt_yaw, 2),
            "pitchDeg": 0.0,
            "rollDeg": 0.0,
            "confidence": round(confidence if confidence is not None else 0.0, 3),
        }
    return msg


# ------------------------------------------------------------------------ akis


def prepare_frames(args) -> Tuple[List[dict], dict]:
    """Yorungeleri okur, hizalar ve gonderime hazir cerceve listesi uretir."""
    gt = load_trajectory(args.gt, args.gt_format)
    print(f"[besleyici] GT     : {args.gt}  ({len(gt)} poz, {gt[-1].t - gt[0].t:.1f} s)")

    if args.slam:
        slam_raw = load_trajectory(args.slam, args.slam_format)
        slam_label = args.slam
    else:
        from simulate import simulate_vislam  # sadece gerektiginde yuklenir

        slam_raw = simulate_vislam(gt, seed=args.seed, scale=args.sim_scale)
        slam_label = f"SIMULE (seed={args.seed}, olcek={args.sim_scale})"
        if args.save_slam:
            save_tum(args.save_slam, slam_raw)
            print(f"[besleyici] Simule SLAM yorungesi yazildi: {args.save_slam}")
    print(f"[besleyici] SLAM   : {slam_label}  ({len(slam_raw)} poz)")

    pairs = associate(gt, slam_raw, args.max_dt)
    if len(pairs) < 3:
        raise SystemExit(
            "[besleyici] HATA: GT ile SLAM zaman damgalari eslesmiyor "
            f"({len(pairs)} eslesme). --max-dt degerini buyutun veya dosya bicimlerini kontrol edin."
        )

    transform = None
    slam = list(slam_raw)
    if args.align != "none":
        src = [(slam_raw[ei].x, slam_raw[ei].y, slam_raw[ei].z) for _, ei in pairs]
        dst = [(gt[gi].x, gt[gi].y, gt[gi].z) for gi, _ in pairs]
        transform = umeyama(src, dst, with_scale=(args.align == "sim3"))
        slam = [apply_sim3(p, transform) for p in slam_raw]
        print(
            f"[besleyici] Hizalama: {args.align} ({transform.method}), "
            f"olcek s={transform.s:.4f}, eslesen ornek={len(pairs)}"
        )
    stats = ate_stats(gt, slam, pairs)
    print(
        "[besleyici] ATE    : RMSE {rmse:.3f} m · ort {mean:.3f} m · "
        "medyan {median:.3f} m · maks {max:.3f} m".format(**stats)
    )

    # GT saati referans; her GT ornegine (varsa) hizalanmis SLAM ornegi eslenir.
    slam_by_gt = {gi: ei for gi, ei in pairs}
    axes = _axis_map(args.axes)
    origin = gt[0]
    lat0, lon0 = args.anchor

    conf = ConfidenceEstimator()
    frames: List[dict] = []
    prev_gt_en: Optional[Tuple[float, float]] = None
    prev_slam_en: Optional[Tuple[float, float]] = None
    gt_yaw = 0.0
    slam_yaw = 0.0
    for gi, g in enumerate(gt):
        ge, gn, gu = _to_enu(g, axes, origin, args.scale)
        if prev_gt_en is not None:
            h = heading_from_motion(prev_gt_en[0], prev_gt_en[1], ge, gn)
            gt_yaw = h if h is not None else gt_yaw
        else:
            gt_yaw = yaw_from_quaternion(g.qx, g.qy, g.qz, g.qw)
        gt_lat, gt_lon = enu_to_geo(lat0, lon0, ge, gn)

        speed = 0.0
        if prev_gt_en is not None and gi > 0:
            dt = max(1e-3, g.t - gt[gi - 1].t)
            speed = math.dist((ge, gn), prev_gt_en) / dt / max(args.scale, 1e-6)

        slam_entry = None
        ei = slam_by_gt.get(gi)
        if ei is not None:
            s = slam[ei]
            se, sn, su = _to_enu(s, axes, origin, args.scale)
            if prev_slam_en is not None:
                h = heading_from_motion(prev_slam_en[0], prev_slam_en[1], se, sn)
                slam_yaw = h if h is not None else slam_yaw
            else:
                slam_yaw = gt_yaw
            slam_lat, slam_lon = enu_to_geo(lat0, lon0, se, sn)
            slam_entry = {
                "geo": (slam_lat, slam_lon, su + args.altitude_offset),
                "yaw": slam_yaw,
                "confidence": conf.update(s.t, se, sn, su),
            }
            prev_slam_en = (se, sn)

        frames.append(
            {
                "t": g.t,
                "gt_geo": (gt_lat, gt_lon, gu + args.altitude_offset),
                "gt_yaw": gt_yaw,
                "speed": speed,
                "slam": slam_entry,
            }
        )
        prev_gt_en = (ge, gn)

    meta = {
        "gt_count": len(gt),
        "slam_count": len(slam),
        "pairs": len(pairs),
        "ate": stats,
        "align": None if transform is None else {"scale": transform.s, "method": transform.method},
        "anchor": (lat0, lon0),
        "duration_s": gt[-1].t - gt[0].t,
    }
    return frames, meta


def stream(frames: Sequence[dict], args) -> None:
    sock = None
    if not args.dry_run:
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    target = (args.host, args.port)
    seq = int(time.time()) % 1_000_000
    sent = 0
    skipped_slam = 0
    min_period = 1.0 / args.rate if args.rate > 0 else 0.0

    csv_rows: List[str] = []
    loops = 0
    try:
        while True:
            t0_wall = time.perf_counter()
            t0_data = frames[0]["t"]
            last_send = -1e9
            for fr in frames:
                # Veri kumesi zamanlamasini koru (speed ile hizlandir/yavaslat).
                target_wall = t0_wall + (fr["t"] - t0_data) / max(args.speed, 1e-6)
                delay = target_wall - time.perf_counter()
                if delay > 0:
                    time.sleep(delay)
                if fr["t"] - last_send < min_period * args.speed:
                    continue  # gonderim hizini sinirla (varsayilan 20 Hz)
                last_send = fr["t"]

                slam = fr["slam"]
                if slam is None:
                    skipped_slam += 1
                seq += 1
                msg = build_message(
                    seq=seq,
                    gt_geo=fr["gt_geo"],
                    gt_yaw=fr["gt_yaw"],
                    slam_geo=None if slam is None else slam["geo"],
                    slam_yaw=None if slam is None else slam["yaw"],
                    confidence=None if slam is None else slam["confidence"],
                    speed_mps=fr["speed"],
                    source_id=args.source_id,
                    auth_token=args.auth_token,
                    vehicle_type=args.vehicle_type,
                    mission_phase=args.mission_phase,
                    battery_percent=max(20.0, 98.0 - 60.0 * (sent / max(1, len(frames)))),
                )
                payload = json.dumps(msg, separators=(",", ":")).encode("utf-8")
                if sock is not None:
                    sock.sendto(payload, target)
                sent += 1

                if args.save_geo_csv:
                    p, sp = msg["pose"], msg.get("slamPose")
                    csv_rows.append(
                        "{};{:.9f};{:.9f};{};{};{}".format(
                            msg["timestampMs"],
                            p["latitude"],
                            p["longitude"],
                            f"{sp['latitude']:.9f}" if sp else "",
                            f"{sp['longitude']:.9f}" if sp else "",
                            f"{sp['confidence']:.3f}" if sp else "",
                        )
                    )
                if args.verbose and sent % 20 == 0:
                    print(
                        f"  #{sent:5d}  t={fr['t'] - t0_data:7.2f}s  "
                        f"GT {fr['gt_geo'][0]:.7f},{fr['gt_geo'][1]:.7f}"
                        + ("  SLAM yok (izleme kaybi)" if slam is None else f"  guven={slam['confidence']:.2f}")
                    )
            loops += 1
            if not args.loop:
                break
            print(f"[besleyici] Tur {loops} bitti, bastan baslaniyor (Ctrl+C ile cikis)...")
    except KeyboardInterrupt:
        print("\n[besleyici] Durduruldu.")
    finally:
        if sock is not None:
            sock.close()
        if args.save_geo_csv and csv_rows:
            with open(args.save_geo_csv, "w", encoding="utf-8") as fh:
                fh.write("timestampMs;gtLat;gtLon;slamLat;slamLon;confidence\n")
                fh.write("\n".join(csv_rows) + "\n")
            print(f"[besleyici] Gonderilen konumlar CSV olarak yazildi: {args.save_geo_csv}")

    print(
        f"[besleyici] Gonderilen mesaj: {sent}"
        + (f"  (SLAM pozu olmayan: {skipped_slam})" if skipped_slam else "")
        + (f"  -> udp://{args.host}:{args.port}" if not args.dry_run else "  (dry-run, UDP kapali)")
    )


def build_parser() -> argparse.ArgumentParser:
    ap = argparse.ArgumentParser(
        description="Ground truth + ORB-SLAM3 yorungesini yer istasyonuna UDP ile besler.",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=__doc__,
    )
    ap.add_argument("--gt", default="data/MH01_GT.txt", help="Ground truth yorunge dosyasi")
    ap.add_argument("--slam", help="ORB-SLAM3 cikti yorungesi (TUM). Verilmezse --simulate-slam gerekir.")
    ap.add_argument("--simulate-slam", action="store_true", help="SLAM ciktisi yoksa GT'den gercekci kestirim uret")
    ap.add_argument("--gt-format", choices=["auto", "tum", "euroc"], default="auto")
    ap.add_argument("--slam-format", choices=["auto", "tum", "euroc"], default="auto")
    ap.add_argument("--align", choices=["sim3", "se3", "none"], default="sim3",
                    help="sim3: monoculer (olcek serbest), se3: stereo/IMU, none: hizalama yok")
    ap.add_argument("--max-dt", type=float, default=0.05, help="Zaman eslestirme toleransi (s)")

    ap.add_argument("--host", default="127.0.0.1", help="Yer istasyonu IP")
    ap.add_argument("--port", type=int, default=19090, help="DigitalTwinUdpIngress portu")
    ap.add_argument("--rate", type=float, default=20.0, help="Saniyedeki maksimum mesaj (0 = sinirsiz)")
    ap.add_argument("--speed", type=float, default=1.0, help="Oynatma hizi carpani")
    ap.add_argument("--loop", action="store_true", help="Bittiginde bastan basla")
    ap.add_argument("--dry-run", action="store_true", help="UDP gonderme, sadece hesapla/raporla")
    ap.add_argument("--verbose", action="store_true")

    ap.add_argument("--anchor", type=_parse_anchor, default=DEFAULT_ANCHOR,
                    help="Yerel cercevenin oturtulacagi 'enlem,boylam' (varsayilan: harita merkezi)")
    ap.add_argument("--axes", default="xyz", help="Yerel eksen -> (dogu,kuzey,yukari) eslemesi, or. xyz / -yxz")
    ap.add_argument("--scale", type=float, default=1.0, help="Yorunge buyutme carpani (gorunurluk icin)")
    ap.add_argument("--altitude-offset", type=float, default=30.0, help="Irtifaya eklenen sabit (m)")

    ap.add_argument("--source-id", default="orbslam3-feeder")
    ap.add_argument("--auth-token", default="simurgh-2026")
    ap.add_argument("--vehicle-type", default="uav")
    ap.add_argument("--mission-phase", default="scan")

    ap.add_argument("--seed", type=int, default=7, help="Simulasyon tohumu")
    ap.add_argument("--sim-scale", type=float, default=0.78, help="Simulasyonda monoculer olcek hatasi")
    ap.add_argument("--save-slam", help="Simule SLAM yorungesini TUM biciminde bu dosyaya yaz")
    ap.add_argument("--save-geo-csv", help="Gonderilen lat/lon ciftlerini CSV olarak kaydet")
    return ap


def main(argv: Optional[Sequence[str]] = None) -> int:
    args = build_parser().parse_args(argv)
    if not args.slam and not args.simulate_slam:
        print("[besleyici] --slam verilmedi; --simulate-slam ile GT'den kestirim uretilecek.\n"
              "            Gercek ORB-SLAM3 ciktisi icin: --slam f_dataset-MH01_mono.txt", file=sys.stderr)
        args.simulate_slam = True

    frames, meta = prepare_frames(args)
    if abs(args.scale - 1.0) > 1e-6:
        print(f"[besleyici] UYARI: yorunge x{args.scale:g} buyutuldu. Yer istasyonundaki sapma/RMSE "
              f"degerleri de x{args.scale:g} gorunecek; gercek ATE RMSE {meta['ate']['rmse']:.3f} m.")
    print(
        f"[besleyici] Capa: {meta['anchor'][0]:.5f}, {meta['anchor'][1]:.5f} · "
        f"sure {meta['duration_s']:.1f} s · cerceve {len(frames)}"
    )
    stream(frames, args)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
