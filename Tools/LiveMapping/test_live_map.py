import io
import json
import math
import socket
import struct
import threading
import unittest
from pathlib import Path
from types import SimpleNamespace as NS
from live_map_sender import cloud_points, downsample, encode_snapshot, send_snapshot


def make_cloud(big=False):
    endian = ">" if big else "<"
    fields = [NS(name=n, offset=i * 4, datatype=7, count=1) for i, n in enumerate(("x", "y", "z", "rgb"))]
    data = struct.pack(endian + "fffI", 1, 2, 3, 0x12ABEF) + b"PAD!" + struct.pack(endian + "fffI", 4, 5, 6, 0xFF8040) + b"PAD!"
    return NS(header=NS(frame_id="map"), width=1, height=2, fields=fields, point_step=16, row_step=20, data=data, is_bigendian=big)


def fixture_points():
    # Deliberately synthetic courtyard + a vertical wall, for renderer checks only.
    points = []
    for x in range(-50, 51):
        for y in range(-40, 41):
            points.append((x * 0.12, y * 0.12, 0, 75, 118 + (x % 4) * 8, 101))
    for x in range(-25, 26):
        for z in range(1, 28):
            points.append((x * 0.12, 2.4, z * 0.12, 176 + z, 170 + z, 153))
    for x in range(-25, 26):
        for y in range(12, 21):
            points.append((x * 0.12, y * 0.12, 3.36, 71, 145, 179))
    return points


def metadata(**changes):
    result = dict(token="simurgh-2026", source_id="local-map-test", map_id="synthetic-courtyard",
                  sequence=1, captured_at_ms=100000, voxel_size=0.12, data_kind="test")
    result.update(changes)
    return result


class LiveMapTests(unittest.TestCase):
    def test_little_endian_and_row_padding(self):
        self.assertEqual(list(cloud_points(make_cloud())), [(1, 2, 3, 18, 171, 239), (4, 5, 6, 255, 128, 64)])

    def test_big_endian_rgb_is_bits_not_float(self):
        self.assertEqual(list(cloud_points(make_cloud(True)))[0], (1, 2, 3, 18, 171, 239))

    def test_camera_frame_is_not_registered_map(self):
        cloud = make_cloud(); cloud.header.frame_id = "camera_depth_optical_frame"
        with self.assertRaises(ValueError): list(cloud_points(cloud))

    def test_bad_stride(self):
        cloud = make_cloud(); cloud.row_step = 12
        with self.assertRaises(ValueError): list(cloud_points(cloud))

    def test_truncated_cloud(self):
        cloud = make_cloud(); cloud.data = cloud.data[:-1]
        with self.assertRaises(ValueError): list(cloud_points(cloud))

    def test_bad_field_offset(self):
        cloud = make_cloud(); cloud.fields[0].offset = 16
        with self.assertRaises(ValueError): list(cloud_points(cloud))

    def test_nan_depth_filtered(self):
        cloud = make_cloud(); cloud.data = struct.pack("<f", math.nan) + cloud.data[4:]
        self.assertEqual(len(list(cloud_points(cloud))), 1)

    def test_negative_voxels_and_duplicate_points(self):
        points = [(-0.01, 0, 0, 0, 1, 2), (0.01, 0, 0, 0, 1, 2), (0.05, 0, 0, 4, 5, 6)]
        sampled, size = downsample(points, .1)
        self.assertEqual(len(sampled), 2)
        self.assertEqual(size, .1)

    def test_coarsens_without_cropping_map(self):
        sampled, size = downsample(((i * .01, 0, 0, 1, 2, 3) for i in range(10000)), .02, 40)
        self.assertLessEqual(len(sampled), 40)
        self.assertGreater(max(p[0] for p in sampled), 95)
        self.assertGreater(size, .02)

    def test_invalid_points_not_retained(self):
        sampled, _ = downsample([(math.nan, 0, 0, 1, 2, 3), (0, 0, 0, 256, 2, 3), (1, 2, 3, 5, 6, 7)])
        self.assertEqual(len(sampled), 1)

    def test_chunk_sizes_and_count(self):
        data = list(encode_snapshot([(1, 2, 3, 4, 5, 6)] * 513, **metadata()))
        lines = [json.loads(x) for x in data]
        self.assertEqual(lines[0]["chunkCount"], 3)
        self.assertEqual([len(x["points"]) for x in lines[1:-1]], [256, 256, 1])
        self.assertEqual(lines[-1], dict(type="end", sequence=1))
        self.assertTrue(all(len(x) <= 65536 for x in data))

    def test_empty_map_cannot_clear_station(self):
        with self.assertRaises(ValueError): list(encode_snapshot([], **metadata()))

    def test_identifiers_reject_markup(self):
        with self.assertRaises(ValueError): list(encode_snapshot([(1, 2, 3, 1, 2, 3)], **metadata(source_id="<b>test</b>")))

    def test_socket_transport_and_ack(self):
        with socket.socket() as server:
            server.bind(("127.0.0.1", 0)); server.listen(1); port = server.getsockname()[1]
            result = []
            def receive():
                with server.accept()[0] as connection:
                    with connection.makefile("rb") as stream:
                        while True:
                            value = json.loads(stream.readline()); result.append(value)
                            if value["type"] == "end": break
                        connection.sendall(b'{"type":"received","sequence":1}\n')
            worker = threading.Thread(target=receive); worker.start()
            sent = send_snapshot("127.0.0.1", port, [(1, 2, 3, 4, 5, 6)], **metadata())
            worker.join(3)
            self.assertFalse(worker.is_alive()); self.assertGreater(sent, 100)
            self.assertEqual(result[1]["points"][0], [1, 2, 3, 4, 5, 6])


if __name__ == "__main__":
    # Shared Python-produced bytes are consumed by the C# protocol + socket checks.
    output = Path(__file__).resolve().parents[2] / "Temp" / "live-map-fixture.ndjson"
    output.parent.mkdir(exist_ok=True)
    output.write_bytes(b"".join(encode_snapshot(fixture_points(), **metadata())))
    unittest.main()
