"""Collect public KBO profiles independently of game imports (Python 3.10+, stdlib)."""
from __future__ import annotations

import argparse
from datetime import datetime, timedelta, timezone
from html.parser import HTMLParser
import json
from pathlib import Path
import re
import sqlite3
import time
from urllib.error import HTTPError, URLError
from urllib.parse import parse_qs, urljoin, urlparse
from urllib.request import Request, urlopen

BASE = "https://www.koreabaseball.com"
PHOTO_HOSTS = {"6ptotvmi5753.edge.naverncp.com", "www.koreabaseball.com", "eng.koreabaseball.com"}
MAX_IMAGE = 5 * 1024 * 1024
COLUMNS = ["Pcode", "Name", "TeamName", "UniformNumber", "BirthDate", "Position", "BatsThrows",
           "HeightCm", "WeightKg", "Career", "SigningBonusText", "SalaryText", "DraftText",
           "EntryYearText", "ProfileSeason", "SourceUrl", "PhotoSourceUrl", "PhotoFileName",
           "FetchedUtc", "LastCheckedUtc"]
SCHEMA = "CREATE TABLE IF NOT EXISTS OfficialPlayerProfiles (" + ",".join(
    f'{c} ' + ("TEXT PRIMARY KEY NOT NULL" if c == "Pcode" else "INTEGER" if c in
               {"HeightCm", "WeightKg", "ProfileSeason"} else "TEXT") for c in COLUMNS) + ")"


def clean(value):
    value = re.sub(r"\s+", " ", value or "").strip()
    return None if value in {"", "-", "--", "미상"} else value


class ProfileHtml(HTMLParser):
    def __init__(self):
        super().__init__(convert_charrefs=True)
        self.fields, self.stack, self.photo, self.ids = {}, [], None, set()

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        ident = attrs.get("id", "")
        key = ident.split("_playerProfile_")[-1] if "_playerProfile_lbl" in ident else None
        if ident == "h4Team":
            key = "team"
        if tag not in {"img", "input", "br", "hr", "meta", "link"}:
            self.stack.append((tag, key))
        if key:
            self.fields.setdefault(key, "")
        if tag == "img" and "playerProfile_img" in ident:
            self.photo = attrs.get("src")
        if tag == "a":
            ids = parse_qs(urlparse(attrs.get("href", "")).query).get("playerId", [])
            self.ids.update(ids)

    def handle_endtag(self, tag):
        for i in range(len(self.stack) - 1, -1, -1):
            if self.stack[i][0] == tag:
                del self.stack[i:]
                break

    def handle_data(self, value):
        for _, key in self.stack:
            if key:
                self.fields[key] += value


def photo_url(raw, code):
    if not raw:
        return None
    url = urljoin(BASE, raw)
    p = urlparse(url)
    if (p.scheme != "https" or p.hostname not in PHOTO_HOSTS or p.username or p.password
            or p.port not in (None, 443) or p.query or p.fragment):
        return None
    # Only a player portrait explicitly present on the official page. Never guess paths.
    if not re.fullmatch(rf"/KBO_IMAGE/person/(?:middle|small|big)/\d{{4}}/{re.escape(code)}\.(?:jpg|jpeg|png)", p.path, re.I):
        return None
    return url


def parse_profile(html, code, source_url):
    parser = ProfileHtml()
    parser.feed(html)
    f = {key: clean(value) for key, value in parser.fields.items()}
    if not f.get("lblName"):
        raise ValueError("공식 선수 프로필이 없거나 페이지 형식이 달라졌습니다")
    required = {"lblBackNo", "lblBirthday", "lblPosition", "lblHeightWeight", "lblCareer",
                "lblPayment", "lblSalary", "lblDraft", "lblJoinInfo"}
    if not required.issubset(f):
        raise ValueError("프로필 항목 구조가 달라졌습니다. 기존 정보를 유지합니다")
    photo = photo_url(parser.photo, code)
    if code not in parser.ids and photo is None:
        raise ValueError("응답에서 요청한 선수 ID를 확인하지 못했습니다")
    raw_photo_id = re.search(r"/person/[^/]+/\d{4}/(\d+)\.", parser.photo or "")
    if raw_photo_id and raw_photo_id[1] != code:
        raise ValueError("공식 사진의 선수 ID가 요청과 다릅니다")
    birth = f.get("lblBirthday")
    m = re.fullmatch(r"(\d{4})년\s*(\d{1,2})월\s*(\d{1,2})일", birth or "")
    birth = datetime(*map(int, m.groups())).date().isoformat() if m else None
    position = f.get("lblPosition")
    hand = re.search(r"\(([^()]+)\)", position or "")
    size = f.get("lblHeightWeight") or ""
    height, weight = re.search(r"(\d+)\s*cm", size), re.search(r"(\d+)\s*kg", size)
    return dict(Pcode=code, Name=f["lblName"], TeamName=f.get("team"), UniformNumber=f.get("lblBackNo"),
                BirthDate=birth, Position=clean(re.sub(r"\([^()]*\)", "", position or "")),
                BatsThrows=hand[1] if hand else None, HeightCm=int(height[1]) if height else None,
                WeightKg=int(weight[1]) if weight else None, Career=f.get("lblCareer"),
                SigningBonusText=f.get("lblPayment"), SalaryText=f.get("lblSalary"),
                DraftText=f.get("lblDraft"), EntryYearText=f.get("lblJoinInfo"),
                # The statistics heading/photo directory does not establish a salary year.
                ProfileSeason=None, SourceUrl=source_url, PhotoSourceUrl=photo)


