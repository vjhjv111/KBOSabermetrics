"""Chronological, read-only KBO forecast calibration (Python 3 + NumPy).

Train: 2020-2023; choose model family on 2024; open 2025 only with --holdout.
Input is an explicit export of scored regular Games rows, not a database write.
"""
import argparse
from datetime import date as calendar_date
import hashlib
import itertools
import json
import math
from pathlib import Path
import numpy as np

TRAIN = [2020, 2021, 2022, 2023]
VALIDATION = 2024
HOLDOUT = 2025


def sigmoid(x):
    return 1 / (1 + np.exp(-np.clip(x, -40, 40)))


def inputs(teams, home):
    # teams: W,D,L,RF,RA. Include draws in played counts and runs.
    g = teams[:, :3].sum(axis=1)
    if np.any(g <= 0):
        raise ValueError('All teams require past games')
    rf, ra = teams[:, 3], teams[:, 4]
    pyth = np.clip(rf ** 1.83 / (rf ** 1.83 + ra ** 1.83), 1e-6, 1 - 1e-6)
    raw = np.log(pyth / (1 - pyth))
    opposition = (home + home.T) / g[:, None]
    balance = (home.sum(axis=1) - home.sum(axis=0)) / g
    return g, raw, opposition, balance


def ratings(features, params):
    g, raw, opposition, balance = features
    alpha, prior, advantage = params
    shrink = g / (g + prior)
    matrix = np.eye(10) - shrink[..., :, None] * alpha * opposition
    rhs = shrink * (raw - advantage * balance)
    strength = np.linalg.solve(matrix, rhs[..., None])[..., 0]
    return strength - strength.mean(axis=-1, keepdims=True)


def season_data(rows, year):
    games = []
    for game in rows:
        if not isinstance(game, dict) or type(game.get('year')) is not int:
            raise ValueError('Every game requires an integer season year')
        if game['year'] == year:
            games.append(game)
    seen = set()
    for game in games:
        game_id = game.get('id')
        if not isinstance(game_id, str) or not game_id.strip() or game_id in seen:
            raise ValueError(f'{year}: missing or duplicate game ID')
        seen.add(game_id)
        stamp = game.get('date')
        try:
            parsed = calendar_date.fromisoformat(stamp[:10]) if isinstance(stamp, str) else None
        except ValueError as error:
            raise ValueError(f'{year}: invalid game date for {game_id}') from error
        if parsed is None or parsed.year != year or stamp[:10] != parsed.isoformat():
            raise ValueError(f'{year}: game date and season year disagree for {game_id}')
        if any(not isinstance(game.get(side), str) or not game[side].strip() for side in ['home', 'away']) or game['home'] == game['away']:
            raise ValueError(f'{year}: invalid home/away teams for {game_id}')
        if any(type(game.get(score)) is not int or game[score] < 0 for score in ['hs', 'as']):
            raise ValueError(f'{year}: scores must be nonnegative integers for {game_id}')
    # Never rely on an export's ordering to protect pregame predictions from future results.
    games.sort(key=lambda game: (game['date'][:10], game['id']))
    codes = sorted({g['home'] for g in games} | {g['away'] for g in games})
    if len(codes) != 10 or len(games) != 720:
        raise ValueError(f'{year} is not a complete 10-team/720-game season')
    mapping = {code: i for i, code in enumerate(codes)}
    teams = np.zeros((10, 5), dtype=float)
    home = np.zeros((10, 10), dtype=float)
    features, snapshot, hs, aws, labels, dates, checkpoints = [], [], [], [], [], [], []
    targets = iter([20, 40, 60, 80, 100, 120])
    target = next(targets, None)
    used = 0
    for date, group in itertools.groupby(games, key=lambda g: g['date'][:10]):
        group = list(group)
        # Every game on this date uses the previous day's data, even doubleheaders.
        valid = teams[:, :3].sum(axis=1).min() >= 20
        if valid:
            features.append(inputs(teams.copy(), home.copy()))
            index = len(features) - 1
            for game in group:
                if game['hs'] == game['as']:
                    continue  # This model estimates P(home win | decisive game).
                snapshot.append(index)
                hs.append(mapping[game['home']]); aws.append(mapping[game['away']])
                labels.append(int(game['hs'] > game['as'])); dates.append(date)
        for game in group:
            h, a = mapping[game['home']], mapping[game['away']]
            rh, ra = game['hs'], game['as']
            home[h, a] += 1
            teams[h, 3:] += [rh, ra]; teams[a, 3:] += [ra, rh]
            if rh == ra:
                teams[h, 1] += 1; teams[a, 1] += 1
            else:
                teams[h if rh > ra else a, 0] += 1
                teams[a if rh > ra else h, 2] += 1
        used += len(group)
        if target is not None and teams[:, :3].sum(axis=1).min() >= target:
            checkpoints.append(dict(threshold=target, date=date, teams=teams.copy(), home=home.copy(), remaining=games[used:]))
            target = next(targets, None)
    if not np.all(teams[:, :3].sum(axis=1) == 144) or not np.all((home + home.T + np.eye(10) * 16) == 16):
        raise ValueError(f'{year}: incomplete team/matchup totals')
    stacked = tuple(np.stack([f[i] for f in features]) for i in range(4))
    return dict(year=year, codes=codes, mapping=mapping, features=stacked,
                snapshots=np.array(snapshot), hs=np.array(hs), aws=np.array(aws), labels=np.array(labels),
                dates=dates, checkpoints=checkpoints, final=teams)


