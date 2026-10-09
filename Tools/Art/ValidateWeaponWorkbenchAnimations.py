"""Standalone GLB validation: clip contract, endpoint poses, articulation, and unchanged approved sources."""
import hashlib, json, math, struct
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]
reports = []
errors = []
for path in sorted((ROOT / "Resources/Weapons").glob("*.manifest.json")):
    manifest = json.loads(path.read_text())
    if "workbench" not in manifest: continue
    data = path.with_suffix("").with_suffix(".glb").read_bytes()
    size = struct.unpack_from("<I", data, 12)[0]
    doc = json.loads(data[20:20+size]); offset = 28+size
    binary = data[offset:]
    def values(index):
        acc = doc["accessors"][index]; view = doc["bufferViews"][acc["bufferView"]]
        components = {"SCALAR": 1, "VEC3": 3, "VEC4": 4}[acc["type"]]
        assert acc["componentType"] == 5126
        start = view.get("byteOffset", 0)+acc.get("byteOffset", 0)
        stride = view.get("byteStride", components*4)
        return [struct.unpack_from("<"+"f"*components, binary, start+i*stride) for i in range(acc["count"])]
    for clip in doc.get("animations", []):
        if clip["name"] == "Fire": continue
        expected = .7 if clip["name"] in ("Disassemble", "Assemble") else .4
        end = max(values(s["input"])[-1][0] for s in clip["samplers"])
        if abs(end-expected) > .0001: errors.append([path.name, clip["name"], "duration", end])
        for channel in clip["channels"]:
            sampler = clip["samplers"][channel["sampler"]]
            samples = values(sampler["output"])
            if not all(math.isfinite(v) for sample in samples for v in sample): errors.append([path.name, "nonfinite"])
            node = doc["nodes"][channel["target"]["node"]]
            if node["name"] == "GunRoot": errors.append([path.name, "root animated"])
            if clip["name"] == "Assemble" or clip["name"].startswith("SocketSeat"):
                kind = channel["target"]["path"]
                defaults = {"translation": (0,0,0), "rotation": (0,0,0,1), "scale": (1,1,1)}
                rest = node.get(kind, defaults[kind])
                actual = samples[-1]
                deviation = max(abs(a-b) for a,b in zip(actual, rest))
                if kind == "rotation": deviation = min(deviation, max(abs(a+b) for a,b in zip(actual, rest)))
                if deviation > .0001: errors.append([path.name, clip["name"], node["name"], kind, deviation])
    source = ROOT / manifest["source"] if "source" in manifest else ROOT / "Art/WeaponConcepts/SquareHandgun/SquareHandgun.blend"
    if hashlib.sha256(source.read_bytes()).hexdigest() != manifest["workbench"]["approved_source_sha256"]: errors.append([path.name, "approved source changed"])
    reports.append({"asset": path.name, "bytes": len(data), "clips": len(doc.get("animations", []))})
report = {"assets": reports, "total_glb_bytes": sum(row["bytes"] for row in reports), "errors": errors}
(ROOT / ".tmp/weapon-workbench-animation/validation.json").write_text(json.dumps(report, indent=2))
print(json.dumps(report, indent=2))
assert len(reports) == 16, len(reports)
assert not errors, f"{len(errors)} validation errors"
