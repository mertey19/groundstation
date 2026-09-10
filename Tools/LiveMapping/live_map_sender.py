"""Bounded registered-cloud transport. Does not perform SLAM or invent depth."""
import json
import math
import socket
import struct
import time

PROTOCOL = "simurgh-live-map/1"
MAX_POINTS = 50_000
CHUNK_POINTS = 256


def cloud_points(cloud, expected_frame="map", max_samples=200_000):
    """Decode real PointCloud2, including row padding, endian and packed RGB.

    Only a fixed, registered SLAM map is accepted. A single camera-frame depth
    cloud cannot be passed off as an accumulated environment reconstruction.
    """
    if cloud.header.frame_id != expected_frame:
        raise ValueError(f"Expected registered frame {expected_frame!r}; got {cloud.header.frame_id!r}")
    fields = {f.name: f for f in cloud.fields}
    for name in ("x", "y", "z"):
        if name not in fields or fields[name].datatype not in (7, 8) or fields[name].count != 1:
            raise ValueError("PointCloud2 needs scalar FLOAT32/FLOAT64 x,y,z fields")
    if cloud.width < 1 or cloud.height < 1 or cloud.width * cloud.height > 2_000_000:
        raise ValueError("Cloud point limit exceeded or cloud is empty")
    if not 12 <= cloud.point_step <= 256 or cloud.row_step < cloud.width * cloud.point_step:
        raise ValueError("Invalid PointCloud2 stride")
    if len(cloud.data) > 64 * 1024 * 1024 or len(cloud.data) < cloud.height * cloud.row_step:
        raise ValueError("Invalid PointCloud2 buffer length")
    endian = ">" if cloud.is_bigendian else "<"
    formats = [(fields[n].offset, struct.Struct(endian + ("f" if fields[n].datatype == 7 else "d"))) for n in ("x", "y", "z")]
    for offset, fmt in formats:
        if offset < 0 or offset + fmt.size > cloud.point_step:
            raise ValueError("PointCloud2 field outside point stride")
    rgb = fields.get("rgb", fields.get("rgba"))
    if rgb is not None and (rgb.datatype not in (6, 7) or rgb.count != 1 or rgb.offset < 0 or rgb.offset + 4 > cloud.point_step):
        raise ValueError("Unsupported packed RGB field")
    packed_color = struct.Struct(endian + "I")
    stride = max(1, math.ceil(cloud.width * cloud.height / max_samples))
    for i in range(0, cloud.width * cloud.height, stride):
        row, col = divmod(i, cloud.width)
        base = row * cloud.row_step + col * cloud.point_step
        xyz = [fmt.unpack_from(cloud.data, base + offset)[0] for offset, fmt in formats]
        if not all(math.isfinite(v) and abs(v) <= 10000 for v in xyz):
            continue
        if rgb is None:
            color = (86, 196, 214)
        else:
            value = packed_color.unpack_from(cloud.data, base + rgb.offset)[0]
            color = ((value >> 16) & 255, (value >> 8) & 255, value & 255)
        yield (*xyz, *color)


def downsample(points, voxel_size=0.15, max_points=12000):
    """Spatial sampling with bounded storage; coarsen the whole cloud, never crop its tail."""
    if not 0.02 <= voxel_size <= 5 or not 1 <= max_points <= MAX_POINTS:
        raise ValueError("Invalid sampling settings")
    cells = {}
    size = voxel_size

    def key(p):
        return tuple(math.floor(v / size) for v in p[:3])

    def coarsen():
        nonlocal cells, size
        if size >= 5:
            raise ValueError("Map exceeds bounded preview capacity; reduce the area or increase max_points")
        size = min(5.0, size * 2)
        smaller = {}
        for p in cells.values():
            smaller.setdefault(key(p), p)
        cells = smaller

    for p in points:
        if len(p) != 6 or not all(math.isfinite(float(v)) for v in p):
            continue
        if any(abs(v) > 10000 for v in p[:3]) or any(v < 0 or v > 255 or int(v) != v for v in p[3:]):
            continue
        cells.setdefault(key(p), tuple(p))
        while len(cells) > max_points * 2:
            coarsen()
    while len(cells) > max_points:
        coarsen()
    return list(cells.values()), size


def encode_snapshot(points, *, token, source_id, map_id, sequence, captured_at_ms,
                    voxel_size, frame_id="map", data_kind="measured"):
    if not token or not 1 <= len(points) <= MAX_POINTS or sequence < 1:
        raise ValueError("Empty token, cloud or invalid sequence")
    if data_kind not in ("measured", "test") or not 0.02 <= voxel_size <= 5:
        raise ValueError("Invalid map metadata")
    for name in (source_id, map_id, frame_id):
        if not name or len(name) > 80 or any(not (c.isascii() and (c.isalnum() or c in "_-/")) for c in name):
            raise ValueError("Invalid source/map/frame identifier")
    header = dict(type="begin", protocol=PROTOCOL, authToken=token, sourceId=source_id,
                  mapId=map_id, frameId=frame_id, frameConvention="ros_z_up_m", dataKind=data_kind,
                  sequence=sequence, capturedAtMs=captured_at_ms, pointCount=len(points),
                  chunkCount=math.ceil(len(points) / CHUNK_POINTS), voxelSizeM=voxel_size)

    def line(value):
        return (json.dumps(value, separators=(",", ":"), allow_nan=False) + "\n").encode("utf-8")

    yield line(header)
    for index, start in enumerate(range(0, len(points), CHUNK_POINTS)):
        rows = [[round(float(v), 4) for v in p[:3]] + [int(v) for v in p[3:]] for p in points[start:start + CHUNK_POINTS]]
        yield line(dict(type="points", index=index, points=rows))
    yield line(dict(type="end", sequence=sequence))


def send_snapshot(host, port, points, **metadata):
    started = time.monotonic()
    sent = 0
    with socket.create_connection((host, port), timeout=2) as connection:
        connection.settimeout(2)
        for data in encode_snapshot(points, **metadata):
            if time.monotonic() - started > 4:
                raise TimeoutError("Map transport took too long; reduce point count or rate")
            connection.sendall(data)
            sent += len(data)
        response = bytearray()
        while not response.endswith(b"\n"):
            part = connection.recv(1)
            if not part or len(response) >= 512:
                raise ConnectionError("Map was not acknowledged")
            response.extend(part)
        ack = json.loads(response)
        if ack.get("type") != "received" or ack.get("sequence") != metadata["sequence"]:
            raise ConnectionError("Unexpected map acknowledgement")
    return sent
