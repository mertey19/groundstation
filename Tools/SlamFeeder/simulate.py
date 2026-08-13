"""Ground truth'tan gercekci bir VI-SLAM kestirimi uretir (ORB-SLAM3 ciktisi yerine).

Bu modul ORB-SLAM3'un YERINE gecmez; ORB-SLAM3 derlenip kosturulana kadar yer
istasyonu hattinin ucundan ucuna calistigini gostermek icindir. Uretilen dosya
gercek ORB-SLAM3 ciktisiyla ayni TUM biciminde oldugu icin, gercek cikti hazir
oldugunda tek yapilacak sey ``--slam`` yolunu degistirmektir.

Taklit edilen gercek davranislar:
  * keyfi referans cercevesi ve monoculer olcek belirsizligi (Sim(3) hizalama gerekir),
  * zamanla buyuyen konum surumesi (rastgele yuruyus / OU sureci),
  * kapanis (loop closure) aninda surumenin kismi sifirlanmasi,
  * izleme kaybi (tracking loss) boyunca poz uretilmemesi -> akista bosluk.
"""

from __future__ import annotations

import math
import random
from typing import List, Sequence

from trajectory import Pose


def simulate_vislam(
    gt: Sequence[Pose],
    seed: int = 7,
    scale: float = 0.78,
    drift_sigma_mps: float = 0.035,
    noise_sigma_m: float = 0.012,
    loop_closure_at: Sequence[float] = (0.45, 0.85),
    tracking_loss: Sequence[tuple] = ((0.62, 0.65),),
) -> List[Pose]:
    """GT'den tureyen, keyfi cerceveli ve surukleneli bir SLAM yorungesi dondurur.

    scale: monoculer olcek belirsizligi (1.0 = olcek dogru, stereo/IMU durumu).
    drift_sigma_mps: surumenin saniyedeki rastgele yuruyus siddeti (m/s^0.5).
    loop_closure_at: yorungenin bu oranlarinda birikmis surume %80 geri alinir.
    tracking_loss: (baslangic_orani, bitis_orani) araliklarinda poz uretilmez.
    """
    if not gt:
        return []
    rng = random.Random(seed)

    # 1) Keyfi referans cercevesi: SLAM ilk karede baslar, dunya cercevesini bilmez.
    yaw = rng.uniform(-math.pi, math.pi)
    pitch = rng.uniform(-0.05, 0.05)
    roll = rng.uniform(-0.05, 0.05)
    R = _euler_to_matrix(roll, pitch, yaw)
    origin = gt[0]
    t_off = (rng.uniform(-4.0, 4.0), rng.uniform(-4.0, 4.0), rng.uniform(-1.0, 1.0))

    total = gt[-1].t - gt[0].t
    loss_windows = [
        (gt[0].t + a * total, gt[0].t + b * total) for a, b in tracking_loss if 0.0 <= a < b <= 1.0
    ]
    closures = sorted(gt[0].t + f * total for f in loop_closure_at if 0.0 < f < 1.0)

    out: List[Pose] = []
    drift = [0.0, 0.0, 0.0]
    prev_t = gt[0].t
    next_closure = 0
    for p in gt:
        dt = max(0.0, p.t - prev_t)
        prev_t = p.t

        # 2) Surume: rastgele yuruyus (varyansi zamanla dogrusal buyur).
        step = drift_sigma_mps * math.sqrt(dt) if dt > 0 else 0.0
        for k in range(3):
            drift[k] += rng.gauss(0.0, step) * (0.4 if k == 2 else 1.0)  # dikey surume daha az

        # 3) Kapanis: birikmis surumenin buyuk kismi geri alinir.
        while next_closure < len(closures) and p.t >= closures[next_closure]:
            for k in range(3):
                drift[k] *= 0.2
            next_closure += 1

        # 4) Izleme kaybi: bu araliklarda SLAM poz uretemez.
        if any(a <= p.t <= b for a, b in loss_windows):
            continue

        lx, ly, lz = p.x - origin.x, p.y - origin.y, p.z - origin.z
        sx = scale * (R[0][0] * lx + R[0][1] * ly + R[0][2] * lz) + t_off[0]
        sy = scale * (R[1][0] * lx + R[1][1] * ly + R[1][2] * lz) + t_off[1]
        sz = scale * (R[2][0] * lx + R[2][1] * ly + R[2][2] * lz) + t_off[2]

        # Surume ve olcum gurultusu SLAM cercevesinde eklenir (olcekle carpilir).
        sx += scale * (drift[0] + rng.gauss(0.0, noise_sigma_m))
        sy += scale * (drift[1] + rng.gauss(0.0, noise_sigma_m))
        sz += scale * (drift[2] + rng.gauss(0.0, noise_sigma_m * 0.6))

        qx, qy, qz, qw = _quat_mul(_matrix_to_quat(R), (p.qx, p.qy, p.qz, p.qw))
        out.append(Pose(p.t, sx, sy, sz, qx, qy, qz, qw))
    return out


# ------------------------------------------------------------------ yardimcilar


def _euler_to_matrix(roll: float, pitch: float, yaw: float):
    cr, sr = math.cos(roll), math.sin(roll)
    cp, sp = math.cos(pitch), math.sin(pitch)
    cy, sy = math.cos(yaw), math.sin(yaw)
    return (
        (cy * cp, cy * sp * sr - sy * cr, cy * sp * cr + sy * sr),
        (sy * cp, sy * sp * sr + cy * cr, sy * sp * cr - cy * sr),
        (-sp, cp * sr, cp * cr),
    )


def _matrix_to_quat(R):
    tr = R[0][0] + R[1][1] + R[2][2]
    if tr > 0.0:
        s = math.sqrt(tr + 1.0) * 2.0
        return ((R[2][1] - R[1][2]) / s, (R[0][2] - R[2][0]) / s, (R[1][0] - R[0][1]) / s, 0.25 * s)
    if R[0][0] > R[1][1] and R[0][0] > R[2][2]:
        s = math.sqrt(1.0 + R[0][0] - R[1][1] - R[2][2]) * 2.0
        return (0.25 * s, (R[0][1] + R[1][0]) / s, (R[0][2] + R[2][0]) / s, (R[2][1] - R[1][2]) / s)
    if R[1][1] > R[2][2]:
        s = math.sqrt(1.0 + R[1][1] - R[0][0] - R[2][2]) * 2.0
        return ((R[0][1] + R[1][0]) / s, 0.25 * s, (R[1][2] + R[2][1]) / s, (R[0][2] - R[2][0]) / s)
    s = math.sqrt(1.0 + R[2][2] - R[0][0] - R[1][1]) * 2.0
    return ((R[0][2] + R[2][0]) / s, (R[1][2] + R[2][1]) / s, 0.25 * s, (R[1][0] - R[0][1]) / s)


def _quat_mul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
        aw * bw - ax * bx - ay * by - az * bz,
    )
