"""Besleyici hattinin birim ve ucdan uca testleri:  python test_slam_feeder.py"""

from __future__ import annotations

import json
import math
import os
import socket
import unittest

import simulate
import slam_feeder
import trajectory as tj
import udp_probe

HERE = os.path.dirname(os.path.abspath(__file__))
GT_FILE = os.path.join(HERE, "data", "MH01_GT.txt")


def synthetic_gt(n=200, dt=0.05):
    """Sekiz cizen kucuk bir yorunge (veri dosyasi olmadan da test kosabilsin)."""
    out = []
    for i in range(n):
        t = i * dt
        out.append(tj.Pose(t, 6.0 * math.sin(0.2 * t), 4.0 * math.sin(0.4 * t), 1.5 + 0.2 * math.sin(0.1 * t),
                           0.0, 0.0, math.sin(0.05 * t), math.cos(0.05 * t)))
    return out


class TestParsing(unittest.TestCase):
    def test_tum_and_euroc_column_order(self):
        tum = tj.parse_trajectory_text("# c\n1403636580.8635 4.665 -1.847 0.781 0.691 0.474 -0.307 0.450\n")
        self.assertEqual(len(tum), 1)
        self.assertAlmostEqual(tum[0].qw, 0.450)  # TUM: qx qy qz qw
        self.assertAlmostEqual(tum[0].t, 1403636580.8635, places=3)

        euroc = tj.parse_trajectory_text(
            "#timestamp [ns],p_x,p_y,p_z,q_w,q_x,q_y,q_z\n"
            "1403636580863555584.0,4.665,-1.847,0.781,0.450,0.691,0.474,-0.307\n"
        )
        self.assertEqual(len(euroc), 1)
        self.assertAlmostEqual(euroc[0].qw, 0.450)  # EuRoC: qw once
        self.assertAlmostEqual(euroc[0].t, 1403636580.863555, places=3)

    def test_nanosecond_normalisation(self):
        self.assertAlmostEqual(tj._normalize_time(1403636580863555584.0), 1403636580.863556, places=4)
        self.assertAlmostEqual(tj._normalize_time(1403636580.8635), 1403636580.8635, places=4)

    @unittest.skipUnless(os.path.exists(GT_FILE), "EuRoC GT dosyasi yok")
    def test_real_euroc_ground_truth(self):
        gt = tj.load_trajectory(GT_FILE)
        self.assertGreater(len(gt), 3000)
        self.assertGreater(gt[-1].t - gt[0].t, 100.0)  # MH01 ~182 s
        self.assertTrue(all(b.t >= a.t for a, b in zip(gt, gt[1:])))


class TestAlignment(unittest.TestCase):
    def _known_transform(self, poses, s=0.7, yaw=0.9, off=(3.0, -2.0, 1.0)):
        c, sn = math.cos(yaw), math.sin(yaw)
        out = []
        for p in poses:
            x = s * (c * p.x - sn * p.y) + off[0]
            y = s * (sn * p.x + c * p.y) + off[1]
            z = s * p.z + off[2]
            out.append(p._replace(x=x, y=y, z=z))
        return out

    def test_umeyama_recovers_scale_and_pose(self):
        gt = synthetic_gt(120)
        est = self._known_transform(gt)  # est = T(gt); hizalama T^-1 bulmali
        src = [(p.x, p.y, p.z) for p in est]
        dst = [(p.x, p.y, p.z) for p in gt]
        tr = tj.umeyama(src, dst, with_scale=True)
        self.assertAlmostEqual(tr.s, 1.0 / 0.7, places=4)
        back = [tj.apply_sim3(p, tr) for p in est]
        worst = max(math.dist((a.x, a.y, a.z), (b.x, b.y, b.z)) for a, b in zip(gt, back))
        self.assertLess(worst, 1e-6)

    def test_yaw2d_fallback_matches(self):
        gt = synthetic_gt(120)
        est = self._known_transform(gt)
        src = [(p.x, p.y, p.z) for p in est]
        dst = [(p.x, p.y, p.z) for p in gt]
        tr = tj._umeyama_yaw2d(src, dst, with_scale=True)
        self.assertEqual(tr.method, "yaw2d")
        self.assertAlmostEqual(tr.s, 1.0 / 0.7, places=4)
        back = [tj.apply_sim3(p, tr) for p in est]
        worst = max(math.dist((a.x, a.y, a.z), (b.x, b.y, b.z)) for a, b in zip(gt, back))
        self.assertLess(worst, 1e-6)

    def test_se3_alignment_keeps_scale(self):
        gt = synthetic_gt(60)
        est = self._known_transform(gt, s=1.0)
        tr = tj.umeyama([(p.x, p.y, p.z) for p in est], [(p.x, p.y, p.z) for p in gt], with_scale=False)
        self.assertAlmostEqual(tr.s, 1.0, places=9)

    def test_associate_pairs_nearest_and_respects_tolerance(self):
        gt = synthetic_gt(50, dt=0.05)
        est = [p._replace(t=p.t + 0.004) for p in gt[::2]]
        pairs = tj.associate(gt, est, max_dt=0.01)
        self.assertEqual(len(pairs), len(est))
        self.assertTrue(all(gi == ei * 2 for gi, ei in pairs))
        self.assertEqual(len(tj.associate(gt, [p._replace(t=p.t + 5.0) for p in est], max_dt=0.01)), 0)


