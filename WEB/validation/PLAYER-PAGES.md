# Player pages

Select a player name in a record table or player search. Direct links use
`#player=69737&role=batter` (pitchers use `role=pitcher`).

The player page includes summary, career and yearly records, yearly charts,
game logs, situation splits, opponent matchups, play logs and pitch types.
Season, competition and role selectors use available database records.
Applicable tables support date/team filters, sorting and bounded pagination.

Percentile bars show the actual value and an empirical percentile against
all players with valid metric values in the selected season and competition. Ties receive their
midrank. Lower-is-better statistics reverse direction. A cohort smaller than
two has no percentile. There is no PA or innings qualification threshold.
Basic, advanced and value metrics appear together without duplicate labels
on the summary page.

Pitch-type outcome splits use the final pitch of each plate appearance;
pitch usage and average velocity summarize actual pitches over the full season.
Hit directions display recorded direction counts. No landing coordinates,
tracking estimates, awards, contracts, photos or jersey history are fabricated
when the source database does not supply them.

## Verification

Build the web project with the .NET 8 SDK selected by `WEB/global.json`.
Create a sample database using the application's `--sample-db` command and
`SampleData/2026.zip`, then run the web server against that database. Run:

```sh
node validation/player-page-smoke.mjs http://127.0.0.1:5191
```

The test is restricted to loopback hosts. It exercises batter and pitcher
views, pagination, OBP including sacrifice flies, hidden WAR columns, invalid
requests and empty date ranges. It obtains a session token before API calls.
The implementation was also inspected in a browser across the player tabs.
Full production-database latency has not been measured with the small fixture.