class Fetcher:
    def __init__(self, delay=0.5):
        self.delay, self.last = max(0.25, delay), 0.0

    def get(self, url, limit, is_photo=False):
        for attempt in range(3):
            time.sleep(max(0, self.delay - (time.monotonic() - self.last)))
            self.last = time.monotonic()
            try:
                req = Request(url, headers={"User-Agent": "KBO-Sabermetrics-ProfileCollector/1.0", "Referer": BASE + "/"})
                with urlopen(req, timeout=20) as response:
                    final = urlparse(response.url)
                    hosts = PHOTO_HOSTS if is_photo else {"www.koreabaseball.com"}
                    if final.scheme != "https" or final.hostname not in hosts:
                        raise ValueError("허용되지 않은 호스트로 이동했습니다")
                    if is_photo and response.url != url:
                        raise ValueError("사진 주소가 다른 주소로 이동했습니다")
                    body = response.read(limit + 1)
                    if len(body) > limit:
                        raise ValueError("응답 크기 제한을 초과했습니다")
                    return body
            except HTTPError as error:
                if error.code not in {429, 500, 502, 503, 504} or attempt == 2:
                    raise
            except (TimeoutError, URLError):
                if attempt == 2:
                    raise
            time.sleep(2 ** (attempt + 1))


def image_extension(data):
    if data.startswith(b"\xff\xd8\xff") and len(data) > 100:
        return ".jpg"
    if data.startswith(b"\x89PNG\r\n\x1a\n") and len(data) > 100:
        return ".png"
    raise ValueError("유효한 JPEG/PNG 사진이 아닙니다")


def store_profile(connection, data):
    sql = f"INSERT INTO OfficialPlayerProfiles ({','.join(COLUMNS)}) VALUES ({','.join('?' for _ in COLUMNS)}) ON CONFLICT(Pcode) DO UPDATE SET "
    sql += ",".join(f"{c}=excluded.{c}" for c in COLUMNS if c != "Pcode")
    # Network/HTML work has already finished; hold a write lock only for this row.
    with connection:
        connection.execute(sql, [data.get(c) for c in COLUMNS])


def sync_one(connection, fetcher, code, photos, old=None):
    source = f"{BASE}/Record/Player/HitterDetail/Basic.aspx?playerId={code}"
    data = parse_profile(fetcher.get(source, 2 * 1024 * 1024).decode("utf-8-sig"), code, source)
    now = datetime.now(timezone.utc).isoformat()
    data.update(FetchedUtc=now, LastCheckedUtc=now, PhotoFileName=None)
    photo_error = None
    if data["PhotoSourceUrl"]:
        try:
            body = fetcher.get(data["PhotoSourceUrl"], MAX_IMAGE, is_photo=True)
            extension = image_extension(body)
            filename = code + extension
            photos.mkdir(parents=True, exist_ok=True)
            target = photos / filename
            temp = photos / (filename + ".tmp")
            if target.is_symlink() or temp.is_symlink():
                raise ValueError("사진 경로가 심볼릭 링크입니다")
            temp.write_bytes(body)
            temp.replace(target)
            data["PhotoFileName"] = filename
        except (OSError, ValueError) as error:
            photo_error = str(error)
    if old and not data["PhotoFileName"]:
        previous = old["PhotoFileName"]
        if previous in {code + ".jpg", code + ".jpeg", code + ".png"} and (photos / previous).is_file():
            data["PhotoFileName"] = previous
            data["PhotoSourceUrl"] = old["PhotoSourceUrl"]
    store_profile(connection, data)
    return data, photo_error


