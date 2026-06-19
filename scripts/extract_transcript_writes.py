import json
import os
import glob
import sys

base = sys.argv[1] if len(sys.argv) > 1 else "."
out_dir = sys.argv[2] if len(sys.argv) > 2 else "."
targets = set(sys.argv[3:]) if len(sys.argv) > 3 else None

found = {}
for fp in glob.glob(os.path.join(base, "**", "*.jsonl"), recursive=True):
    with open(fp, encoding="utf-8") as f:
        for line in f:
            try:
                obj = json.loads(line)
            except json.JSONDecodeError:
                continue
            for block in obj.get("message", {}).get("content") or []:
                if not isinstance(block, dict) or block.get("name") != "Write":
                    continue
                inp = block.get("input", {})
                path = inp.get("path", "")
                name = os.path.basename(path)
                if targets and name not in targets:
                    continue
                found[name] = (path, inp.get("contents", ""))

os.makedirs(out_dir, exist_ok=True)
for name, (path, contents) in found.items():
    out_path = os.path.join(out_dir, name)
    with open(out_path, "w", encoding="utf-8") as out:
        out.write(contents)
    print(f"Wrote {out_path} ({len(contents)} chars)")
