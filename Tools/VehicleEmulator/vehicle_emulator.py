"""Loopback vehicle emulator for the ground-station JSON/UDP command contract.

Default bind and telemetry targets are 127.0.0.1 only. This is not flight-controller
firmware and does not prove hardware command execution.

Duplicate command cache keeps the last 4096 command IDs with fingerprint and terminal
status. Older IDs are evicted FIFO. applied is stored only after a simulated state
change completes. Duplicate retries replay that stored terminal result and never
re-apply.
"""
from __future__ import annotations

import argparse
import json
import math
import socket
import sys
import threading
import time
from collections import OrderedDict
from typing import Callable, Dict, Optional, Tuple


ALLOWED_COMMANDS = {
    "hold",
    "rtl",
    "emergency_stop",
    "set_speed",
    "set_altitude",
    "upload_route",
    "start_mission",
}

MAX_PACKET_BYTES = 65535
MAX_SEEN = 4096
MAX_WAYPOINTS = 2000


class VehicleEmulator:
    def __init__(
        self,
        host: str,
        command_port: int,
        telemetry_port: int,
        token: str,
        vehicle: str,
        source: str,
        clock: Optional[Callable[[], float]] = None,
        timestamp_max_age_ms: int = 30000,
        timestamp_future_ms: int = 5000,
    ):
        if host not in ("127.0.0.1", "::1"):
            raise ValueError("Default emulator binds only to loopback")
        self.host = host
        self.command_port = command_port
        self.telemetry_port = telemetry_port
        self.token = token
        self.vehicle = vehicle
        self.source = source
        self.clock = clock
        self.timestamp_max_age_ms = timestamp_max_age_ms
        self.timestamp_future_ms = timestamp_future_ms
        self.delay_s = 0.0
        self.drop_acks = False
        self.force_reject = False
        self.drop_telemetry = False
        self.telemetry_delay_s = 0.0
        self.apply_count: Dict[str, int] = {}
        self.seen: "OrderedDict[str, dict]" = OrderedDict()
        self.last_command: Optional[dict] = None
        self.running = False
        self.speed_mps = 4.0
        self.altitude_m = 35.0
        self.mode = "SIM-EMU"
        self.route = None
        self.mission_started = False
        self.holding = False
        self._seq = 1
        self._sock: Optional[socket.socket] = None
        self._lock = threading.Lock()
        self._threads: list = []

    def _now_ms(self) -> int:
        if self.clock is not None:
            return int(self.clock())
        return int(time.time() * 1000)

    def start(self, telemetry_hz: float = 0.0) -> None:
        self._sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._sock.bind((self.host, self.command_port))
        self.command_port = self._sock.getsockname()[1]
        self._sock.settimeout(0.2)
        self.running = True
        listener = threading.Thread(target=self._listen, name="sim-emu-listen", daemon=True)
        listener.start()
        self._threads.append(listener)
        if telemetry_hz and telemetry_hz > 0:
            tel = threading.Thread(
                target=self._telemetry_loop,
                args=(float(telemetry_hz),),
                name="sim-emu-telemetry",
                daemon=True,
            )
            tel.start()
            self._threads.append(tel)

    def stop(self) -> None:
        self.running = False
        sock = self._sock
        self._sock = None
        if sock is not None:
            try:
                sock.close()
            except OSError:
                pass
        for thread in self._threads:
            if thread.is_alive() and thread is not threading.current_thread():
                thread.join(1.0)
        self._threads = []

    def send_telemetry(self, battery: float = 80.0, altitude: float = None, sequence: int = None) -> None:
        if self.drop_telemetry:
            return
        if self.telemetry_delay_s > 0:
            time.sleep(self.telemetry_delay_s)
        if altitude is None:
            altitude = self.altitude_m
        if sequence is None:
            sequence = self._seq
            self._seq += 1
        message = {
            "schemaVersion": "1.0",
            "authToken": self.token,
            "sourceId": self.source,
            "vehicleType": self.vehicle,
            "sequenceId": sequence,
            "timestampMs": self._now_ms(),
            "telemetry": {
                "altitudeM": altitude,
                "speedMps": self.speed_mps,
                "mode": self.mode,
                "batteryPercent": battery,
            },
            "pose": {"latitude": 41.3043, "longitude": -81.7524, "altitudeM": altitude, "yawDeg": 0},
            "simulated": True,
        }
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.sendto(json.dumps(message).encode("utf-8"), (self.host, self.telemetry_port))
        sock.close()

    def _telemetry_loop(self, hz: float) -> None:
        interval = 1.0 / hz
        while self.running:
            started = time.time()
            try:
                self.send_telemetry()
            except OSError:
                if not self.running:
                    return
            remaining = interval - (time.time() - started)
            deadline = time.time() + max(0.0, remaining)
            while self.running and time.time() < deadline:
                time.sleep(min(0.05, max(0.0, deadline - time.time())))

    def _listen(self) -> None:
        while self.running and self._sock is not None:
            try:
                data, addr = self._sock.recvfrom(MAX_PACKET_BYTES)
            except socket.timeout:
                continue
            except OSError:
                return
            if not addr or addr[0] not in ("127.0.0.1", "::1"):
                continue
            if data is None or len(data) > MAX_PACKET_BYTES:
                continue
            try:
                text = data.decode("utf-8")
            except UnicodeDecodeError:
                continue
            try:
                command = json.loads(text)
            except json.JSONDecodeError:
                continue
            if not isinstance(command, dict):
                continue
            self.handle_command(command, addr)

    def handle_command(self, command, addr: Tuple[str, int]) -> Optional[str]:
        if not isinstance(command, dict):
            return None
        with self._lock:
            self.last_command = command
            reason = self._validate_envelope(command)
            if reason:
                return self._ack(command, addr, "rejected", reason)
            command_id = command["commandId"]
            fingerprint = self._fingerprint(command)
            previous = self.seen.get(command_id)
            if previous is not None:
                if previous["fingerprint"] != fingerprint:
                    return self._ack(command, addr, "rejected", "commandId reused with different body")
                return self._ack(command, addr, previous["status"], previous["message"])
            if self.force_reject:
                self._remember(command_id, fingerprint, "rejected", "forced")
                return self._ack(command, addr, "rejected", "forced")
            reason = self._validate_payload(command)
            if reason:
                self._remember(command_id, fingerprint, "rejected", reason)
                return self._ack(command, addr, "rejected", reason)
            accepted = self._ack(command, addr, "accepted", "")
            self._apply(command)
            self.apply_count[command_id] = self.apply_count.get(command_id, 0) + 1
            self._remember(command_id, fingerprint, "applied", "")
            applied = self._ack(command, addr, "applied", "")
            return applied or accepted

    def _validate_envelope(self, command: dict) -> Optional[str]:
        command_id = command.get("commandId")
        if not isinstance(command_id, str) or not command_id:
            return "commandId missing"
        if command.get("schemaVersion") != "1.0" or command.get("type") != "command":
            return "unsupported envelope"
        if command.get("authToken") != self.token:
            return "auth"
        if command.get("vehicleType") != self.vehicle or command.get("targetSourceId") != self.source:
            return "wrong vehicle"
        name = command.get("command")
        if name not in ALLOWED_COMMANDS:
            return "unsupported command"
        ts = command.get("timestampMs")
        if not self._finite_number(ts) or int(ts) <= 0:
            return "invalid timestamp"
        now = self._now_ms()
        ts = int(ts)
        if ts + self.timestamp_max_age_ms < now:
            return "invalid timestamp"
        if ts > now + self.timestamp_future_ms:
            return "invalid timestamp"
        return None

    def _validate_payload(self, command: dict) -> Optional[str]:
        name = command.get("command")
        if name == "set_speed":
            value = command.get("value")
            if not self._finite_number(value) or float(value) < 1 or float(value) > 50:
                return "invalid speed"
        elif name == "set_altitude":
            value = command.get("value")
            if not self._finite_number(value) or float(value) < 1 or float(value) > 500:
                return "invalid altitude"
        elif name == "upload_route":
            route = command.get("route")
            if not isinstance(route, dict):
                return "empty or invalid route"
            waypoints = route.get("waypoints")
            if not isinstance(waypoints, list) or len(waypoints) < 2 or len(waypoints) > MAX_WAYPOINTS:
                return "empty or invalid route"
            for waypoint in waypoints:
                if not isinstance(waypoint, dict):
                    return "invalid waypoint"
                lat = waypoint.get("latitude")
                lon = waypoint.get("longitude")
                alt = waypoint.get("altitudeM")
                speed = waypoint.get("speedMps", -1)
                hold = waypoint.get("holdSeconds", 0)
                if not self._finite_number(lat) or not self._finite_number(lon):
                    return "invalid coordinate"
                if float(lat) < -90 or float(lat) > 90 or float(lon) < -180 or float(lon) > 180:
                    return "invalid coordinate"
                if not self._finite_number(alt) or float(alt) <= 0:
                    return "invalid altitude"
                if not self._finite_number(speed) or float(speed) < -1:
                    return "invalid speed"
                if not self._finite_number(hold) or float(hold) < 0:
                    return "invalid hold"
        return None

    def _apply(self, command: dict) -> None:
        name = command.get("command")
        if name == "set_speed":
            self.speed_mps = float(command.get("value"))
        elif name == "set_altitude":
            self.altitude_m = float(command.get("value"))
        elif name == "hold":
            self.holding = True
            self.mode = "SIM-EMU-HOLD"
        elif name == "rtl":
            self.mode = "SIM-EMU-RTL"
        elif name == "emergency_stop":
            self.mode = "SIM-EMU-ESTOP"
            self.speed_mps = 0.0
        elif name == "upload_route":
            self.route = command.get("route")
            self.mission_started = False
        elif name == "start_mission":
            self.mission_started = True
            self.holding = False
            self.mode = "SIM-EMU-AUTO"

    def _remember(self, command_id: str, fingerprint: str, status: str, message: str) -> None:
        self.seen[command_id] = {
            "fingerprint": fingerprint,
            "status": status,
            "message": message,
        }
        self.seen.move_to_end(command_id)
        while len(self.seen) > MAX_SEEN:
            self.seen.popitem(last=False)

    def _fingerprint(self, command: dict) -> str:
        return json.dumps(command, sort_keys=True, separators=(",", ":"))

    def _finite_number(self, value) -> bool:
        if isinstance(value, bool) or not isinstance(value, (int, float)):
            return False
        return math.isfinite(value)

    def _ack(self, command: dict, addr: Tuple[str, int], status: str, message: str) -> Optional[str]:
        if self.drop_acks:
            return None
        if self.delay_s > 0:
            time.sleep(self.delay_s)
        ack = {
            "schemaVersion": "1.0",
            "type": "command_ack",
            "commandId": command.get("commandId"),
            "vehicleType": self.vehicle,
            "sourceId": self.source,
            "timestampMs": self._now_ms(),
            "authToken": self.token,
            "status": status,
            "message": message,
        }
        payload = json.dumps(ack)
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        # Contract: ACK returns to the ground-station listen port, not the ephemeral sender.
        sock.sendto(payload.encode("utf-8"), (self.host, self.telemetry_port))
        sock.close()
        return payload


