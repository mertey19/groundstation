# Ground station fix verification

Inspection baseline: `a15c4b0` (`Rover planini geometrik dogrula; komut ve kayit dayanikliligi`).  
Current HEAD is still `a15c4b0`. The five fixes are in the working tree and are **not committed**.

Unity Editor: `2022.3.62f3`.  
This pass did not change Unity or package versions, `.meta` GUIDs, or scene/prefab links.

---

## Findings vs current HEAD

The review findings were treated as hypotheses and re-checked on `a15c4b0` before editing. **None were already fixed.** Regression coverage for the previous round was kept and extended.

| ID | Hypothesis at `a15c4b0` | Status |
| --- | --- | --- |
| P0-1 | `Fingerprint()` / `BuildRoute()` compared only lat/lon/alt. Speed, hold, action, vehicle/source, and altitude reference could change while a pending `upload_route` ACK still started the mission. | **Confirmed, then fixed.** |
| P0-2 | Mission engine passed the circular fence to the planner as `center ± radius` axis-aligned bounds. Direct-path short-circuit used that box, so `(8,8)` inside a 10 m square but outside the 10 m disk was accepted. | **Confirmed, then fixed.** |
| P1-3 | In-memory list capped at 500. After `StopRecording()`, `SaveRecording()` could write a new file from RAM and retarget `_lastSavedPath`. Size limit stopped recording instead of rotating. | **Confirmed, then fixed.** |
| P1-4 | Emulator accepted empty routes and negative speeds, did not reject bad timestamps, replayed `applied` for a previously rejected id, and died on `[]`. | **Confirmed, then fixed.** |
| P1-5 | `send_telemetry()` existed; `main()` only slept and never published. | **Confirmed, then fixed.** |

Polygon geofence does **not** exist in this repo (`DigitalTwinGeofence` is circular). No polygon planner was added.

---

## What changed

| File | Why |
| --- | --- |
| `Assets/Scripts/DigitalTwin/DigitalTwinCommandEgress.cs` | Immutable mission snapshot (vehicle, source, altitude reference, waypoint order/count, lat/lon/alt bits, speed, hold, action). Transport fields (`commandId`, retry time, token) excluded. Any observed live edit sets a one-way `contentDiverged` flag, so reverting values does not revive auto-start. `applied` re-checks snapshot, endpoint, source, and cancel before `start_mission`. Retry still uses the same id and bytes. `accepted` still does not start. |
| `Assets/Scripts/DigitalTwin/RoverLocalPlanner.cs` | Bounding box remains a search window only. All paths, including the direct short-circuit and string-pull, use the circle. Start/goal/vertices/segments validated; effective radius is `fence − rover − margin`. Invalid radius or start outside fails closed with an empty path. |
| `Assets/Scripts/DigitalTwin/DigitalTwinGeofence.cs` | `Revision` increments on `SetFence`. `TryGetCircle()` fails closed when the fence is missing or invalid. |
| `Assets/Scripts/DigitalTwin/DigitalTwinMissionEngine.cs` | Passes the real circle plus a search box; stamps and re-checks fence revision after `Plan()`; does not keep a previous detour when planning fails. |
| `Assets/Scripts/DigitalTwin/DigitalTwinOperationRecorder.cs` | Disk session is the source of truth. RAM stays a 500-line preview. `StopRecording` drains the queue. `SaveRecording` finalizes or returns the session path; it never dumps RAM as a full session. Size limit rotates `*_pNNN.jsonl` parts with a `.session.json` manifest. `_lastSavedPath` updates only after a successful session start/finalize. Closed/disposed writers no longer throw on Stop/Save. |
| `Assets/Scripts/DigitalTwin/Editor/GroundStationRegressionChecks.cs` | Route-snapshot, circular-geofence, and recorder regressions added. Existing command/planner/recorder checks kept. |
| `Tools/VehicleEmulator/vehicle_emulator.py` | Dict-only payloads, command-specific validation, injectable clock, stored terminal results, listener isolation, optional telemetry thread, simulated source/mode. |
| `Tools/VehicleEmulator/test_vehicle_emulator.py` | Original five tests kept. Negative cases and a real CLI subprocess test added. |
| `KOMUT-PROTOKOLU.md` | Snapshot, recorder rotation, emulator CLI, and cache policy documented. The example command is the one that was actually run. |

---

## Commands that were run

From `C:\Users\mert\Downloads\groundstation-main\groundstation-main`:

