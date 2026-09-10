"""Loopback vehicle emulator for the ground-station JSON/UDP command contract.

Default bind and telemetry targets are 127.0.0.1 only. This is not flight-controller
firmware and does not prove hardware command execution.
"""
from __future__ import annotations

import argparse
import json
import socket
import sys
import threading
import time
from typing import Dict, Optional, Tuple


ALLOWED_COMMANDS = {
    "hold",
    "rtl",
    "emergency_stop",
    "set_speed",
    "set_altitude",
    "upload_route",
    "start_mission",
}


class VehicleEmulator:
    def __init__(self, host: str, command_port: int, telemetry_port: int, token: str, vehicle: str, source: str):
        if host not in ("127.0.0.1", "::1"):
            raise ValueError("Default emulator binds only to loopback")
        self.host = host
        self.command_port = command_port
        self.telemetry_port = telemetry_port
        self.token = token
        self.vehicle = vehicle
        self.source = source
        self.delay_s = 0.0
        self.drop_acks = False
        self.force_reject = False
        self.apply_count: Dict[str, int] = {}
        self.seen: Dict[str, str] = {}
        self.last_command: Optional[dict] = None
        self.running = False
        self._sock: Optional[socket.socket] = None
        self._lock = threading.Lock()

    def start(self) -> None:
        self._sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self._sock.bind((self.host, self.command_port))
        self.command_port = self._sock.getsockname()[1]
        self._sock.settimeout(0.2)
        self.running = True
        threading.Thread(target=self._listen, daemon=True).start()

    def stop(self) -> None:
        self.running = False
        if self._sock is not None:
            try:
                self._sock.close()
            except OSError:
                pass
            self._sock = None

    def send_telemetry(self, battery: float = 80.0, altitude: float = 35.0, sequence: int = 1) -> None:
        now_ms = int(time.time() * 1000)
        message = {
            "schemaVersion": "1.0",
            "authToken": self.token,
            "sourceId": self.source,
            "vehicleType": self.vehicle,
            "sequenceId": sequence,
            "timestampMs": now_ms,
            "telemetry": {
                "altitudeM": altitude,
                "speedMps": 4.0,
                "mode": "AUTO",
                "batteryPercent": battery,
            },
            "pose": {"latitude": 41.3043, "longitude": -81.7524, "altitudeM": altitude, "yawDeg": 0},
        }
        sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        sock.sendto(json.dumps(message).encode("utf-8"), (self.host, self.telemetry_port))
        sock.close()

    def _listen(self) -> None:
        while self.running and self._sock is not None:
            try:
                data, addr = self._sock.recvfrom(65535)
            except socket.timeout:
                continue
            except OSError:
                return
            if addr[0] not in ("127.0.0.1", "::1"):
                continue
            try:
                command = json.loads(data.decode("utf-8"))
            except (UnicodeDecodeError, json.JSONDecodeError):
                continue
            self.handle_command(command, addr)

    def handle_command(self, command: dict, addr: Tuple[str, int]) -> Optional[str]:
        command_id = command.get("commandId")
        name = command.get("command")
        fingerprint = json.dumps(command, sort_keys=True)
        with self._lock:
            self.last_command = command
            if not isinstance(command_id, str) or not command_id:
                return self._ack(command, addr, "rejected", "commandId missing")
            if command.get("schemaVersion") != "1.0" or command.get("type") != "command":
                return self._ack(command, addr, "rejected", "unsupported envelope")
            if command.get("authToken") != self.token:
                return self._ack(command, addr, "rejected", "auth")
            if command.get("vehicleType") != self.vehicle or command.get("targetSourceId") != self.source:
                return self._ack(command, addr, "rejected", "wrong vehicle")
            if name not in ALLOWED_COMMANDS:
                return self._ack(command, addr, "rejected", "unsupported command")
            previous = self.seen.get(command_id)
            if previous is not None:
                if previous != fingerprint:
                    return self._ack(command, addr, "rejected", "commandId reused with different body")
                status = "rejected" if self.force_reject else "applied"
                return self._ack(command, addr, status, "duplicate")
            self.seen[command_id] = fingerprint
            if self.force_reject:
                return self._ack(command, addr, "rejected", "forced")
            accepted = self._ack(command, addr, "accepted", "")
            self.apply_count[command_id] = self.apply_count.get(command_id, 0) + 1
            applied = self._ack(command, addr, "applied", "")
            return applied or accepted

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
            "timestampMs": int(time.time() * 1000),
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
    parser.add_argument("--token", default="test-token")
    parser.add_argument("--vehicle", default="uav")
    parser.add_argument("--source", default="emu-uav")
    args = parser.parse_args(argv)
    emu = VehicleEmulator(args.host, args.command_port, args.telemetry_port, args.token, args.vehicle, args.source)
    emu.start()
    print("loopback emulator listening on %s:%s" % (args.host, args.command_port), flush=True)
    try:
        while True:
            time.sleep(1)
    except KeyboardInterrupt:
        emu.stop()
    return 0


if __name__ == "__main__":
    sys.exit(main())
