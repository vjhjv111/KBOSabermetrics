/* Forecast explanations use the server's estimated ratings, never a second model. */
(() => {
  'use strict';
  const names = { HH: '한화', HT: 'KIA', KT: 'KT', LG: 'LG', LT: '롯데', NC: 'NC', OB: '두산', SK: 'SSG', SS: '삼성', WO: '키움' };
  const make = (tag, content, className) => {
    const element = document.createElement(tag);
    if (content !== undefined && content !== null) element.textContent = String(content);
    if (className) element.className = className;
    return element;
  };
  const finite = value => typeof value === 'number' && Number.isFinite(value);
  const number = (value, digits = 0) => finite(value) ? value.toLocaleString('ko-KR', { minimumFractionDigits: digits, maximumFractionDigits: digits }) : '—';
  const percent = value => finite(value) ? `${number(value * 100, 2)}%` : '—';
  const signed = value => `${value >= 0 ? '+' : '−'}${number(Math.abs(value), 4)}`;
  function formula(lines) {
    const block = make('div', undefined, 'fm-formula');
    for (const line of lines) block.append(make('span', line));
    return block;
  }
  function step(index, title, lead, formulas, explanation) {
    const card = make('section', undefined, 'fm-step');
    const heading = make('h3', undefined, 'fm-step-title');
    heading.append(make('span', index, 'fm-step-number'), make('span', title));
    card.append(heading, make('p', lead), formula(formulas), make('p', explanation, 'fm-help'));
    return card;
  }
  function externalLink(title, href) {
    const link = make('a', title);
    link.href = href;
    link.target = '_blank';
    link.rel = 'noopener noreferrer';
    return link;
  }
  function metric(label, value) {
    const item = make('div');
    item.append(make('dt', label), make('dd', value));
    return item;
  }
  function teamCard(row, venue) {
    const card = make('section', undefined, 'fm-team-card');
    const heading = make('h4');
    const name = make('span', names[row.team] || row.team, 'team-name');
    name.dataset.team = Object.hasOwn(names, row.team) ? row.team : 'neutral';
    heading.append(make('span', venue, 'fm-venue'), name);
    const stats = make('dl', undefined, 'fm-stats');
    stats.append(metric('경기 G', number(row.g)), metric('득점 RF', number(row.rf)), metric('실점 RA', number(row.ra)), metric('피타고리안 p', percent(row.pyth)), metric('회귀 가중치 λ', number(row.regressionWeight, 4)), metric('보정 전력 r', signed(row.rating)));
    card.append(heading, stats);
    return card;
  }
  function example(rows, homeLogOdds) {
    const section = make('section', undefined, 'fm-example');
    section.append(make('h3', '현재 기록을 공식에 넣어 보면'), make('p', '두 팀을 골라 1경기의 예상 승률을 확인하세요. 팀 전력 r은 서버에서 전체 10개 팀을 함께 계산한 값입니다.', 'fm-help'));
    const controls = make('div', undefined, 'fm-controls');
    const home = make('select');
    const away = make('select');
    for (const select of [home, away]) for (const row of rows) select.add(new Option(names[row.team] || row.team, row.team));
    const preferred = rows.find(row => row.team === 'KT') || rows[0];
    home.value = preferred.team;
    away.value = (rows.find(row => row.team === 'HT' && row.team !== home.value) || rows.find(row => row.team !== home.value)).team;
    for (const [labelText, select] of [['홈 팀', home], ['원정 팀', away]]) {
      const label = make('label', labelText);
      label.append(select);
      controls.append(label);
    }
    const cards = make('div', undefined, 'fm-example-teams');
    const result = make('div', undefined, 'fm-result');
    result.setAttribute('aria-live', 'polite');
    result.setAttribute('aria-atomic', 'true');
    const update = () => {
      for (const option of home.options) option.disabled = option.value === away.value;
      for (const option of away.options) option.disabled = option.value === home.value;
      const h = rows.find(row => row.team === home.value);
      const a = rows.find(row => row.team === away.value);
      if (!h || !a || h.team === a.team) return;
      cards.replaceChildren(teamCard(h, '홈'), teamCard(a, '원정'));
      const difference = h.rating - a.rating + homeLogOdds;
      const probability = difference >= 0 ? 1 / (1 + Math.exp(-difference)) : Math.exp(difference) / (1 + Math.exp(difference));
      const headline = make('p', undefined, 'fm-result-headline');
      headline.append(make('span', `${names[h.team] || h.team} 홈 승리`), make('strong', percent(probability)));
      result.replaceChildren(headline, formula([`r홈 − r원정 + H = ${number(h.rating, 4)} − (${number(a.rating, 4)}) + ${number(homeLogOdds, 2)}`, `= ${number(difference, 4)}`, `P(홈 승리) = 1 / (1 + exp(−(${number(difference, 4)})))`, `= ${percent(probability)}`]), make('p', `원정 ${names[a.team] || a.team} 승리 ${percent(1 - probability)} · 표시값은 반올림했으며 계산에는 원래 정밀도를 사용합니다.`, 'fm-help'));
    };
    home.addEventListener('change', update);
    away.addEventListener('change', update);
    section.append(controls, cards, result, make('p', '이 예시는 무승부를 제외한 1경기 승리 확률입니다. 순위표의 포스트시즌 진출확률은 남은 모든 경기를 반복해 계산한 별도 값입니다.', 'fm-example-note'));
    update();
    return section;
  }
  function validation() {
    const section = make('section', undefined, 'fm-validation');
    section.append(make('h3', '보정 강도는 과거 기록으로 선택했습니다'), make('p', '2020~2023년 학습 → 2024년 모델 선택 → 값을 고정한 뒤 2025년 최종 평가. 총 4,320경기를 사용했으며, 예측 시점 이후 결과는 팀 전력 계산에 넣지 않았습니다.', 'fm-help'));
    const table = make('table', undefined, 'fm-table');
    const caption = make('caption', '2025년 최종 평가 · 두 지표 모두 낮을수록 좋음');
    const thead = make('thead');
    const header = make('tr');
    for (const label of ['지표', '기존 모델', '보정 모델']) {
      const th = make('th', label);
      th.scope = 'col';
      header.append(th);
    }
    thead.append(header);
    const tbody = make('tbody');
    for (const [label, previous, calibrated] of [['경기 예측 로그손실', '0.702731', '0.686922'], ['진출확률 Brier 오차', '0.216749', '0.205845']]) {
      const tr = make('tr');
      const th = make('th', label);
      th.scope = 'row';
      tr.append(th, make('td', previous), make('td', calibrated, 'fm-better'));
      tbody.append(tr);
    }
    table.append(caption, thead, tbody);
    section.append(table, make('p', '개선의 대부분은 평균 회귀에서 나왔습니다. 진출확률 오차는 6개 시점의 평균이며, 시즌 후반 일부 시점은 기존 모델이 더 좋았습니다. 2024년 경기 예측은 단순 50:50 예측보다 좋지 않았고, 독립 최종 평가는 아직 한 시즌입니다.', 'fm-help'));
    return section;
  }
  window.renderForecastMethod = (container, data = {}) => {
    if (!container) return;
    const params = data.forecastParameters || {};
    const alpha = finite(params.opponentWeight) ? params.opponentWeight : 0.5;
    const prior = finite(params.priorGames) ? params.priorGames : 40;
    const home = finite(params.homeLogOdds) ? params.homeLogOdds : 0;
    const exponent = finite(data.exponent) ? data.exponent : 1.83;
    const trials = finite(data.simulations) && data.simulations > 0 ? data.simulations : 10000;
    const root = make('div', undefined, 'fm-method');
    root.append(make('p', '득실점에서 팀 전력을 추정하고, 남은 상대와 홈·원정 배정을 따라 시즌을 반복합니다.', 'fm-intro'));
    const badges = make('div', undefined, 'fm-parameters');
    for (const [label, value] of [['상대 수준 α', number(alpha, 1)], ['평균 회귀 k', number(prior)], ['홈 계수 H', number(home, 2)], ['시즌 반복', `${number(trials)}회`]]) {
      const badge = make('span');
      badge.append(document.createTextNode(`${label} `), make('strong', value));
      badges.append(badge);
    }
    root.append(badges);
    const steps = make('div', undefined, 'fm-steps');
    steps.append(
      step('1', '득실점으로 기본 전력 계산', '순위표의 피타고리안 승률은 아래 기본값을 그대로 보여줍니다.', [`pᵢ = RFᵢ^${exponent} / (RFᵢ^${exponent} + RAᵢ^${exponent})`, 'sᵢ = ln(pᵢ / (1 − pᵢ))'], 'RF는 득점, RA는 실점입니다. ln은 자연로그이며, s는 승률을 로그오즈로 바꾼 값입니다. 로그 계산에서는 p를 0.000001~0.999999로 제한합니다.'),
      step('2', '상대 수준과 경기 수로 보정', '경기 수가 적으면 평균에 더 가깝게, 강한 상대를 많이 만났다면 이를 반영해 전력을 추정합니다.', ['λᵢ = Gᵢ / (Gᵢ + k)', 'zᵢ = λᵢ × [sᵢ + α × Σⱼ(Nᵢⱼ / Gᵢ)zⱼ − H × vᵢ]', 'rᵢ = zᵢ − 전체 팀 z의 평균'], 'G는 승+무+패, Nᵢⱼ는 두 팀이 이미 치른 경기 수, v는 (이미 치른 홈경기−원정경기)/G입니다. 10개 팀의 식을 함께 풉니다. 상대 승률을 정확히 역산하는 대신 로그오즈에서 선형 근사하는 방식입니다.'),
      step('3', '남은 경기별 승리 확률 계산', '보정된 두 팀의 전력 차이에 홈 계수를 더합니다.', ['P(홈 승리) = 1 / (1 + exp(−(r홈 − r원정 + H)))', 'P(원정 승리) = 1 − P(홈 승리)'], `exp(x)는 e의 x제곱입니다. ${home === 0 ? '모델 선택에 쓴 2024년 경기 예측에서 H=0이 선택됐습니다. 홈 이점이 없다는 결론이 아니라, 이 기준에서는 홈 보정을 더한 후보가 개선을 보이지 않아 기본값에 반영하지 않았다는 뜻입니다.' : `현재 적용하는 홈 계수 H는 ${number(home, 2)}입니다.`}`),
      step('4', '시즌을 반복해 상위 5팀 집계', '현재 승·무·패는 유지하고, 2026 공식 홈·원정 배정에서 이미 치른 경기를 뺀 뒤 남은 경기를 예측합니다.', ['최종 승률 = 최종 승 / (144 − 현재 무)', `진출확률 = (1 / ${number(trials)}) × Σₜ cᵢ,ₜ`], '각 반복에서 5위 경계보다 높으면 진출 점수 c=1, 낮으면 0입니다. 경계에 여러 팀이 동률이면 남은 진출 자리를 동률 팀 수로 나눠 받습니다. 예: 4위까지 확정되고 3팀이 공동 5위면 각 1/3점입니다.')
    );
    root.append(steps);
    const rows = Array.isArray(data.forecastDetails) ? data.forecastDetails.filter(row => row && typeof row.team === 'string' && finite(row.rating) && finite(row.regressionWeight) && finite(row.pyth) && finite(row.g) && finite(row.rf) && finite(row.ra)) : [];
    if (data.forecastAvailable !== false && rows.length >= 2) root.append(example(rows, home));
    else root.append(make('p', '현재 조회에는 보정 전력의 상세값이 없어 팀별 계산 예시를 표시하지 않습니다. 진출확률 제공 조건을 충족하는 2026 시즌 조회에서 확인할 수 있습니다.', 'fm-unavailable'));
    root.append(validation());
    const limitations = make('div', undefined, 'fm-limitations');
    limitations.append(make('strong', '해석할 때 참고하세요'), make('p', '남은 경기의 무승부, 부상·선발투수·최근 엔트리 변화, 실제 동률 결정전은 별도로 예측하지 않습니다. 각 반복에서 팀 전력은 고정됩니다. 표본에 따라 보정값과 정확도는 달라질 수 있으며, KBO의 공식 진출확률이 아닙니다.'));
    const sources = make('p', undefined, 'fm-sources');
    sources.append(externalLink('공식·검증 결과 전체 보기 ↗', 'https://github.com/vjhjv111/KBOSabermetrics/blob/main/WEB/docs/PLAYOFF-MODEL.md'), document.createTextNode(' · '), externalLink('2026 KBO 공식 일정 ↗', 'https://www.koreabaseball.com/MediaNews/Notice/View.aspx?bdSe=11794'));
    root.append(limitations, sources);
    container.replaceChildren(root);
  };
})();
