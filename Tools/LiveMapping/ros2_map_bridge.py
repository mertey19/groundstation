#!/usr/bin/env python3
"""Forward RTAB-Map's complete registered cloud to the Unity ground station.

Requires ROS 2 on the Jetson. This Windows project does not supply a ROS runtime.
"""
import argparse
import os
import threading
import time
import uuid
from live_map_sender import cloud_points, downsample, send_snapshot


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", required=True, help="Ground-station LAN address")
    parser.add_argument("--port", type=int, default=19093)
    parser.add_argument("--topic", default="/rtabmap/cloud_map")
    parser.add_argument("--frame", default="map", help="Fixed SLAM map frame, never a camera optical frame")
    parser.add_argument("--source", default="jetson-realsense")
    parser.add_argument("--map-id", default="flight-" + uuid.uuid4().hex[:12])
    parser.add_argument("--voxel", type=float, default=0.15)
    parser.add_argument("--max-points", type=int, default=12000)
    parser.add_argument("--hz", type=float, default=1.0)
    args = parser.parse_args()
    token = os.environ.get("SIMURGH_MAP_TOKEN", "")
    if not token:
        parser.error("Set SIMURGH_MAP_TOKEN to the same value as the ground station")
    if not (0.1 <= args.hz <= 5 and 0.02 <= args.voxel <= 5 and 1 <= args.max_points <= 50000 and 1 <= args.port <= 65535):
        parser.error("Invalid rate, point limit, voxel size or port")
    try:
        import rclpy
        from rclpy.node import Node
        from rclpy.qos import QoSProfile, ReliabilityPolicy, HistoryPolicy, DurabilityPolicy
        from sensor_msgs.msg import PointCloud2
    except ImportError as error:
        parser.error("ROS 2 environment is unavailable; source its setup.bash on the Jetson. " + str(error))

    class Bridge(Node):
        def __init__(self):
            super().__init__("simurgh_live_map_bridge")
            self.lock = threading.Lock()
            self.latest = None
            self.stop = threading.Event()
            # A volatile depth-1 subscription accepts current publications from both
            # reliable and best-effort publishers, without replaying an old latched map.
            qos = QoSProfile(depth=1, history=HistoryPolicy.KEEP_LAST,
                             reliability=ReliabilityPolicy.BEST_EFFORT, durability=DurabilityPolicy.VOLATILE)
            self.subscription = self.create_subscription(PointCloud2, args.topic, self.on_cloud, qos)
            self.worker = threading.Thread(target=self.transmit, daemon=True)
            self.worker.start()
            self.get_logger().info(f"Map {args.map_id}; waiting for {args.topic} in {args.frame}; no synthetic data")

        def on_cloud(self, cloud):
            if len(cloud.data) > 64 * 1024 * 1024:
                self.get_logger().warning("Cloud exceeds 64 MiB input limit")
                return
            with self.lock:
                self.latest = cloud  # Back-pressure: replace waiting work, never grow a queue.

        def transmit(self):
            sequence = 0
            while not self.stop.wait(1 / args.hz):
                with self.lock:
                    cloud, self.latest = self.latest, None
                if cloud is None:
                    continue
                try:
                    captured = cloud.header.stamp.sec * 1000 + cloud.header.stamp.nanosec // 1_000_000
                    age = time.time() * 1000 - captured
                    if age > 4000 or age < -1000 or captured <= 0:
                        raise ValueError("Cloud stamp is stale or not Unix time; synchronize clocks; do not rebase old recordings")
                    points, voxel = downsample(cloud_points(cloud, args.frame), args.voxel, args.max_points)
                    if not points:
                        raise ValueError("Cloud has no usable depth; previous map is preserved")
                    sequence += 1
                    count = send_snapshot(args.host, args.port, points, token=token, source_id=args.source,
                                          map_id=args.map_id, sequence=sequence, captured_at_ms=captured,
                                          voxel_size=voxel, frame_id=args.frame)
                    self.get_logger().info(f"Received by station: v{sequence}, {len(points)} points, {count} bytes, voxel {voxel:.2f} m")
                except (ValueError, OSError, TimeoutError, ConnectionError) as error:
                    self.get_logger().warning(str(error))

    rclpy.init()
    node = Bridge()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.stop.set()
        node.worker.join(timeout=6)
        node.destroy_node()
        if rclpy.ok():
            rclpy.shutdown()


if __name__ == "__main__":
    main()
