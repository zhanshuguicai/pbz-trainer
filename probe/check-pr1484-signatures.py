"""Bounded, read-only static validation of the two reviewed PR signatures."""
import hashlib
import json
from pathlib import Path
import re
import struct

ROOT = Path(__file__).resolve().parents[1]
EXE = Path(r"C:\Program Files (x86)\Steam\steamapps\common\PBZ\platform\steam\PBZ.exe")
data = EXE.read_bytes()
pe = struct.unpack_from("<I", data, 60)[0]
section_start = pe + 24 + struct.unpack_from("<H", data, pe + 20)[0]
section_count = struct.unpack_from("<H", data, pe + 6)[0]
sections = []
for offset in range(section_start, section_start + 40 * section_count, 40):
    sections.append(dict(
        name=data[offset:offset+8].rstrip(b"\0").decode(),
        rva=struct.unpack_from("<I", data, offset+12)[0],
        virtual_size=struct.unpack_from("<I", data, offset+8)[0],
        raw=struct.unpack_from("<I", data, offset+20)[0],
        size=struct.unpack_from("<I", data, offset+16)[0],
        flags=struct.unpack_from("<I", data, offset+36)[0],
    ))
results = []
for name in ("GUObjectArray", "GNatives"):
    source = (ROOT / "probe/pr1484-reviewed/UE4SS_Signatures" / (name + ".lua")).read_text()
    pattern = re.search(r'return "([0-9A-F? ]+)"', source).group(1)
    regex = b"".join(b"." if "?" in part else re.escape(bytes.fromhex(part)) for part in pattern.split())
    matches = list(re.finditer(regex, data, re.S))
    result = dict(symbol=name, staticFileMatchCount=len(matches), matches=[])
    for match in matches:
        section = next(s for s in sections if s["raw"] <= match.start() < s["raw"] + s["size"])
        rva = match.start() - section["raw"] + section["rva"]
        instruction = 0 if name == "GUObjectArray" else 0x15
        displacement = struct.unpack_from("<i", data, match.start() + instruction + 3)[0]
        target = rva + instruction + 7 + displacement - (0x10 if name == "GUObjectArray" else 0)
        target_section = next((s for s in sections if s["rva"] <= target < s["rva"] + max(s["size"],s["virtual_size"])), None)
        result["matches"].append(dict(rva=hex(rva), resolvedTargetRVA=hex(target),
            targetSection=target_section["name"] if target_section else None,
            targetWritable=bool(target_section and target_section["flags"] & 0x80000000)))
    results.append(result)
(ROOT / "evidence/pr1484-static-signature-check.json").write_text(json.dumps(results, indent=2), encoding="utf-8")
print(json.dumps(results, indent=2))