def main(argv: Optional[list] = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--command-port", type=int, default=19092)
    parser.add_argument("--telemetry-port", type=int, default=19090)
    parser.add_argument("--telemetry-hz", type=float, default=2.0,
                        help="Simulated telemetry rate. 0 disables the publisher.")
    parser.add_argument("--token", default="test-token")
    parser.add_argument("--vehicle", default="uav")
    parser.add_argument("--source", default="sim-emu-uav",
                        help="Simulated telemetry source id. Not a real vehicle identity.")
    parser.add_argument("--drop-telemetry", action="store_true",
                        help="Inject telemetry loss. Off by default.")
    parser.add_argument("--telemetry-delay-ms", type=float, default=0,
                        help="Inject telemetry delay in milliseconds. 0 disables delay.")
    args = parser.parse_args(argv)
    emu = VehicleEmulator(args.host, args.command_port, args.telemetry_port, args.token, args.vehicle, args.source)
    emu.drop_telemetry = bool(args.drop_telemetry)
    emu.telemetry_delay_s = max(0.0, args.telemetry_delay_ms) / 1000.0
    emu.start(telemetry_hz=args.telemetry_hz)
    print("loopback emulator listening on %s:%s" % (args.host, emu.command_port), flush=True)
    print("simulated source %s publishing telemetry to %s:%s at %s Hz" % (
        args.source, args.host, args.telemetry_port, args.telemetry_hz), flush=True)
    try:
        while emu.running:
            time.sleep(0.2)
    except KeyboardInterrupt:
        pass
    finally:
        emu.stop()
    return 0


if __name__ == "__main__":
    sys.exit(main())
