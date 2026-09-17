using Microsoft.Data.Sqlite;
using NaverRelayUI.Models;

namespace NaverRelayUI.Parsing
{
    public static class SqliteWriter
    {
        public static void EnsureSchema(SqliteConnection conn)
        {
            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS games (
                    game_id TEXT PRIMARY KEY,
                    game_date TEXT,
                    season_year INTEGER,
                    stadium TEXT,
                    home_team_code TEXT,
                    away_team_code TEXT,
                    home_score INTEGER,
                    away_score INTEGER,
                    winner TEXT,
                    status_code TEXT,
                    round_code TEXT,
                    raw_json TEXT
                );

                CREATE TABLE IF NOT EXISTS plate_appearances (
                    game_id TEXT NOT NULL,
                    pa_no INTEGER NOT NULL,
                    inning INTEGER,
                    home_or_away TEXT,
                    batter_pcode TEXT,
                    batter_name TEXT,
                    bat_order INTEGER,
                    pitcher_pcode TEXT,
                    batter_result_text TEXT,
                    batter_result_type INTEGER,
                    home_score_before INTEGER,
                    away_score_before INTEGER,
                    home_score_after INTEGER,
                    away_score_after INTEGER,
                    home_win_rate_after REAL,
                    wpa REAL,
                    pitch_count INTEGER,
                    PRIMARY KEY (game_id, pa_no)
                );

                CREATE TABLE IF NOT EXISTS pitch_events (
                    game_id TEXT NOT NULL,
                    pa_no INTEGER NOT NULL,
                    seqno INTEGER NOT NULL,
                    pitch_num INTEGER,
                    stuff TEXT,
                    speed_kmh REAL,
                    result_code TEXT,
                    result_raw TEXT,
                    balls_after INTEGER,
                    strikes_after INTEGER,
                    pts_pitch_id TEXT,
                    cross_plate_x REAL,
                    cross_plate_y REAL,
                    vy0 REAL,
                    vz0 REAL,
                    vx0 REAL,
                    batter_stance TEXT,
                    PRIMARY KEY (game_id, seqno)
                );

                CREATE TABLE IF NOT EXISTS runner_advances (
                    game_id TEXT NOT NULL,
                    pa_no INTEGER NOT NULL,
                    seqno INTEGER NOT NULL,
                    from_base INTEGER NOT NULL,
                    runner_bat_order INTEGER,
                    runner_name TEXT,
                    to_base_raw TEXT,
                    is_score INTEGER,
                    is_out INTEGER,
                    text TEXT,
                    PRIMARY KEY (game_id, seqno, from_base)
                );

                CREATE INDEX IF NOT EXISTS ix_pa_batter ON plate_appearances(batter_pcode);
                CREATE INDEX IF NOT EXISTS ix_pa_pitcher ON plate_appearances(pitcher_pcode);
                CREATE INDEX IF NOT EXISTS ix_pitch_pa ON pitch_events(game_id, pa_no);
            ";
            cmd.ExecuteNonQuery();
        }