```text
python Tools/VehicleEmulator/test_vehicle_emulator.py
```

```text
python Tools/VehicleEmulator/vehicle_emulator.py --host 127.0.0.1 --command-port 19092 --telemetry-port 19090 --telemetry-hz 2 --vehicle uav --source sim-emu-uav --token test-token
```

```text
"C:\Program Files\Unity\Hub\Editor\2022.3.62f3\Editor\Unity.exe" -batchmode -nographics -projectPath "C:\Users\mert\Downloads\groundstation-main\groundstation-main" -executeMethod GroundStationRegressionChecks.Run -quit -logFile "C:\Users\mert\Downloads\groundstation-main\groundstation-main\Logs\groundstation-regression-unity.log"
```

The Unity Hub/`Unity.exe` launcher can return in a few seconds while the editor is still compiling. The result used here is `Logs/groundstation-regression-checks.txt` plus the editor log, not the launcher process exit.

---

## Test added / test run / test passed

### Python emulator (isolated loopback)

- **Test added:** empty `upload_route`; negative `set_speed`; injected-clock timestamps; rejected-id retry; `[]` / `null` / number / string / broken JSON after which a valid command still applies; NaN/missing route; concurrent same-id; CLI subprocess telemetry + ACK + process exit. The original five tests remain.
- **Test run:** `python Tools/VehicleEmulator/test_vehicle_emulator.py` → **13 tests**.
- **Test passed:** **13/13**.

This is the emulator’s own contract test. It is **not** a Unity–vehicle integration test and **not** flight-controller or hardware evidence.

Documented CLI command: **run**. A local UDP socket on `127.0.0.1:19090` received simulated telemetry (`sourceId=sim-emu-uav`, `mode=SIM-EMU`, `"simulated": true`) from `127.0.0.1`. The process was then killed (Windows `Kill` exit `-1`). That is not a Ctrl+C proof on Windows; the CLI test uses `terminate()` after ACKs.

### Unity editor batch (not Play Mode)

- **Test added:** speed/hold/action/order/count edits; revert-after-edit; unchanged mission still starts; `accepted` does not start; late ACK after timeout does not start; circle vs box `(0,0)` r=10 goal `(8,8)`; near inside/outside; vehicle-center radius; start outside; detour that would leave the circle; invalid radius; cell-size metre stability; fence revision; 1200-record Stop/Save/reload; save-while-recording finalizes disk; repeated Stop/Save; small-limit rotation; truncated last line (existing); visible IO error; token masking on parts.
- **Test run:** `GroundStationRegressionChecks.Run` in batchmode / nographics.
- **Test passed:** **169** ground-station checks **and** **14** HUD checks (`Logs/groundstation-regression-checks.txt`: `PASS: 169 ground-station checks`; editor log: `[GroundStationRegressionChecks] PASS: 169 checks`).

**Unity Play Mode was not run.** Do not read the editor-batch result as a Play Mode pass.

**Hardware was not connected. Donanımda doğrulanmadı.**

---

## Remaining risks

- Crash / power loss can still drop the last unflushed JSONL line. Controlled `StopRecording` drains the queue; that is not the same guarantee.
- `StopRecording`/`SaveRecording` may join the writer for up to 8 s. Record/flush stay off the main thread; stop is a bounded wait.
- Fence revision is re-checked after synchronous `Plan()`. A mid-search revision race is unlikely on the Unity main thread.
- `DigitalTwinGeofence.SetFence` still clamps inspector radius to at least 10 m. Planner tests pass the circle in metres directly.
- `GeoFrames` is a coordinate helper, not a finished GPS–SLAM alignment.
- Token checks are shared-secret matching, not encrypted transport.
- Default emulator bind remains loopback. Do not point it at a real vehicle.

Other gaps noticed, not expanded in this round: no polygon geofence; no MAVLink adapter; command ACK path is still JSON/UDP to a vehicle computer that this repo does not ship.

---

## Local reproduction

1. Work only in `groundstation-main`. Do not commit unless asked.
2. `python Tools/VehicleEmulator/test_vehicle_emulator.py`
3. Optional live CLI (loopback only): the documented `vehicle_emulator.py` command above, with a listener on UDP `19090`.
4. Unity batch: the `Unity.exe` command above, then read `Logs/groundstation-regression-checks.txt` (wait until it is rewritten; do not trust the launcher’s immediate exit).
5. Expect `PASS: 169 ground-station checks` plus HUD `PASS (14)` if this tree is unchanged.
