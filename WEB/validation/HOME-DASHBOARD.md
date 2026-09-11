# Home dashboard

`POST /api/home` accepts `year` and `section` (`standings` or `leaders`). It uses the existing CSRF, quota and concurrency protections. Results are cached by database source version. Only completed regular-season games contribute to team records; EA/WE are excluded.

Pythagorean winning percentage is RF^1.83 / (RF^1.83 + RA^1.83). Both zero yields no estimate. Expected wins use decisive games (W+L); actual-minus-expected compares W with that number.

The postseason model supports the 2026 ten-team, 144-game format. It requires all ten teams to have at least twenty imported games and validates each opponent pair has at most sixteen games. Remaining pair counts are sixteen minus imported results. Log5 converts the two Pythagorean strengths to a matchup probability. A fixed-seed 10,000-trial Monte Carlo simulation preserves actual wins and draws and counts the top five by final W/(W+L). Ties crossing fifth place share the available slots equally. Completed seasons therefore have deterministic qualification except ties.

This is an uncalibrated model estimate, not an official forecast. It assumes no future draws and ignores home advantage, injuries, starting pitchers and actual future scheduling order. Missing completed games look like remaining games, so the displayed database date and completeness matter. Twenty games is a display guard, not evidence of predictive calibration. Historical seasons retain team/Pythagorean rankings but have no postseason estimate.

WAR combines batter WAR and pitcher KBO fWAR already calculated by this site. Rate leaders require the maximum team game count multiplied by 3.1 PA or 1 IP. Counting leaders use all players. Equal values use player code for stable display ordering.

References:
- https://m.koreabaseball.com/About/GameManage.aspx
- https://www.koreabaseball.com/MediaNews/Notice/View.aspx?bdSe=11794
- https://www.baseball-reference.com/bullpen/Pythagorean_W-L

Run `node validation/home-page-smoke.mjs http://127.0.0.1:5193` against a loopback sample-data host. Checks cover standings conservation, the formula, forecast slot totals, null estimates and leader ordering. Additional model checks used equal-strength teams (approximately 50% each), increased strength, a completed season and a ten-way final tie.
