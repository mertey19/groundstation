"""Yorunge dosyasi okuma, zaman eslestirme, Sim(3) hizalama ve cografi izdusum.

Desteklenen bicimler
--------------------
TUM      : ``timestamp tx ty tz qx qy qz qw``            (bosluk ayracli, ORB-SLAM3 ciktisi)
EuRoC    : ``timestamp_ns,px,py,pz,qw,qx,qy,qz[,...]``   (virgul ayracli, ASL ground truth)

ORB-SLAM3'un monoculer ciktisi keyfi bir referans cercevesinde ve keyfi olcektedir;
ground truth ile karsilastirmadan once Umeyama (Sim(3)) hizalamasi sarttir. Stereo/IMU
modunda olcek gozlemlenebilir oldugu icin ``with_scale=False`` (SE(3)) kullanilmalidir.
"""

from __future__ import annotations

import math
from typing import Iterable, List, NamedTuple, Sequence, Tuple

try:  # numpy varsa tam 3B Umeyama; yoksa 2B (yaw) kapali form yedegine dusulur.
    import numpy as _np
except ImportError:  # pragma: no cover - ortama bagli
    _np = None


class Pose(NamedTuple):
    t: float  # saniye
    x: float
    y: float
    z: float
    qx: float
    qy: float
    qz: float
    qw: float


class Sim3(NamedTuple):
    """est -> gt donusumu: ``p_gt ~= s * R @ p_est + t``."""

    s: float
    R: Tuple[Tuple[float, float, float], ...]
    t: Tuple[float, float, float]
    method: str  # "umeyama3d" | "yaw2d"


# --------------------------------------------------------------------------- IO


def _normalize_time(raw: float) -> float:
    """Nanosaniye/mikrosaniye damgalarini saniyeye cevirir."""
    if raw > 1e14:  # 1.4e18 -> EuRoC nanosaniye
        return raw / 1e9
    if raw > 1e11:  # mikrosaniye
        return raw / 1e6
    return raw


def parse_trajectory_text(text: str, fmt: str = "auto") -> List[Pose]:
    poses: List[Pose] = []
    for line in text.splitlines():
        line = line.strip()
        if not line or line[0] in "#%":
            continue
        if "," in line:
            parts = [p for p in line.replace(";", ",").split(",") if p.strip()]
            line_fmt = "euroc"
        else:
            parts = line.split()
            line_fmt = "tum"
        if fmt != "auto":
            line_fmt = fmt
        if len(parts) < 8:
            continue
        try:
            vals = [float(p) for p in parts[:8]]
        except ValueError:
            continue  # basliklar ve bozuk satirlar sessizce atlanir
        t = _normalize_time(vals[0])
        x, y, z = vals[1], vals[2], vals[3]
        if line_fmt == "euroc":
            qw, qx, qy, qz = vals[4], vals[5], vals[6], vals[7]
        else:
            qx, qy, qz, qw = vals[4], vals[5], vals[6], vals[7]
        poses.append(Pose(t, x, y, z, qx, qy, qz, qw))
    poses.sort(key=lambda p: p.t)
    return poses


def load_trajectory(path: str, fmt: str = "auto") -> List[Pose]:
    with open(path, "r", encoding="utf-8", errors="replace") as fh:
        poses = parse_trajectory_text(fh.read(), fmt)
    if not poses:
        raise ValueError(f"Yorunge okunamadi (gecerli satir yok): {path}")
    return poses


def save_tum(path: str, poses: Iterable[Pose]) -> None:
    """ORB-SLAM3 ciktisiyla ayni TUM bicimi (evo/evaluate_ate ile uyumlu)."""
    with open(path, "w", encoding="utf-8") as fh:
        fh.write("# timestamp tx ty tz qx qy qz qw\n")
        for p in poses:
            fh.write(
                f"{p.t:.9f} {p.x:.7f} {p.y:.7f} {p.z:.7f} "
                f"{p.qx:.7f} {p.qy:.7f} {p.qz:.7f} {p.qw:.7f}\n"
            )


