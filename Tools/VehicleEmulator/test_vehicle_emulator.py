import json
import math
import os
import socket
import subprocess
import sys
import threading
import time
import unittest
from pathlib import Path

from vehicle_emulator import ALLOWED_COMMANDS, VehicleEmulator


def command(command_id, name="hold", token="test-token", vehicle="uav", source="emu-uav", extra=None):
    body = {
        "schemaVersion": "1.0",
        "type": "command",
        "commandId": command_id,
        "command": name,
        "vehicleType": vehicle,
        "targetSourceId": source,
        "timestampMs": int(time.time() * 1000),
        "authToken": token,
        "value": 0,
    }
    if extra:
        body.update(extra)
    return body


def route_extra(waypoints):
    return {"route": {"waypoints": waypoints}}


VALID_WAYPOINTS = [
    {"index": 0, "latitude": 41.304, "longitude": -81.752, "altitudeM": 35, "speedMps": 5, "holdSeconds": 0, "action": ""},
    {"index": 1, "latitude": 41.305, "longitude": -81.753, "altitudeM": 35, "speedMps": 5, "holdSeconds": 0, "action": ""},
]


class VehicleEmulatorTests(unittest.TestCase):
    def setUp(self):
        self.acks = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.acks.bind(("127.0.0.1", 0))
        self.acks.settimeout(1.5)
        port = self.acks.getsockname()[1]
        self.emu = VehicleEmulator("127.0.0.1", 0, port, "test-token", "uav", "emu-uav")
        self.emu.start()
        command_port = self.emu._sock.getsockname()[1]
        self.command_port = command_port
        self.tx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    def tearDown(self):
        self.emu.stop()
        self.acks.close()
        self.tx.close()

    def send(self, body):
        self.tx.sendto(json.dumps(body).encode("utf-8"), ("127.0.0.1", self.command_port))

    def send_raw(self, payload):
        if isinstance(payload, str):
            payload = payload.encode("utf-8")
        self.tx.sendto(payload, ("127.0.0.1", self.command_port))

    def read_ack(self):
        data, addr = self.acks.recvfrom(65535)
        self.assertEqual(addr[0], "127.0.0.1")
        return json.loads(data.decode("utf-8"))

    def test_accepted_then_applied_and_duplicate_is_not_reapplied(self):
        body = command("c1")
        self.send(body)
        self.assertEqual(self.read_ack()["status"], "accepted")
        self.assertEqual(self.read_ack()["status"], "applied")
        self.assertEqual(self.emu.apply_count["c1"], 1)
        self.send(body)
        self.assertEqual(self.read_ack()["status"], "applied")
        self.assertEqual(self.emu.apply_count["c1"], 1)

    def test_same_id_different_body_is_rejected(self):
        self.send(command("c2", name="hold"))
        self.read_ack()
        self.read_ack()
        self.send(command("c2", name="rtl"))
        ack = self.read_ack()
        self.assertEqual(ack["status"], "rejected")
        self.assertIn("different", ack["message"])
        self.assertEqual(self.emu.apply_count.get("c2", 0), 1)
        self.assertEqual(self.emu.mode, "SIM-EMU-HOLD")

    def test_unsupported_command_is_not_success(self):
        self.send(command("c3", extra={"command": "arm_motors"}))
        ack = self.read_ack()
        self.assertEqual(ack["status"], "rejected")
        self.assertNotIn("arm_motors", ALLOWED_COMMANDS)
        self.assertNotIn("c3", self.emu.apply_count)

    def test_wrong_token_rejected(self):
        self.send(command("c4", token="other"))
        self.assertEqual(self.read_ack()["status"], "rejected")

    def test_non_loopback_host_rejected(self):
        with self.assertRaises(ValueError):
            VehicleEmulator("192.0.2.10", 19092, 19090, "test-token", "uav", "emu-uav")

    def test_empty_route_is_not_applied(self):
        self.send(command("empty-route", name="upload_route", extra=route_extra([])))
        ack = self.read_ack()
        self.assertEqual(ack["status"], "rejected")
        self.assertIn("route", ack["message"])
        self.assertNotIn("empty-route", self.emu.apply_count)
        self.send(command("empty-route", name="upload_route", extra=route_extra([])))
        retry = self.read_ack()
        self.assertEqual(retry["status"], "rejected")
        self.assertNotIn("empty-route", self.emu.apply_count)

    def test_negative_speed_is_not_applied(self):
        self.send(command("neg-speed", name="set_speed", extra={"value": -3}))
        ack = self.read_ack()
        self.assertEqual(ack["status"], "rejected")
        self.assertNotIn("neg-speed", self.emu.apply_count)
        self.assertEqual(self.emu.speed_mps, 4.0)

    def test_invalid_timestamp_rejected_with_injected_clock(self):
        self.emu.clock = lambda: 10_000
        self.emu.timestamp_max_age_ms = 1000
        self.emu.timestamp_future_ms = 50
        self.send(command("old-ts", extra={"timestampMs": 1}))
        self.assertEqual(self.read_ack()["status"], "rejected")
        self.send(command("future-ts", extra={"timestampMs": 50_000}))
        self.assertEqual(self.read_ack()["status"], "rejected")
        self.send(command("zero-ts", extra={"timestampMs": 0}))
        self.assertEqual(self.read_ack()["status"], "rejected")
        self.assertNotIn("old-ts", self.emu.apply_count)

    def test_rejected_retry_does_not_become_applied(self):
        self.emu.force_reject = True
        body = command("forced")
        self.send(body)
        self.assertEqual(self.read_ack()["status"], "rejected")
        self.emu.force_reject = False
        self.send(body)
        ack = self.read_ack()
        self.assertEqual(ack["status"], "rejected")
        self.assertEqual(ack["message"], "forced")
        self.assertNotIn("forced", self.emu.apply_count)

    def test_json_array_does_not_kill_listener(self):
        self.send_raw("[]")
        self.send_raw("null")
        self.send_raw("4")
        self.send_raw('"nope"')
        self.send_raw("{")
        self.send_raw(b"\xff\xfe")
        time.sleep(0.2)
        self.send(command("after-garbage"))
        self.assertEqual(self.read_ack()["status"], "accepted")
        self.assertEqual(self.read_ack()["status"], "applied")
        self.assertEqual(self.emu.apply_count["after-garbage"], 1)

    def test_nan_and_missing_route_rejected(self):
        self.send(command("nan-speed", name="set_speed", extra={"value": float("nan")}))
        self.assertEqual(self.read_ack()["status"], "rejected")
        self.send(command("no-route", name="upload_route"))
        self.assertEqual(self.read_ack()["status"], "rejected")
        self.send(command("ok-route", name="upload_route", extra=route_extra(VALID_WAYPOINTS)))
        self.assertEqual(self.read_ack()["status"], "accepted")
        self.assertEqual(self.read_ack()["status"], "applied")
        self.assertEqual(self.emu.apply_count["ok-route"], 1)
        self.assertIsNotNone(self.emu.route)

    def test_concurrent_duplicate_does_not_double_apply(self):
        body = command("parallel")
        payload = json.dumps(body).encode("utf-8")

        def fire():
            self.tx.sendto(payload, ("127.0.0.1", self.command_port))

        threads = [threading.Thread(target=fire) for _ in range(8)]
        for thread in threads:
            thread.start()
        for thread in threads:
            thread.join()
        statuses = []
        deadline = time.time() + 2
        while time.time() < deadline and len(statuses) < 16:
            try:
                statuses.append(self.read_ack()["status"])
            except socket.timeout:
                break
        self.assertEqual(self.emu.apply_count.get("parallel", 0), 1)
        self.assertNotIn("accepted", statuses[statuses.index("applied") + 1:] if "applied" in statuses else [])


