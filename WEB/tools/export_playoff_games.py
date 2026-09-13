"""Export scored regular-season Games for reproducible playoff calibration.

Uses only the Python standard library and opens SQLite with URI mode=ro.
JSON rows contain year, date (Games.GameDate), id, home, away, hs, as, stadium.
GameDate can differ from the ID's original date for suspended games. Preserve
every game ID, including doubleheaders with the same teams and final scores.
"""

import argparse
from collections import Counter
from contextlib import closing
from datetime import date as calendar_date
import json
from pathlib import Path
import re
import sqlite3


QUERY = """
    SELECT SeasonYear AS year, GameDate AS date, GameId AS id,
           HomeTeamCode AS home, AwayTeamCode AS away,
           HomeScore AS hs, AwayScore AS [as], Stadium AS stadium
    FROM Games
    WHERE SeasonYear BETWEEN ? AND ?
      AND LOWER(TRIM(RoundCode)) = 'kbo_r'
      AND UPPER(StatusCode) = 'RESULT'
      AND HomeScore IS NOT NULL AND AwayScore IS NOT NULL
      AND HomeScore >= 0 AND AwayScore >= 0
      AND UPPER(HomeTeamCode) NOT IN ('EA', 'WE')
      AND UPPER(AwayTeamCode) NOT IN ('EA', 'WE')
    ORDER BY GameDate, GameDateTime, GameId
"""


def validate_rows(rows, from_year, to_year):
    """Reject corrupt records rather than silently deduplicating or repairing."""
    seen = set()
    for row in rows:
        year, game_id, stamp = row['year'], row['id'], row['date']
        if type(year) is not int or not from_year <= year <= to_year:
            raise ValueError('A game has an invalid season year')
        if not isinstance(game_id, str) or not game_id.strip() or game_id in seen:
            raise ValueError('A game has a missing or duplicate ID')
        seen.add(game_id)
        try:
            parsed = calendar_date.fromisoformat(stamp) if isinstance(stamp, str) else None
        except ValueError as error:
            raise ValueError(f'{game_id}: invalid GameDate') from error
        if parsed is None or stamp != parsed.isoformat() or parsed.year != year:
            raise ValueError(f'{game_id}: GameDate and season year disagree')
        for side in ('home', 'away'):
            team = row[side]
            if not isinstance(team, str) or not re.fullmatch(r'[A-Za-z0-9]{1,20}', team):
                raise ValueError(f'{game_id}: invalid {side} team code')
            if team.upper() in ('EA', 'WE'):
                raise ValueError(f'{game_id}: an All-Star team is not eligible')
        if row['home'].upper() == row['away'].upper():
            raise ValueError(f'{game_id}: home and away teams are identical')
        if any(type(row[key]) is not int or row[key] < 0 for key in ('hs', 'as')):
            raise ValueError(f'{game_id}: scores must be nonnegative integers')
        if row['stadium'] is not None and not isinstance(row['stadium'], str):
            raise ValueError(f'{game_id}: invalid stadium value')


def export_games(db, output, from_year=2020, to_year=2026):
    if not 1 <= from_year <= to_year <= 9999:
        raise ValueError('Year bounds must satisfy 1 <= from-year <= to-year <= 9999')
    db, output = Path(db).resolve(), Path(output).resolve()
    if not db.is_file():
        raise ValueError('The database must be an existing file')
    protected = {db, *(Path(str(db) + suffix) for suffix in ('-wal', '-shm', '-journal'))}
    if output in protected or (output.exists() and output.samefile(db)):
        raise ValueError('The output must not overwrite the database or its sidecars')
    with closing(sqlite3.connect(db.as_uri() + '?mode=ro', uri=True)) as connection:
        connection.row_factory = sqlite3.Row
        rows = [dict(row) for row in connection.execute(QUERY, (from_year, to_year))]
    validate_rows(rows, from_year, to_year)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(rows, ensure_ascii=False, separators=(',', ':')) + '\n',
                      encoding='utf-8')
    return Counter(row['year'] for row in rows)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--db', required=True, type=Path, help='Existing SQLite database (read-only)')
    parser.add_argument('--out', required=True, type=Path, help='Output JSON array')
    parser.add_argument('--from-year', type=int, default=2020)
    parser.add_argument('--to-year', type=int, default=2026)
    args = parser.parse_args()
    try:
        counts = export_games(args.db, args.out, args.from_year, args.to_year)
    except (OSError, sqlite3.Error, ValueError) as error:
        parser.exit(1, f'Export failed: {error}\n')
    print(json.dumps({'games': sum(counts.values()), 'by_year': dict(sorted(counts.items()))}))


if __name__ == '__main__':
    main()
