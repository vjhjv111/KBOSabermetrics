using Microsoft.Data.Sqlite;

internal static class RosterFixture
{
    public static void Create(string path)
    {
        if (File.Exists(path)) throw new InvalidOperationException("Fixture must use a new database.");
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        db.Open();
        using var schema = db.CreateCommand();
        schema.CommandText = """
            CREATE TABLE Metadata(MetaKey TEXT PRIMARY KEY,MetaValue TEXT);
            INSERT INTO Metadata VALUES('DataVersion','1');
            CREATE TABLE Games(GameId TEXT PRIMARY KEY,SeasonYear INTEGER,GameDate TEXT,RoundCode TEXT,
                StatusCode TEXT,AwayTeamCode TEXT,HomeTeamCode TEXT);
            CREATE TABLE Players(Pcode TEXT PRIMARY KEY,Name TEXT,BatsThrows TEXT,PrimaryPosition TEXT);
            CREATE TABLE GamePlayers(GameId TEXT,Pcode TEXT,TeamCode TEXT,HitType TEXT);
            CREATE TABLE BatterGameStats(GameId TEXT,Pcode TEXT,TeamCode TEXT,Name TEXT,
                PA INTEGER,AB INTEGER,H INTEGER,HR INTEGER,BB INTEGER,HBP INTEGER,SF INTEGER,SO INTEGER,TB INTEGER,
                Pitches INTEGER,InZone INTEGER,OutZone INTEGER,ZoneSwings INTEGER,ChaseSwings INTEGER,
                ZoneContacts INTEGER,OutZoneContacts INTEGER);
            CREATE TABLE PitcherGameStats(GameId TEXT,Pcode TEXT,TeamCode TEXT,Name TEXT,
                TBF INTEGER,HasFinalLine INTEGER,FinalBB INTEGER,FinalSO INTEGER,PaBB INTEGER,PaSO INTEGER,
                InningsOuts INTEGER,HitsAllowed INTEGER,EarnedRuns INTEGER,Pitches INTEGER,
                InZone INTEGER,OutZone INTEGER,ZoneSwings INTEGER,ChaseSwings INTEGER,
                ZoneContacts INTEGER,OutZoneContacts INTEGER);
            CREATE TABLE Pitches(GameId TEXT,PitcherPcode TEXT,PitchType TEXT,SpeedKmh REAL);
            CREATE TABLE BattingGameLines(GameId TEXT,Pcode TEXT,Height TEXT);
            CREATE TABLE PitchingGameLines(GameId TEXT,Pcode TEXT,Height TEXT);
            """;
        schema.ExecuteNonQuery();
        void Insert(string table, params object?[] values)
        {
            using var command = db.CreateCommand();
            command.CommandText = $"INSERT INTO {table} VALUES({string.Join(',', values.Select((_, i) => "$p" + i))})";
            for (var i = 0; i < values.Length; i++) command.Parameters.AddWithValue("$p" + i, values[i] ?? DBNull.Value);
            command.ExecuteNonQuery();
        }
        Insert("Games", "g25", 2025, "2025-09-10", "kbo_r", "RESULT", "HH", "SS");
        Insert("Games", "g26a", 2026, "2026-04-01", "kbo_r", "RESULT", "HH", "SS");
        Insert("Games", "g26b", 2026, "2026-09-11", "kbo_r", "RESULT", "KT", "HH");
        Insert("Games", "post", 2026, "2026-10-20", "kbo_ps", "RESULT", "HH", "SS");
        Insert("Games", "star", 2026, "2026-09-12", "kbo_r", "RESULT", "EA", "WE");
        Insert("Games", "future", 2026, "2026-09-13", "kbo_r", "BEFORE", "HH", "SS");
        Insert("Games", "g99", 2029, "2029-04-01", "kbo_r", "BEFORE", "HH", "SS");
        Insert("Players", "101", "같은이름", "우투좌타", "외야수");
        Insert("Players", "102", "같은이름", null, null);
        Insert("Players", "201", "실측투수", "좌투좌타", "투수");
        Insert("Players", "202", "측정없음", null, null);
        Insert("Players", "301", "이도류", "우투우타", "투수");
        Insert("BattingGameLines", "g26a", "101", "188cm");
        Insert("PitchingGameLines", "g26a", "201", "190cm");
        Insert("BattingGameLines", "g25", "101", "181cm");
        Insert("GamePlayers", "g25", "101", "SS", "우투우타");
        Insert("GamePlayers", "g25", "201", "SS", "우투우타");
        Insert("GamePlayers", "g26a", "101", "HH", "우투좌타");
        Insert("GamePlayers", "g26a", "201", "HH", "좌투좌타");
        Insert("BatterGameStats", "g25", "101", "SS", "같은이름", 4,4,1,0,0,0,0,2,1,10,5,5,4,1,2,0);
        Insert("BatterGameStats", "g26a", "101", "HH", "같은이름", 5,4,2,1,1,0,0,1,5,20,12,8,9,2,8,1);
        Insert("BatterGameStats", "g26b", "101", "KT", "같은이름", 4,3,1,0,0,1,0,0,2,16,8,8,4,4,3,2);
        Insert("BatterGameStats", "g26b", "102", "HH", "같은이름", 1,1,0,0,0,0,0,1,0,0,0,0,0,0,0,0);
        Insert("BatterGameStats", "g26b", "301", "HH", "이도류", 2,2,1,1,0,0,0,0,4,0,0,0,0,0,0,0);
        Insert("BatterGameStats", "g26b", "999", "HH", "미출장", 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0);
        foreach (var game in new[] { "post", "star", "future", "g99" })
            Insert("BatterGameStats", game, "101", game == "star" ? "EA" : "HH", "제외기록", 100,100,100,100,0,0,0,0,400,100,100,0,100,0,100,0);
        Insert("PitcherGameStats", "g25", "201", "SS", "실측투수", 10,1,0,2,99,99,9,5,3,12,4,8,3,4,2,1);
        Insert("PitcherGameStats", "g26a", "201", "HH", "실측투수", 10,1,2,3,99,99,9,2,1,20,12,8,9,2,8,1);
        Insert("PitcherGameStats", "g26b", "201", "HH", "실측투수", 8,0,99,99,1,2,6,1,0,16,8,8,4,4,3,2);
        Insert("PitcherGameStats", "g26b", "202", "HH", "측정없음", 3,1,0,0,0,0,3,0,0,0,0,0,0,0,0,0);
        Insert("PitcherGameStats", "g26b", "301", "HH", "이도류", 4,1,0,1,0,1,3,1,1,0,0,0,0,0,0,0);
        Insert("PitcherGameStats", "g26b", "999", "HH", "미출장", 0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0);
        foreach (var game in new[] { "post", "star", "future", "g99" })
            Insert("PitcherGameStats", game, "201", game == "star" ? "WE" : "HH", "제외기록", 100,1,90,90,90,90,300,90,90,100,100,0,100,0,100,0);
        foreach (var speed in new object?[] { 146d, 150d, 0d, null }) Insert("Pitches", "g26a", "201", "직구", speed);
        Insert("Pitches", "g26b", "201", "슬라이더", 130d);
        Insert("Pitches", "g26b", "201", "슬라이더", 134d);
        Insert("Pitches", "g26b", "201", "커브", 120d);
        Insert("Pitches", "g26b", "201", "스위퍼", 140d);
        Insert("Pitches", "g25", "201", "직구", 136d);
        Insert("Pitches", "post", "201", "직구", 170d);
        Insert("Pitches", "star", "201", "직구", 170d);
        Insert("Pitches", "future", "201", "직구", 170d);
    }

    public static void ChangeSeason2026(string path)
    {
        using var db = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        db.Open(); using var command = db.CreateCommand();
        command.CommandText = """
            UPDATE BatterGameStats SET H=H+1,TB=TB+1 WHERE GameId='g26b' AND Pcode='101';
            UPDATE Pitches SET SpeedKmh=158 WHERE PitcherPcode='201' AND GameId='g26a' AND SpeedKmh>0;
            UPDATE Metadata SET MetaValue='2' WHERE MetaKey='DataVersion';
            """;
        command.ExecuteNonQuery();
    }
}