class TestGeo(unittest.TestCase):
    def test_enu_geo_roundtrip(self):
        lat0, lon0 = slam_feeder.DEFAULT_ANCHOR
        for east, north in ((0.0, 0.0), (12.5, -30.0), (-250.0, 480.0)):
            lat, lon = tj.enu_to_geo(lat0, lon0, east, north)
            e2, n2 = tj.geo_to_enu(lat0, lon0, lat, lon)
            self.assertAlmostEqual(east, e2, places=6)
            self.assertAlmostEqual(north, n2, places=6)

    def test_metre_distance_matches_haversine(self):
        lat0, lon0 = slam_feeder.DEFAULT_ANCHOR
        lat1, lon1 = tj.enu_to_geo(lat0, lon0, 0.0, 0.0)
        lat2, lon2 = tj.enu_to_geo(lat0, lon0, 30.0, 40.0)  # 50 m
        self.assertAlmostEqual(tj.haversine_m(lat1, lon1, lat2, lon2), 50.0, delta=0.1)

    def test_heading_is_compass_bearing(self):
        self.assertAlmostEqual(tj.heading_from_motion(0, 0, 0, 10), 0.0, places=3)    # kuzey
        self.assertAlmostEqual(tj.heading_from_motion(0, 0, 10, 0), 90.0, places=3)   # dogu
        self.assertIsNone(tj.heading_from_motion(0, 0, 0, 0))


class TestSimulation(unittest.TestCase):
    def test_simulated_slam_needs_alignment_but_matches_after(self):
        gt = synthetic_gt(400)
        est = simulate.simulate_vislam(gt, seed=3)
        self.assertLess(len(est), len(gt))  # izleme kaybi araligi -> bosluk

        pairs = tj.associate(gt, est, 0.02)
        self.assertGreater(len(pairs), 200)
        raw = tj.ate_stats(gt, est, pairs)
        self.assertGreater(raw["rmse"], 1.0)  # hizalanmadan buyuk (keyfi cerceve/olcek)

        tr = tj.umeyama([(est[e].x, est[e].y, est[e].z) for _, e in pairs],
                        [(gt[g].x, gt[g].y, gt[g].z) for g, _ in pairs], with_scale=True)
        aligned = [tj.apply_sim3(p, tr) for p in est]
        after = tj.ate_stats(gt, aligned, pairs)
        self.assertLess(after["rmse"], 1.0)   # hizalamadan sonra surume seviyesinde
        self.assertGreater(after["rmse"], 0.0)

    def test_simulation_is_deterministic(self):
        gt = synthetic_gt(50)
        a = simulate.simulate_vislam(gt, seed=11)
        b = simulate.simulate_vislam(gt, seed=11)
        self.assertEqual([p.x for p in a], [p.x for p in b])