# ------------------------------------------------------------------ eslestirme


def associate(gt: Sequence[Pose], est: Sequence[Pose], max_dt: float = 0.05) -> List[Tuple[int, int]]:
    """Zaman damgasina gore en yakin komsu eslestirmesi (iki isaretci, O(n+m)).

    Her est ornegi en fazla bir kez eslesir; ``max_dt`` disindaki cift atilir.
    """
    pairs: List[Tuple[int, int]] = []
    if not gt or not est:
        return pairs
    j = 0
    used_gt = -1
    for i, e in enumerate(est):
        while j + 1 < len(gt) and abs(gt[j + 1].t - e.t) <= abs(gt[j].t - e.t):
            j += 1
        if abs(gt[j].t - e.t) <= max_dt and j != used_gt:
            pairs.append((j, i))
            used_gt = j
    return pairs


# --------------------------------------------------------------------- hizalama


def umeyama(src: Sequence[Sequence[float]], dst: Sequence[Sequence[float]], with_scale: bool = True) -> Sim3:
    """src -> dst icin en kucuk kareler benzerlik donusumu (Umeyama 1991)."""
    n = min(len(src), len(dst))
    if n < 3:
        raise ValueError("Hizalama icin en az 3 eslesmis nokta gerekli")
    if _np is None:
        return _umeyama_yaw2d(src, dst, with_scale)

    S = _np.asarray(src[:n], dtype=float)
    D = _np.asarray(dst[:n], dtype=float)
    mu_s, mu_d = S.mean(axis=0), D.mean(axis=0)
    Sc, Dc = S - mu_s, D - mu_d
    cov = (Dc.T @ Sc) / n
    U, sv, Vt = _np.linalg.svd(cov)
    # Yansimayi engelle: det(R) = +1 olmali (aksi halde ayna donusum uretilir).
    E = _np.eye(3)
    if _np.linalg.det(U) * _np.linalg.det(Vt) < 0:
        E[2, 2] = -1.0
    R = U @ E @ Vt
    var_s = (Sc ** 2).sum() / n
    s = float((sv * _np.diag(E)).sum() / var_s) if (with_scale and var_s > 1e-12) else 1.0
    t = mu_d - s * (R @ mu_s)
    return Sim3(s, tuple(tuple(float(v) for v in row) for row in R), tuple(float(v) for v in t), "umeyama3d")


def _umeyama_yaw2d(src: Sequence[Sequence[float]], dst: Sequence[Sequence[float]], with_scale: bool) -> Sim3:
    """numpy yoksa yedek: yalniz yaw + olcek + oteleme (kapali form, SVD gerektirmez).

    Harita gorunumu 2B oldugu icin gorsellestirme acisindan yeterlidir; z ekseni
    ortalama farkla hizalanir.
    """
    n = min(len(src), len(dst))
    msx = sum(p[0] for p in src[:n]) / n
    msy = sum(p[1] for p in src[:n]) / n
    msz = sum(p[2] for p in src[:n]) / n
    mdx = sum(p[0] for p in dst[:n]) / n
    mdy = sum(p[1] for p in dst[:n]) / n
    mdz = sum(p[2] for p in dst[:n]) / n
    num = den = var = 0.0
    for i in range(n):
        sx, sy = src[i][0] - msx, src[i][1] - msy
        dx, dy = dst[i][0] - mdx, dst[i][1] - mdy
        num += sx * dy - sy * dx  # capraz carpim -> sin
        den += sx * dx + sy * dy  # ic carpim   -> cos
        var += sx * sx + sy * sy  # olcek yalniz duzlem varyansindan (z ortalama ile oturur)
    theta = math.atan2(num, den)
    c, s_ = math.cos(theta), math.sin(theta)
    scale = 1.0
    if with_scale and var > 1e-12:
        scale = math.hypot(num, den) / var
    R = ((c, -s_, 0.0), (s_, c, 0.0), (0.0, 0.0, 1.0))
    tx = mdx - scale * (c * msx - s_ * msy)
    ty = mdy - scale * (s_ * msx + c * msy)
    tz = mdz - scale * msz
    return Sim3(scale, R, (tx, ty, tz), "yaw2d")


