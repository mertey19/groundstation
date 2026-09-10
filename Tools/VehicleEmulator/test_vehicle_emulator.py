import json
import socket
import time
import unittest

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


class VehicleEmulatorTests(unittest.TestCase):
    def setUp(self):
        self.acks = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        self.acks.bind(("127.0.0.1", 0))
        self.acks.settimeout(1.5)
        port = self.acks.getsockname()[1]
        self.emu = VehicleEmulator("127.0.0.1", 0, port, "test-token", "uav", "emu-uav")
        self.emu.start()
        # Rebind the emulator to an ephemeral command port after start() used 0.
        command_port = self.emu._sock.getsockname()[1]
        self.command_port = command_port
        self.tx = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)

    def tearDown(self):
        self.emu.stop()
        self.acks.close()
        self.tx.close()

    def send(self, body):
        self.tx.sendto(json.dumps(body).encode("utf-8"), ("127.0.0.1", self.command_port))

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

    def test_unsupported_command_is_not_success(self):
        self.send(command("c3", extra={"command": "arm_motors"}))
        ack = self.read_ack()
        self.assertEqual(ack["status"], "rejected")
        self.assertNotIn("arm_motors", ALLOWED_COMMANDS)

    def test_wrong_token_rejected(self):
        self.send(command("c4", token="other"))
        self.assertEqual(self.read_ack()["status"], "rejected")

    def test_non_loopback_host_rejected(self):
        with self.assertRaises(ValueError):
            VehicleEmulator("192.0.2.10", 19092, 19090, "test-token", "uav", "emu-uav")


if __name__ == "__main__":
    unittest.main()