class TestMessages(unittest.TestCase):
    def test_message_passes_ground_station_schema(self):
        msg = slam_feeder.build_message(
            seq=5, gt_geo=(39.87, 32.73, 30.0), gt_yaw=12.0,
            slam_geo=(39.870005, 32.730002, 30.2), slam_yaw=13.0, confidence=0.87,
            speed_mps=2.4, source_id="test", auth_token="simurgh-2026",
            vehicle_type="uav", mission_phase="scan", battery_percent=90.0,
        )
        self.assertEqual(udp_probe.validate(msg), [])
        self.assertEqual(msg["schemaVersion"], "1.0")
        # Unity JsonUtility alanlari birebir isimle bekler.
        self.assertEqual(set(msg["slamPose"]), {"latitude", "longitude", "altitudeM",
                                                "yawDeg", "pitchDeg", "rollDeg", "confidence"})
        # Enlem/boylam yuvarlamasi metre alti farki korumali (float32 olsaydi kaybolurdu).
        self.assertNotEqual(msg["pose"]["latitude"], msg["slamPose"]["latitude"])

    def test_missing_slam_pose_is_omitted_not_zeroed(self):
        msg = slam_feeder.build_message(
            seq=1, gt_geo=(39.87, 32.73, 30.0), gt_yaw=0.0, slam_geo=None, slam_yaw=None,
            confidence=None, speed_mps=0.0, source_id="t", auth_token="", vehicle_type="uav",
            mission_phase="scan", battery_percent=100.0,
        )
        self.assertNotIn("slamPose", msg)  # (0,0)'a isinlamayi onler
        self.assertEqual(udp_probe.validate(msg), [])

    def test_probe_flags_broken_messages(self):
        self.assertTrue(udp_probe.validate({"pose": {"latitude": 999.0, "longitude": 0.0,
                                                     "altitudeM": 0.0, "yawDeg": 0.0},
                                            "vehicleType": "uav"}))
        self.assertTrue(udp_probe.validate({"vehicleType": "uav"}))


class TestEndToEnd(unittest.TestCase):
    def test_feeder_streams_valid_udp_to_listener(self):
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.bind(("127.0.0.1", 0))
        sock.settimeout(5.0)
        port = sock.getsockname()[1]

        gt_path = os.path.join(HERE, "data", "_test_gt.txt")
        tj.save_tum(gt_path, synthetic_gt(120))
        try:
            args = slam_feeder.build_parser().parse_args([
                "--gt", gt_path, "--simulate-slam", "--host", "127.0.0.1", "--port", str(port),
                "--speed", "1000", "--rate", "0", "--anchor", "39.8719,32.7302",
            ])
            frames, meta = slam_feeder.prepare_frames(args)
            self.assertEqual(len(frames), 120)
            self.assertLess(meta["ate"]["rmse"], 1.0)
            slam_feeder.stream(frames, args)

            received = []
            while True:
                try:
                    data, _ = sock.recvfrom(65535)
                except socket.timeout:
                    break
                received.append(json.loads(data.decode("utf-8")))
                if len(received) >= len(frames):
                    break
        finally:
            sock.close()
            os.remove(gt_path)

        self.assertGreaterEqual(len(received), 100)
        self.assertTrue(all(udp_probe.validate(m) == [] for m in received))
        seqs = [m["sequenceId"] for m in received]
        self.assertEqual(seqs, sorted(seqs))  # yer istasyonu geri sirali mesaji reddeder
        with_slam = [m for m in received if "slamPose" in m]
        self.assertGreater(len(with_slam), 0.5 * len(received))
        devs = [tj.haversine_m(m["pose"]["latitude"], m["pose"]["longitude"],
                               m["slamPose"]["latitude"], m["slamPose"]["longitude"]) for m in with_slam]
        self.assertLess(max(devs), 5.0)   # hizalanmis: sapma surume seviyesinde
        self.assertGreater(max(devs), 0.0)


if __name__ == "__main__":
    unittest.main(verbosity=2)
