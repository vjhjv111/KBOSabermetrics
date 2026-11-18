"""Exercise the production batted-ball SQL with synthetic contact and stance data."""
from pathlib import Path
import sqlite3

source = (Path(__file__).resolve().parents[2] / 'server/src/NaverRelay.Infrastructure.Sqlite/DatabaseCacheService.RecordRoom.cs').read_text(encoding='utf-8')
method = source.split('internal async Task<IReadOnlyList<BatterBattedBallRecordRow>>', 1)[1]
sql = method.split('command.CommandText = $"""', 1)[1].split('""";', 1)[0]
sql = sql.replace('{filter.Cte}', 'WITH FilteredGames AS (SELECT * FROM Games)')
sql = sql.replace('{identity.SelectColumns}', 'pa.BatterPcode').replace('{identity.JoinClause}', '').replace('{identity.GroupBy}', 'pa.BatterPcode').replace('{situation.Sql}', '')
db = sqlite3.connect(':memory:')
db.executescript('''
CREATE TABLE Games(GameId TEXT);
INSERT INTO Games VALUES ('G');
CREATE TABLE PlateAppearances(PlateAppearanceId TEXT,GameId TEXT,BatterPcode TEXT,BattingTeamCode TEXT,IsOfficial INT,BattedBallType INT,ResultType INT,FieldDirection INT,CountsAsAtBat INT,IsHit INT);
CREATE TABLE Pitches(PlateAppearanceId TEXT,GameId TEXT,BatterStance TEXT,ActualPitchIndex INT,SourceOptionIndex INT,PitchEventId TEXT);
''')
def contact(key, ball, direction, stance, result=1, batter='B', official=1):
    db.execute('INSERT INTO PlateAppearances VALUES (?,\'G\',?,\'T\',?,?,?,?,1,0)', (key,batter,official,ball,result,direction))
    if stance is not None:
        db.execute('INSERT INTO Pitches VALUES (?,\'G\',?,1,1,?)', (key,stance,key))
contact('RF',2,1,'R')          # pulled fly
contact('LL',3,5,'L')          # pulled line drive
contact('RP',4,10,'R')         # pulled infield popup
contact('HR',0,4,'L',6)        # home run without explicit ball type
contact('GB',1,1,'R')          # ground ball: denominator only
contact('CF',2,3,'R')          # center: denominator only
contact('OF',2,5,'R')          # opposite: denominator only
contact('BU',5,8,'L')          # bunt: denominator only
contact('SW',2,1,'L')          # latest stance R wins; no duplicate PA
# Swing hitter's actual final stance, not season-level handedness.
db.execute("INSERT INTO Pitches VALUES ('SW','G','R',2,2,'SW2')")
contact('UD',2,0,'R')          # unknown direction excluded
contact('US',2,1,None)         # missing stance excluded
contact('UB',0,1,'R')          # unknown batted-ball type excluded
contact('NO',2,1,'R',official=0)
contact('EMPTY',2,0,None,batter='E')
rows = dict((row[0], row) for row in db.execute(sql, {'resultTeam':''}))
assert rows['B'][-2:] == (9,5), rows['B']
assert rows['E'][-2:] == (0,0), rows['E']
print('PASS: pulled fly/line/popup/HR, ground/center/opposite/bunt denominator, latest stance, missing data, unofficial exclusion, empty sample')
