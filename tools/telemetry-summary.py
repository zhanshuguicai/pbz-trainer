"""Summarize a PBZSuite telemetry-*.jsonl file: continuity, intervals, and a log of value changes.

Usage: python -I tools/telemetry-summary.py <telemetry.jsonl> [max_change_lines]
"""
import json
import sys
from collections import Counter


def key(r):
    h, c, f = r.get("health") or {}, r.get("combat") or {}, r.get("flask") or {}
    top, bottom = f.get("top") or {}, f.get("bottom") or {}
    return (
        f"{r.get('lifecycle')} gen={r.get('playerGeneration')} "
        f"hp={h.get('current')}/{h.get('max')} v={h.get('valid')} "
        f"comb={c.get('inCombat')} dead={c.get('dead')} "
        f"flask={top.get('id')}:{top.get('count')}+{bottom.get('id')}:{bottom.get('count')}={f.get('total')} v={f.get('valid')}"
    )


def main(path, limit):
    records, bad = [], 0
    with open(path, encoding="utf-8") as fh:
        for line in fh:
            try:
                records.append(json.loads(line))
            except json.JSONDecodeError:
                bad += 1
    if not records:
        print("no records")
        return
    gaps = sum(1 for a, b in zip(records, records[1:]) if b["sequence"] != a["sequence"] + 1)
    intervals = [b["monotonicMs"] - a["monotonicMs"] for a, b in zip(records, records[1:])]
    span = (records[-1]["monotonicMs"] - records[0]["monotonicMs"]) / 60000
    print(f"records={len(records)} unparsable={bad} seq {records[0]['sequence']}..{records[-1]['sequence']} gaps={gaps} span={span:.1f} min")
    if intervals:
        srt = sorted(intervals)
        print(f"interval ms: median={srt[len(srt) // 2]} p99={srt[int(len(srt) * 0.99)]} max={srt[-1]}")
    print("lifecycle:", dict(Counter(r.get("lifecycle") for r in records)))
    print("records with errors:", sum(1 for r in records if r.get("errors")))
    print("config revisions:", sorted({r.get("configRevision") for r in records}))
    prev, shown = None, 0
    for r in records:
        k = key(r)
        if k != prev:
            print(f"{r['sequence']:>7} t={r['monotonicMs'] / 1000:8.1f} {k}")
            prev = k
            shown += 1
            if shown >= limit:
                print("... truncated")
                break


if __name__ == "__main__":
    main(sys.argv[1], int(sys.argv[2]) if len(sys.argv) > 2 else 200)
