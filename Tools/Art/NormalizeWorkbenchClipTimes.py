"""Normalize already-authored workbench tracks to zero without touching Fire or geometry."""
import json, struct
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
for path in (ROOT / "Resources/Weapons").glob("*.glb"):
    raw = path.read_bytes(); size = struct.unpack_from("<I", raw, 12)[0]
    doc = json.loads(raw[20:20+size]); binary = bytearray(raw[28+size:])
    tracks = [a for a in doc.get("animations", []) if a["name"] != "Fire"]
    if not tracks: continue
    inputs = {s["input"] for a in tracks for s in a["samplers"]}
    fire_inputs = {s["input"] for a in doc.get("animations", []) if a["name"] == "Fire" for s in a["samplers"]}
    assert not inputs & fire_inputs
    for index in inputs:
        acc = doc["accessors"][index]; view = doc["bufferViews"][acc["bufferView"]]
        origin = acc["min"][0]
        if origin == 0: continue
        start = view.get("byteOffset", 0)+acc.get("byteOffset", 0)
        for i in range(acc["count"]):
            offset = start+i*view.get("byteStride", 4)
            value = struct.unpack_from("<f", binary, offset)[0]
            struct.pack_into("<f", binary, offset, max(0, value-origin))
        acc["min"] = [0.0]; acc["max"] = [acc["max"][0]-origin]
    payload = json.dumps(doc, separators=(",", ":")).encode(); payload += b" "*(-len(payload)%4)
    path.write_bytes(struct.pack("<III", 0x46546c67, 2, 28+len(payload)+len(binary)) +
                     struct.pack("<II", len(payload), 0x4e4f534a)+payload+
                     struct.pack("<II", len(binary), 0x004e4942)+binary)
    manifest_path = path.with_suffix(".manifest.json")
    manifest = json.loads(manifest_path.read_text())
    for clip in manifest["workbench"]["clips"]:
        clip["duration"] = .7 if clip["name"] in ("Disassemble", "Assemble") else .4
    manifest["bytes"] = path.stat().st_size
    manifest_path.write_text(json.dumps(manifest, indent=2))
print("WORKBENCH_TIMES_NORMALIZED")