def main(argv=None):
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--db", required=True, type=Path, help="Existing SQLite statistics DB (close desktop import before syncing)")
    ap.add_argument("--photos-dir", type=Path)
    ap.add_argument("--player", action="append", default=[], help="Only this existing player ID; repeatable")
    ap.add_argument("--limit", type=int)
    ap.add_argument("--refresh-days", type=int, default=7)
    ap.add_argument("--delay", type=float, default=0.5, help="Minimum seconds between requests (at least 0.25)")
    ap.add_argument("--force", action="store_true")
    ap.add_argument("--report", type=Path)
    args = ap.parse_args(argv)
    if not args.db.is_file():
        ap.error("--db must point to an existing database")
    if args.refresh_days < 0 or args.limit is not None and args.limit < 1:
        ap.error("--refresh-days must be non-negative and --limit must be positive")
    photos = (args.photos_dir or args.db.resolve().parent / "player-photos").resolve()
    connection = sqlite3.connect(args.db, timeout=15)
    connection.row_factory = sqlite3.Row
    players = list(connection.execute("SELECT Pcode,Name FROM Players ORDER BY Pcode"))
    if args.player:
        wanted = set(args.player)
        known = {str(p["Pcode"]) for p in players}
        if wanted - known:
            ap.error("--player includes IDs absent from Players: " + ",".join(sorted(wanted - known)))
        players = [p for p in players if str(p["Pcode"]) in wanted]
    non_official = [str(p["Pcode"]) for p in players if not re.fullmatch(r"\d{4,10}", str(p["Pcode"]))]
    players = [p for p in players if re.fullmatch(r"\d{4,10}", str(p["Pcode"]))]
    if args.limit:
        players = players[:args.limit]
    connection.execute(SCHEMA)
    connection.commit()
    counts = {"selected": len(players), "updated": 0, "cached": 0, "photos": 0, "failed": 0, "photoWarnings": 0, "nonOfficialIds": len(non_official)}
    issues = []
    fetcher = Fetcher(args.delay)
    cutoff = datetime.now(timezone.utc) - timedelta(days=args.refresh_days)
    for i, player in enumerate(players, 1):
        code = str(player["Pcode"])
        old = connection.execute("SELECT * FROM OfficialPlayerProfiles WHERE Pcode=?", (code,)).fetchone()
        recent = False
        if old and old["LastCheckedUtc"]:
            try:
                stamp = datetime.fromisoformat(old["LastCheckedUtc"].replace("Z", "+00:00"))
                recent = stamp.tzinfo is not None and stamp >= cutoff
            except ValueError:
                pass
        photo_present = bool(old and old["PhotoFileName"] in {code + ".jpg", code + ".jpeg", code + ".png"} and (photos / old["PhotoFileName"]).is_file())
        if not args.force and recent and (photo_present or not old["PhotoSourceUrl"]):
            counts["cached"] += 1
            continue
        try:
            data, warning = sync_one(connection, fetcher, code, photos, old)
            counts["updated"] += 1
            counts["photos"] += int(bool(data["PhotoFileName"]))
            if warning:
                counts["photoWarnings"] += 1
                issues.append({"code": code, "kind": "photo", "message": warning})
            print(f"[{i}/{len(players)}] {code} {data['Name']} — 프로필 저장, 사진 {'있음' if data['PhotoFileName'] else '없음'}", flush=True)
        except (OSError, ValueError, sqlite3.Error) as error:
            counts["failed"] += 1
            issues.append({"code": code, "kind": "profile", "message": str(error)})
            print(f"[{i}/{len(players)}] {code} — 보류: {error}", flush=True)
    connection.close()
    report = {"completedUtc": datetime.now(timezone.utc).isoformat(), "counts": counts, "issues": issues, "nonOfficialIds": non_official}
    report_path = args.report or args.db.parent / "player-profile-sync-report.json"
    report_path.parent.mkdir(parents=True, exist_ok=True)
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    print(json.dumps(counts, ensure_ascii=False), flush=True)
    print(f"Report: {report_path}", flush=True)
    return 1 if counts["failed"] else 0


if __name__ == "__main__":
    raise SystemExit(main())