class VehicleEmulatorCliTests(unittest.TestCase):
    def test_cli_telemetry_does_not_block_acks_and_exits(self):
        script = Path(__file__).resolve().parent / "vehicle_emulator.py"
        rx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        rx.bind(("127.0.0.1", 0))
        rx.settimeout(2.0)
        telemetry_port = rx.getsockname()[1]
        env = os.environ.copy()
        env["PYTHONUNBUFFERED"] = "1"
        proc = subprocess.Popen(
            [
                sys.executable,
                str(script),
                "--host", "127.0.0.1",
                "--command-port", "0",
                "--telemetry-port", str(telemetry_port),
                "--telemetry-hz", "20",
                "--token", "test-token",
                "--vehicle", "uav",
                "--source", "sim-emu-uav",
            ],
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            text=True,
            env=env,
        )
        command_port = None
        try:
            deadline = time.time() + 5
            while time.time() < deadline:
                line = proc.stdout.readline()
                if not line:
                    break
                if "listening on" in line:
                    command_port = int(line.strip().rsplit(":", 1)[1])
                    break
            self.assertIsNotNone(command_port, "CLI did not print the bound command port")
            tel = None
            deadline = time.time() + 3
            while time.time() < deadline and tel is None:
                data, addr = rx.recvfrom(65535)
                self.assertEqual(addr[0], "127.0.0.1")
                tel = json.loads(data.decode("utf-8"))
            self.assertIsNotNone(tel)
            self.assertTrue(tel.get("simulated"))
            self.assertEqual(tel.get("sourceId"), "sim-emu-uav")
            self.assertEqual(tel["telemetry"]["mode"], "SIM-EMU")
            tx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
            try:
                tx.sendto(
                    json.dumps(command("cli-hold", source="sim-emu-uav")).encode("utf-8"),
                    ("127.0.0.1", command_port),
                )
                statuses = []
                ack_deadline = time.time() + 3
                while time.time() < ack_deadline and len(statuses) < 2:
                    msg = json.loads(rx.recvfrom(65535)[0].decode("utf-8"))
                    if msg.get("type") == "command_ack":
                        statuses.append(msg.get("status"))
            finally:
                tx.close()
            self.assertEqual(statuses, ["accepted", "applied"])
        finally:
            proc.terminate()
            try:
                proc.wait(timeout=5)
            except subprocess.TimeoutExpired:
                proc.kill()
                proc.wait(timeout=2)
            if proc.stdout is not None:
                proc.stdout.close()
            rx.close()
        self.assertIsNotNone(proc.returncode)


if __name__ == "__main__":
    unittest.main()
