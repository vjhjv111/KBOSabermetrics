"""Archive the public WPA Inquirer table. Never run as a server startup task.

Usage: python collect_fangraphs_we.py OUTPUT_DIRECTORY
The JSONL is resumable. Stop on HTTP errors, including 429, and respect Retry-After.
"""
import datetime
import hashlib
import json
import pathlib
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

root = pathlib.Path(sys.argv[1])
root.mkdir(parents=True, exist_ok=True)
cache = root / "responses.jsonl"
states = [(i, side, outs, bases, lead) for i in range(1, 10) for side in ("H", "A")
          for outs in range(3) for bases in range(8) for lead in range(-10, 11)]
def key(s):
    return ":".join(map(str, s))
known = {}
if cache.exists():
    for line in cache.read_text(encoding="utf-8").splitlines():
        entry = json.loads(line)
        known.update(entry["states"])
pending = [s for s in states if key(s) not in known]
print(f"Resuming {len(known)}/{len(states)} states", flush=True)
next_request = time.monotonic()
with cache.open("a", encoding="utf-8") as output:
    for i in range(0, len(pending), 2):
        pair = pending[i:i+2]
        if len(pair) == 1:
            pair.append(pair[0])
        query = []
        for s, suffix in zip(pair, ("", "2")):
            inning, side, outs, bases, lead = s
            query.extend((k+suffix, str(v)) for k, v in dict(runEnv="4.5", inning=f"{inning}.{side}",
                         outs=outs, runners=bases+1, runs=lead).items())
        url = "https://www.fangraphs.com/api/tools/wpa-inquirer/data?" + urllib.parse.urlencode(query)
        time.sleep(max(0, next_request-time.monotonic()))
        next_request = time.monotonic()+1.05
        try:
            with urllib.request.urlopen(url, timeout=40) as response:
                data = json.load(response)
        except urllib.error.HTTPError as error:
            print(f"Stopped: HTTP {error.code}; Retry-After={error.headers.get('Retry-After', 'unspecified')}", flush=True)
            raise
        rows = {key(s): data[part] for s, part in zip(pair, ("before", "after"))}
        for row in rows.values():
            if (not isinstance(row, dict) or not isinstance(row.get("weh"), (int, float)) or
                not isinstance(row.get("wea"), (int, float)) or not 0 <= row["weh"] <= 1 or
                not 0 <= row["wea"] <= 1 or abs(row["weh"]+row["wea"]-1) > 1e-6):
                raise ValueError((url, row))
        entry = dict(url=url, retrievedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(), states=rows)
        output.write(json.dumps(entry, separators=(",", ":"))+"\n")
        output.flush()
        known.update(rows)
        if i % 100 == 0:
            print(f"{len(known)}/{len(states)} states", flush=True)
document = dict(source="https://www.fangraphs.com/tools/wpa-inquirer", runEnvironment=4.5,
                retrievedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                states={key(s): known[key(s)]["weh"] for s in states})
payload = json.dumps(document, separators=(",", ":")).encode()
(root / "fangraphs-we-4.5.json").write_bytes(payload)
print("Complete SHA256:", hashlib.sha256(payload).hexdigest())