def prediction(data, params):
    strength = ratings(data['features'], params)
    return sigmoid(strength[data['snapshots'], data['hs']] - strength[data['snapshots'], data['aws']] + params[2])


def metrics(y, p):
    p = np.clip(p, 1e-12, 1 - 1e-12)
    return dict(games=len(y), logLoss=float(np.mean(-y*np.log(p)-(1-y)*np.log1p(-p))),
                brier=float(np.mean((p-y)**2)), meanPrediction=float(p.mean()), homeWinRate=float(y.mean()))


def evaluate(data, params):
    per_year = {str(d['year']): metrics(d['labels'], prediction(d, params)) for d in data}
    return dict(parameters=dict(zip(['opponentWeight','priorGames','homeLogOdds'],params)), byYear=per_year,
                meanLogLoss=float(np.mean([v['logLoss'] for v in per_year.values()])),
                meanBrier=float(np.mean([v['brier'] for v in per_year.values()])))


def freeze(rows, out, digest):
    training = [season_data(rows, year) for year in TRAIN]
    validation = [season_data(rows, VALIDATION)]
    groups = dict(baseline=[(0,0,0)], shrink=[(0,k,0) for k in [10,20,40,80,160]],
                  shrink_home=[(0,k,h) for k in [10,20,40,80,160] for h in [.06,.12,.18,.24]],
                  shrink_opponent=[(a,k,0) for a in [.5,1] for k in [10,20,40,80,160]],
                  full=[(a,k,h) for a in [.5,1] for k in [10,20,40,80,160] for h in [.06,.12,.18,.24]])
    families = {}
    grid = []
    for name, candidates in groups.items():
        ranked = []
        for params in candidates:
            item = evaluate(training, params)
            grid.append(dict(family=name, **item))
            ranked.append(item)
        best = min(ranked, key=lambda x:x['meanLogLoss'])
        params = tuple(best['parameters'].values())
        families[name] = dict(training=best, validation=evaluate(validation, params))
    selected = min(families, key=lambda name:families[name]['validation']['meanLogLoss'])
    result = dict(version='pyth-sos-logit-v1', inputSha256=digest, trainYears=TRAIN, validationYear=VALIDATION,
                  holdoutYear=HOLDOUT, selection='Minimize equal-season training log loss per family; select family on 2024; freeze before 2025.',
                  minPastGames=20, refresh='Every day before games; all same-day outcomes withheld',
                  target='Home win conditional on decisive game; draws excluded from scoring, retained in past totals',
                  selectedFamily=selected, selectedParameters=families[selected]['training']['parameters'], families=families, grid=grid)
    (out/'frozen-selection.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({k:v for k,v in result.items() if k!='grid'},ensure_ascii=False,indent=2))


def qualify(values):
    cutoff = np.sort(values, axis=-1)[..., -5]
    above = values > cutoff[..., None] + 1e-12
    tied = np.abs(values-cutoff[..., None]) < 1e-12
    return above.astype(float) + tied * ((5-above.sum(axis=-1))/tied.sum(axis=-1))[..., None]


def playoff(data, params, trials=10000):
    final = data['final']
    actual = qualify(final[:,0]/(144-final[:,1]))
    # 2024's separate fifth-place tiebreak was won by KT over SK/SSG.
    if data['year']==2024:
        actual[data['mapping']['KT']]=1; actual[data['mapping']['SK']]=0
    output=[]
    for cp in data['checkpoints']:
        strength=ratings(inputs(cp['teams'],cp['home']),params)
        wins=np.tile(cp['teams'][:,0],(trials,1))
        rng=np.random.default_rng(20260912+data['year']*100+cp['threshold'])
        for game in cp['remaining']:
            h,a=data['mapping'][game['home']],data['mapping'][game['away']]
            ph=sigmoid(strength[h]-strength[a]+params[2])
            home_won=rng.random(trials)<ph
            wins[:,h]+=home_won; wins[:,a]+=~home_won
        odds=qualify(wins/(144-cp['teams'][:,1])).mean(axis=0)
        output.append(dict(threshold=cp['threshold'],date=cp['date'],remaining=len(cp['remaining']),
                           brier=float(np.mean((odds-actual)**2)),
                           teams=[dict(team=t,probability=float(odds[i]),qualified=float(actual[i])) for i,t in enumerate(data['codes'])]))
    return dict(year=data['year'],meanBrier=float(np.mean([x['brier'] for x in output])),checkpoints=output)


def bootstrap_blocks(data, baseline, candidate):
    y=data['labels']
    ll=lambda p: -y*np.log(np.clip(p,1e-12,1-1e-12))-(1-y)*np.log(np.clip(1-p,1e-12,1-1e-12))
    diffs=np.stack([ll(candidate)-ll(baseline),(candidate-y)**2-(baseline-y)**2],axis=1)
    # Unix-epoch-aligned seven-day paired blocks keep same-date games together.
    dates=np.array(data['dates'],dtype='datetime64[D]').astype(int)
    weeks=dates//7
    blocks=[diffs[weeks==w] for w in sorted(set(weeks))]
    sums=np.array([b.sum(axis=0) for b in blocks]); counts=np.array([len(b) for b in blocks])
    rng=np.random.default_rng(93017)
    choices=rng.integers(0,len(blocks),(5000,len(blocks)))
    samples=sums[choices].sum(axis=1)/counts[choices].sum(axis=1)[:,None]
    return dict(method='Paired seven-day block bootstrap (Unix-epoch aligned), 5000 resamples; does not measure season-to-season uncertainty',
                blocks=len(blocks),candidateMinusBaselineLogLoss95=np.quantile(samples[:,0],[.025,.975]).tolist(),
                candidateMinusBaselineBrier95=np.quantile(samples[:,1],[.025,.975]).tolist())


def holdout(rows,out,digest):
    frozen_path=out/'frozen-selection.json'
    frozen=json.loads(frozen_path.read_text(encoding='utf-8'))
    if frozen['inputSha256']!=digest:
        raise ValueError('Input changed since selection was frozen')
    test=season_data(rows,HOLDOUT)
    families={name:tuple(info['training']['parameters'].values()) for name,info in frozen['families'].items()}
    game={name:evaluate([test],p) for name,p in families.items()}
    selected=frozen['selectedFamily']
    validation=season_data(rows,VALIDATION)
    ps={name:[playoff(validation,params),playoff(test,params)] for name,params in families.items() if name in {'baseline',selected,'full'}}
    interval=bootstrap_blocks(test,prediction(test,families['baseline']),prediction(test,families[selected]))
    result=dict(selectionSha256=hashlib.sha256(frozen_path.read_bytes()).hexdigest(),selectedFamily=selected,
                holdoutYear=HOLDOUT,gameMetrics=game,pairedUncertainty=interval,playoffMetrics=ps,
                playoffLimitations=['10000 trials; paired random draws for model comparison',
                                    'Remaining home/away assignments use final recorded schedule; ignores as-of postponement uncertainty',
                                    'Future draws not simulated (same as baseline); 2024 fifth-place tiebreak label KT=1 SK=0',
                                    'Checkpoints overlap and share the same final qualifier labels; not independent samples',
                                    'Only one untouched test season; this is initial validation, not proof of future accuracy'])
    (out/'holdout-report.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({**{k:v for k,v in result.items() if k!='playoffMetrics'},'playoffBrier':{k:[{'year':y['year'],'meanBrier':y['meanBrier']} for y in v] for k,v in ps.items()}},ensure_ascii=False,indent=2))


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--games',type=Path,required=True)
    parser.add_argument('--out',type=Path,required=True)
    parser.add_argument('--holdout',action='store_true')
    args=parser.parse_args()
    raw=args.games.read_bytes(); obj=json.loads(raw)
    rows=obj if isinstance(obj,list) else obj['games']
    args.out.mkdir(parents=True,exist_ok=True)
    (holdout if args.holdout else freeze)(rows,args.out,hashlib.sha256(raw).hexdigest())


if __name__=='__main__': main()
