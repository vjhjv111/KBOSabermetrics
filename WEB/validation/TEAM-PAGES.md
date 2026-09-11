# Team pages

Open **팀 정보** in the header or click a team in team records. Links use
`#team=HH&year=2026`. Team, season and competition are selectable.

- Overview: latest stored games, previous-game reconstructed line score,
  next stored fixture, position leaders, batting/pitching leaders, matchups,
  standings and runs scored/allowed.
- Schedule and player tables: 25 rows per page; player names link to profiles.
- Scores: final-score distribution, inning totals, 0/1/2/3/4/5+ run inning
  frequency and final results grouped by lead/tie/deficit at inning start.

Only RESULT games with final scores contribute to standings and score results.
Winning percentage excludes ties. Rankings reflect stored games. Position
leaders use recorded defensive innings (DH uses PA), not a claimed lineup.
Rate leaders require team games times 3.1 PA or one inning. Missing fixture,
salary, awards, coaching, WAA or official winning-pitcher data is not invented.
Inning scores are reconstructed from relay groups; absent score states are
excluded. Partial source coverage can differ from official full-season totals.

The endpoint uses read-only parameterized SQL under existing antiforgery,
rate limits, quota and query-gate middleware. No raw events or JSON are exposed.

Validation: .NET build, JS syntax, all ten fixture teams' totals and pagination,
and browser tab/navigation checks. Run `node validation/team-page-smoke.mjs`
against a loopback sample server. Full production database latency is unmeasured.