        // One file = one transaction. INSERT OR REPLACE makes re-running the
        // parser over a folder (e.g. after new games were collected) safe —
        // existing rows just get overwritten with the same data.
        public static void WriteGame(
            SqliteConnection conn,
            GameInfo game,
            string rawJson,
            List<PlateAppearance> plateAppearances,
            List<PitchEvent> pitchEvents,
            List<RunnerAdvance> runnerAdvances)
        {
            using var tx = conn.BeginTransaction();

            Exec(conn, tx, @"
                INSERT OR REPLACE INTO games
                    (game_id, game_date, season_year, stadium, home_team_code, away_team_code,
                     home_score, away_score, winner, status_code, round_code, raw_json)
                VALUES
                    ($gameId, $gameDate, $seasonYear, $stadium, $homeCode, $awayCode,
                     $homeScore, $awayScore, $winner, $statusCode, $roundCode, $rawJson)",
                cmd =>
                {
                    cmd.Parameters.AddWithValue("$gameId", game.GameId ?? "");
                    cmd.Parameters.AddWithValue("$gameDate", game.GameDate ?? "");
                    cmd.Parameters.AddWithValue("$seasonYear", game.SeasonYear);
                    cmd.Parameters.AddWithValue("$stadium", (object?)game.Stadium ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$homeCode", (object?)game.HomeTeamCode ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$awayCode", (object?)game.AwayTeamCode ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$homeScore", (object?)game.HomeTeamScore ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$awayScore", (object?)game.AwayTeamScore ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$winner", (object?)game.Winner ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$statusCode", (object?)game.StatusCode ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$roundCode", (object?)game.RoundCode ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$rawJson", rawJson);
                });

            foreach (var pa in plateAppearances)
            {
                Exec(conn, tx, @"
                    INSERT OR REPLACE INTO plate_appearances
                        (game_id, pa_no, inning, home_or_away, batter_pcode, batter_name, bat_order,
                         pitcher_pcode, batter_result_text, batter_result_type,
                         home_score_before, away_score_before, home_score_after, away_score_after,
                         home_win_rate_after, wpa, pitch_count)
                    VALUES
                        ($gameId, $paNo, $inning, $hoa, $batterPcode, $batterName, $batOrder,
                         $pitcherPcode, $resultText, $resultType,
                         $hsBefore, $asBefore, $hsAfter, $asAfter,
                         $winRate, $wpa, $pitchCount)",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("$gameId", pa.GameId);
                        cmd.Parameters.AddWithValue("$paNo", pa.PaNo);
                        cmd.Parameters.AddWithValue("$inning", pa.Inning);
                        cmd.Parameters.AddWithValue("$hoa", (object?)pa.HomeOrAway ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$batterPcode", (object?)pa.BatterPcode ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$batterName", (object?)pa.BatterName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$batOrder", (object?)pa.BatOrder ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$pitcherPcode", (object?)pa.PitcherPcode ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$resultText", (object?)pa.BatterResultText ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$resultType", (object?)pa.BatterResultType ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$hsBefore", pa.HomeScoreBefore);
                        cmd.Parameters.AddWithValue("$asBefore", pa.AwayScoreBefore);
                        cmd.Parameters.AddWithValue("$hsAfter", pa.HomeScoreAfter);
                        cmd.Parameters.AddWithValue("$asAfter", pa.AwayScoreAfter);
                        cmd.Parameters.AddWithValue("$winRate", (object?)pa.HomeWinRateAfter ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$wpa", (object?)pa.WpaByPlate ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$pitchCount", pa.PitchCount);
                    });
            }

            foreach (var pe in pitchEvents)
            {
                Exec(conn, tx, @"
                    INSERT OR REPLACE INTO pitch_events
                        (game_id, pa_no, seqno, pitch_num, stuff, speed_kmh, result_code, result_raw,
                         balls_after, strikes_after, pts_pitch_id,
                         cross_plate_x, cross_plate_y, vy0, vz0, vx0, batter_stance)
                    VALUES
                        ($gameId, $paNo, $seqno, $pitchNum, $stuff, $speed, $resultCode, $resultRaw,
                         $balls, $strikes, $ptsId,
                         $cpx, $cpy, $vy0, $vz0, $vx0, $stance)",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("$gameId", pe.GameId);
                        cmd.Parameters.AddWithValue("$paNo", pe.PaNo);
                        cmd.Parameters.AddWithValue("$seqno", pe.Seqno);
                        cmd.Parameters.AddWithValue("$pitchNum", pe.PitchNum);
                        cmd.Parameters.AddWithValue("$stuff", (object?)pe.Stuff ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$speed", (object?)pe.SpeedKmh ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$resultCode", pe.Result.ToString());
                        cmd.Parameters.AddWithValue("$resultRaw", (object?)pe.ResultRaw ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$balls", (object?)pe.BallsAfter ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$strikes", (object?)pe.StrikesAfter ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$ptsId", (object?)pe.PtsPitchId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$cpx", (object?)pe.CrossPlateX ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$cpy", (object?)pe.CrossPlateY ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$vy0", (object?)pe.Vy0 ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$vz0", (object?)pe.Vz0 ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$vx0", (object?)pe.Vx0 ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$stance", (object?)pe.BatterStance ?? DBNull.Value);
                    });
            }

            foreach (var ra in runnerAdvances)
            {
                Exec(conn, tx, @"
                    INSERT OR REPLACE INTO runner_advances
                        (game_id, pa_no, seqno, from_base, runner_bat_order, runner_name,
                         to_base_raw, is_score, is_out, text)
                    VALUES
                        ($gameId, $paNo, $seqno, $fromBase, $batOrder, $name,
                         $toBase, $isScore, $isOut, $text)",
                    cmd =>
                    {
                        cmd.Parameters.AddWithValue("$gameId", ra.GameId);
                        cmd.Parameters.AddWithValue("$paNo", ra.PaNo);
                        cmd.Parameters.AddWithValue("$seqno", ra.Seqno);
                        cmd.Parameters.AddWithValue("$fromBase", ra.FromBase);
                        cmd.Parameters.AddWithValue("$batOrder", (object?)ra.RunnerBatOrder ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$name", (object?)ra.RunnerName ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$toBase", (object?)ra.ToBaseRaw ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("$isScore", ra.IsScore ? 1 : 0);
                        cmd.Parameters.AddWithValue("$isOut", ra.IsOut ? 1 : 0);
                        cmd.Parameters.AddWithValue("$text", ra.Text);
                    });
            }

            tx.Commit();
        }

        private static void Exec(SqliteConnection conn, SqliteTransaction tx, string sql, Action<SqliteCommand> bind)
        {
            var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            bind(cmd);
            cmd.ExecuteNonQuery();
        }
    }
}