def apply_sim3(pose: Pose, tr: Sim3) -> Pose:
    R, t, s = tr.R, tr.t, tr.s
    x = s * (R[0][0] * pose.x + R[0][1] * pose.y + R[0][2] * pose.z) + t[0]
    y = s * (R[1][0] * pose.x + R[1][1] * pose.y + R[1][2] * pose.z) + t[1]
    z = s * (R[2][0] * pose.x + R[2][1] * pose.y + R[2][2] * pose.z) + t[2]
    return pose._replace(x=x, y=y, z=z)


def ate_stats(gt: Sequence[Pose], est: Sequence[Pose], pairs: Sequence[Tuple[int, int]]) -> dict:
    """Hizalanmis yorungeler icin mutlak yorunge hatasi istatistikleri (metre)."""
    errs = []
    for gi, ei in pairs:
        a, b = gt[gi], est[ei]
        errs.append(math.sqrt((a.x - b.x) ** 2 + (a.y - b.y) ** 2 + (a.z - b.z) ** 2))
    if not errs:
        return {"count": 0, "rmse": 0.0, "mean": 0.0, "median": 0.0, "max": 0.0}
    errs_sorted = sorted(errs)
    return {
        "count": len(errs),
        "rmse": math.sqrt(sum(e * e for e in errs) / len(errs)),
        "mean": sum(errs) / len(errs),
        "median": errs_sorted[len(errs_sorted) // 2],
        "max": errs_sorted[-1],
    }


# ------------------------------------------------------------------- cografya

# WGS84 ortalama derece basina metre (kucuk alan / duz dunya yaklasimi).
_M_PER_DEG_LAT = 111132.95


def enu_to_geo(lat0: float, lon0: float, east_m: float, north_m: float) -> Tuple[float, float]:
    lat = lat0 + north_m / _M_PER_DEG_LAT
    lon = lon0 + east_m / (_M_PER_DEG_LAT * math.cos(math.radians(lat0)))
    return lat, lon


def geo_to_enu(lat0: float, lon0: float, lat: float, lon: float) -> Tuple[float, float]:
    north = (lat - lat0) * _M_PER_DEG_LAT
    east = (lon - lon0) * _M_PER_DEG_LAT * math.cos(math.radians(lat0))
    return east, north


def haversine_m(lat1: float, lon1: float, lat2: float, lon2: float) -> float:
    r = 6371000.0
    dlat = math.radians(lat2 - lat1)
    dlon = math.radians(lon2 - lon1)
    h = (
        math.sin(dlat / 2) ** 2
        + math.cos(math.radians(lat1)) * math.cos(math.radians(lat2)) * math.sin(dlon / 2) ** 2
    )
    return 2 * r * math.asin(min(1.0, math.sqrt(h)))


def yaw_from_quaternion(qx: float, qy: float, qz: float, qw: float) -> float:
    """Gövde ekseninin ENU duzlemindeki yonu (derece, kuzeyden saat yonunde)."""
    # Z ekseni etrafindaki donme bileseni; ardindan pusula acisina cevrilir.
    siny = 2.0 * (qw * qz + qx * qy)
    cosy = 1.0 - 2.0 * (qy * qy + qz * qz)
    yaw_enu = math.atan2(siny, cosy)  # dogu ekseninden saat yonunun tersine
    return (90.0 - math.degrees(yaw_enu)) % 360.0


def heading_from_motion(e0: float, n0: float, e1: float, n1: float) -> float | None:
    de, dn = e1 - e0, n1 - n0
    if abs(de) < 1e-6 and abs(dn) < 1e-6:
        return None
    return math.degrees(math.atan2(de, dn)) % 360.0
