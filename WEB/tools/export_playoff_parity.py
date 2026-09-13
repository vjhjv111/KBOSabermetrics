"""Generate deterministic Python/C# strength-model parity fixtures.

Requires the same Python + NumPy environment as calibrate_playoffs.py. Uses
three fixed seasons, three completed-game checkpoints, and three fixed
parameter sets; performs no tuning or forecast-quality evaluation. The 2025
rows exercise numerical parity only. Input is export_playoff_games.py JSON.
"""

import argparse
import json
from pathlib import Path

from calibrate_playoffs import inputs, ratings, season_data, sigmoid


YEARS = (2020, 2023, 2025)
CHECKPOINTS = (20, 60, 100)
PARAMETERS = ((0, 0, 0), (0.5, 40, 0), (0.5, 40, 0.06))


def build_fixtures(rows):
    if not isinstance(rows, list):
        raise ValueError('Games input must be a JSON array')
    fixtures = []
    for year in YEARS:
        season = season_data(rows, year)
        checkpoints = {item['threshold']: item for item in season['checkpoints']}
        for threshold in CHECKPOINTS:
            if threshold not in checkpoints:
                raise ValueError(f'{year}: missing checkpoint {threshold}')
            checkpoint = checkpoints[threshold]
            teams, home = checkpoint['teams'], checkpoint['home']
            features = inputs(teams, home)
            for params in PARAMETERS:
                strength = ratings(features, params)
                fixtures.append({
                    'name': f'{year}-{threshold}-{params}',
                    'teams': [
                        {'code': code, 'w': int(team[0]), 'd': int(team[1]),
                         'l': int(team[2]), 'rf': float(team[3]), 'ra': float(team[4])}
                        for code, team in zip(season['codes'], teams)
                    ],
                    # Matrix rows are home teams, columns are away teams.
                    'homeGames': home.astype(int).tolist(),
                    'parameters': {'opponentWeight': params[0], 'priorGames': params[1],
                                   'homeLogOdds': params[2]},
                    'ratings': strength.tolist(),
                    'probabilities': [
                        {'home': h, 'away': a,
                         'probability': float(sigmoid(strength[h] - strength[a] + params[2]))}
                        for h in range(len(teams)) for a in range(len(teams)) if h != a
                    ],
                })
    return fixtures


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--games', required=True, type=Path, help='Exported scored regular Games JSON')
    parser.add_argument('--out', required=True, type=Path, help='Output parity fixture JSON')
    args = parser.parse_args()
    try:
        games, output = args.games.resolve(), args.out.resolve()
        if games == output or (output.exists() and output.samefile(games)):
            raise ValueError('Output must not overwrite the Games input')
        rows = json.loads(games.read_text(encoding='utf-8'))
        fixtures = build_fixtures(rows)
        payload = json.dumps(fixtures, ensure_ascii=False, allow_nan=False, indent=2) + '\n'
        output.parent.mkdir(parents=True, exist_ok=True)
        output.write_text(payload, encoding='utf-8')
    except (OSError, ValueError) as error:
        parser.exit(1, f'Parity export failed: {error}\n')
    print(json.dumps({'fixtures': len(fixtures),
                      'ratings': sum(len(item['ratings']) for item in fixtures),
                      'probabilities': sum(len(item['probabilities']) for item in fixtures)}))


if __name__ == '__main__':
    main()
