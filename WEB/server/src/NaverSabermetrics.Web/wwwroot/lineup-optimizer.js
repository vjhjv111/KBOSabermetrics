// 라인업 최적화 페이지. app.js가 먼저 로드되어 있어야 하며 $, text, api, playerCard,
// teamNameNode, teamNames, abortQuery 전역 헬퍼를 그대로 재사용합니다(games.js와 같은 패턴).
//
// 레이아웃: 팀 하나를 고르는 드롭다운 대신, 선택한 날짜의 "경기별" 카드를 경기일정 페이지의
// 달력 카드(.calendar-day/.calendar-game)와 같은 시각 언어로 세로로 나열합니다. 각 카드는 그
// 경기의 두 팀을 나란한 서브컬럼으로 담아 좌우로 페이지 전체를 쓰지 않게 합니다. 팀 필터는
// 선택 사항이며 이미 그려진 카드를 client-side로 숨기고 보여줄 뿐, 다시 불러오지 않습니다.
const lineupState = { mainController: null, columns: new Map(), filter: '' };
const lineupRoot = text('main', '', 'player-page lineup-page');
lineupRoot.id = 'lineup-page';
lineupRoot.hidden = true;
$('workspace').before(lineupRoot);

const lineupNav = text('button', '라인업 최적화', 'room');
lineupNav.dataset.route = 'lineup';
lineupNav.onclick = () => { location.hash = 'lineup'; };
document.querySelector('.room-nav').append(lineupNav);

window.addEventListener('hashchange', lineupRoute);

function lineupRoute() {
  const active = location.hash === '#lineup';
  lineupRoot.hidden = !active;
  if (!active) { stopAllLineupColumns(); return; }
  for (const id of ['workspace', 'home-page', 'player-page', 'team-page', 'game-page', 'analysis-page', 'comparison-page'])
    { const el = $(id); if (el) el.hidden = true; }
  abortQuery();
  if (!lineupRoot.childElementCount) renderLineupForm();
}

function todayDateValue() {
  // 화면 기본값은 대략적인 오늘 날짜면 충분합니다(정확한 기준은 서버가 date 생략 시 한국시간으로 처리).
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`;
}

function renderLineupForm() {
  document.title = '라인업 최적화 · FANZAI';
  lineupRoot.replaceChildren();
  const breadcrumb = text('div', '', 'player-breadcrumb');
  breadcrumb.append(text('span', '라인업 최적화'));
  const heading = text('h1', '오늘의 최적 타순');
  const intro = text('p', '상대 선발투수의 던지는 손에 맞춘 타자별 성적을 바탕으로, 두 가지 방식으로 타순을 제안합니다. 휴리스틱은 즉시 나오고, 전수조사는 9!(362,880가지) 타순을 모두 계산하느라 30~60초 정도 걸릴 수 있습니다. 전수조사의 기대 득점은 단순화된 모델의 상대적 수치이며, 같은 9명의 서로 다른 타순끼리 비교하는 용도로만 참고하세요 — 실제 팀이 이만큼 득점한다는 예측이 아닙니다.', 'player-note');
  const hero = text('div', '', 'player-hero');
  hero.style.flexWrap = 'wrap';
  const form = document.createElement('form');
  form.className = 'player-controls';
  form.autocomplete = 'off';
  form.onsubmit = (e) => e.preventDefault();

  const dateLabel = text('label', '날짜');
  const dateInput = document.createElement('input');
  dateInput.type = 'date'; dateInput.id = 'lineup-date'; dateInput.value = todayDateValue();
  dateLabel.append(dateInput);
  dateInput.onchange = () => { if (dateInput.value) loadLineupDay(dateInput.value); };

  const filterLabel = text('label', '팀 필터(선택)');
  const filterSelect = document.createElement('select');
  filterSelect.id = 'lineup-filter';
  filterSelect.append(new Option('전체 경기', ''));
  filterLabel.append(filterSelect);
  filterSelect.onchange = () => { lineupState.filter = filterSelect.value; applyLineupFilter(); };

  form.append(dateLabel, filterLabel);
  hero.append(form);

  lineupRoot.append(breadcrumb, heading, intro, hero, text('div', '', 'lineup-games'));
  loadLineupDay(dateInput.value);
}

function lineupColumnKey(team, date) { return `${team}|${date}`; }

function stopLineupColumn(key) {
  const col = lineupState.columns.get(key);
  if (!col) return;
  col.controller?.abort();
  if (col.pollTimer) clearTimeout(col.pollTimer);
  lineupState.columns.delete(key);
}

function stopAllLineupColumns() {
  lineupState.mainController?.abort();
  for (const key of [...lineupState.columns.keys()]) stopLineupColumn(key);
}

// 선택한 날짜의 경기 목록은 이미 채워지고 있는 /api/lineup/today(일자별 선발 라인업) 응답에서
// 뽑아냅니다 — 팀별로 opponent(상대팀 코드)·gameId를 주므로 같은 gameId(없으면 상대 코드 쌍)를
// 묶으면 그 날의 "경기별" 목록이 됩니다. 경기일정 달력을 새로 조회할 필요가 없습니다.
function lineupGamesFromToday(data) {
  const teams = data?.teams ?? [];
  const games = [];
  const seen = new Set();
  for (const t of teams) {
    if (seen.has(t.team)) continue;
    const opponent = t.opponent || null;
    const pairKey = t.gameId ? `game:${t.gameId}` : `pair:${[t.team, opponent].filter(Boolean).sort().join('-')}`;
    if (seen.has(pairKey)) continue;
    seen.add(pairKey); seen.add(t.team);
    if (opponent) seen.add(opponent);
    games.push({ a: t.team, b: opponent, gameId: t.gameId ?? null });
  }
  return games;
}

async function loadLineupDay(date) {
  stopAllLineupColumns();
  const gamesBox = lineupRoot.querySelector('.lineup-games');
  const filterSelect = $('lineup-filter');
  gamesBox.replaceChildren(text('p', '불러오는 중…', 'player-note'));
  lineupState.mainController = new AbortController();
  const signal = lineupState.mainController.signal;
  try {
    const data = await api(`/api/lineup/today?date=${encodeURIComponent(date)}`, undefined, signal);
    const games = lineupGamesFromToday(data);
    renderLineupGames(gamesBox, filterSelect, games, date);
  } catch (e) {
    if (e.name === 'AbortError') return;
    gamesBox.replaceChildren(text('p', e.message, 'player-note'));
  }
}

function renderLineupGames(gamesBox, filterSelect, games, date) {
  gamesBox.replaceChildren();
  lineupState.filter = '';
  if (filterSelect) {
    filterSelect.replaceChildren(new Option('전체 경기', ''));
    for (const g of games) for (const code of [g.a, g.b]) {
      if (!code || filterSelect.querySelector(`option[value="${code}"]`)) continue;
      filterSelect.append(new Option(teamNames[code] ?? code, code));
    }
  }
  if (!games.length) {
    gamesBox.append(text('p', '이 날짜에는 확인된 라인업 정보가 없습니다. 다른 날짜를 선택해 보세요.', 'player-note'));
    return;
  }
  for (const game of games) {
    const card = renderLineupGameCard(game, date);
    gamesBox.append(card);
  }
}

function applyLineupFilter() {
  const filter = lineupState.filter;
  for (const card of lineupRoot.querySelectorAll('.lineup-game-card')) {
    const teams = (card.dataset.teams ?? '').split(',');
    card.hidden = !!filter && !teams.includes(filter);
  }
}

function renderLineupGameCard(game, date) {
  const card = text('section', '', 'player-card lineup-game-card');
  card.dataset.teams = [game.a, game.b].filter(Boolean).join(',');
  const header = text('h2', '');
  header.append(teamNameNode(game.a));
  header.append(document.createTextNode(game.b ? ' vs ' : ' · 상대 미정'));
  if (game.b) header.append(teamNameNode(game.b));
  header.append(text('span', date, 'lineup-game-meta'));
  card.append(header);

  const body = text('div', '', 'lineup-game-body');
  const colA = text('div', '', 'lineup-team-col');
  body.append(colA);
  if (game.b) {
    const colB = text('div', '', 'lineup-team-col');
    body.append(colB);
    loadLineupColumn(game.b, date, colB);
  }
  card.append(body);
  loadLineupColumn(game.a, date, colA);
  applyLineupFilter();
  return card;
}

async function loadLineupColumn(team, date, colBox) {
  const key = lineupColumnKey(team, date);
  stopLineupColumn(key);
  const controller = new AbortController();
  lineupState.columns.set(key, { controller, pollTimer: null, pollAttempt: 0 });
  colBox.replaceChildren(text('h3', teamNames[team] ?? team), text('p', '불러오는 중…', 'player-note'));
  markTeamName(colBox.querySelector('h3'), team);
  try {
    const data = await api(`/api/lineup/optimal?team=${encodeURIComponent(team)}&date=${encodeURIComponent(date)}`, undefined, controller.signal);
    if (!lineupState.columns.has(key)) return;
    renderLineupColumn(colBox, team, date, data, key);
  } catch (e) {
    if (e.name === 'AbortError') return;
    colBox.replaceChildren(text('h3', teamNames[team] ?? team), text('p', e.message, 'player-note'));
    markTeamName(colBox.querySelector('h3'), team);
  }
}

function renderLineupColumn(colBox, team, date, data, key) {
  colBox.replaceChildren();
  const heading = text('h3', teamNames[team] ?? team);
  markTeamName(heading, team);
  colBox.append(heading);
  if (!data.available) {
    colBox.append(text('p', data.reason ?? '오늘은 이 팀의 타순을 계산할 수 없습니다.', 'player-note'));
    return;
  }

  const opponentLine = text('p',
    data.opponent?.team
      ? `상대: ${teamNames[data.opponent.team] ?? data.opponent.team} · 예상 선발 ${data.opponent.pitcher ?? '미정'}${data.opponent.pitcherHand ? ` (${data.opponent.pitcherHand === 'L' ? '좌투' : '우투'})` : ''}`
      : '상대 선발 정보를 아직 확인할 수 없습니다.', 'player-note');
  const sourceLabel = { roster_combo_best9: '등록 로스터 중 상성 최적 조합', official_lineup: '공식 라인업', probable_lineup: '예고 라인업', previous_game_lineup: '전날 로스터 기준', recent_starters_fallback: '최근 30일 출장 빈도 기준 추정' }[data.batterPoolSource] ?? data.batterPoolSource;
  const sourceLine = text('p', `타자 명단: ${sourceLabel}${data.batterPoolSource === 'recent_starters_fallback' ? ' (근사치)' : ''}`, 'player-note');
  colBox.append(opponentLine, sourceLine);
  if (data.opponent?.pitcherFip != null) {
    const parts = [`상대 선발 FIP ${data.opponent.pitcherFip}`];
    if (data.opponent.pitcherFipVsLeft != null) parts.push(`좌타 상대 ${data.opponent.pitcherFipVsLeft}`);
    if (data.opponent.pitcherFipVsRight != null) parts.push(`우타 상대 ${data.opponent.pitcherFipVsRight}`);
    colBox.append(text('p', `${parts.join(' · ')} — 타순 계산에 반영됨`, 'player-note'));
  }

  // pcode → { position, woba } 맵. 알고리즘1/2의 order는 orderPcodes를 함께 주므로, 같은 pcode로
  // 조회해 타순 목록의 각 줄에 포지션과 wOBA를 함께 붙입니다(서버가 배터 하나당 한 번만 내려주면
  // 충분). position은 그 선수가 가장 최근에 실제로 출전했던 경기의 상세 수비 위치(예: "중견수")를
  // 우선하고, 그런 기록이 전혀 없을 때만 광범위한 프로필 포지션(예: "외야수")으로 폴백합니다 —
  // positionIsSpecific이 그 여부를 알려줍니다.
  const batterInfoMap = {};
  for (const b of data.batters ?? []) batterInfoMap[b.pcode] = { position: b.position, positionIsSpecific: !!b.positionIsSpecific, woba: b.woba };
  const col = lineupState.columns.get(key);
  if (col) col.batterInfoMap = batterInfoMap;

  // 알고리즘1/2를 나란히 두 좁은 열로 배치합니다(각 열이 좁아지는 만큼 이름-포지션-wOBA 사이의
  // 빈 공백도 자연히 줄어듭니다).
  const algoRow = text('div', '', 'lineup-algo-row');
  const algo1Col = text('div', '', 'lineup-algo-col');
  const algo2Col = text('div', '', 'lineup-algo-col');
  algoRow.append(algo1Col, algo2Col);
  colBox.append(algoRow);

  algo1Col.append(text('p', '알고리즘1 - 강한2번기준', 'lineup-subhead'),
    lineupOrderList(data.algorithm1?.order, data.algorithm1?.orderPcodes, batterInfoMap));
  if (data.algorithm1 && (data.algorithm1.expectedRuns != null || data.algorithm1.estimatedRuns != null))
    algo1Col.append(text('p', lineupRunsLine(data.algorithm1.expectedRuns, data.algorithm1.estimatedRuns), 'player-note'));

  const algo2Head = text('p', '알고리즘2 - 시뮬레이션(362880회)', 'lineup-subhead');
  const algo2Box = text('div', '', 'lineup-algo2');
  algo2Col.append(algo2Head, algo2Box);
  renderLineupAlgorithm2(algo2Box, team, date, data.algorithm2, key);
}

// 타순을 세로 목록으로 렌더링합니다(한 줄에 한 타순: 순번 · 이름 · 포지션 · wOBA). orderPcodes가
// 있으면 그걸로 batterInfoMap을 조회하고, 없으면(예: 아주 오래된 캐시 응답) 이름만 보여줍니다.
function lineupOrderList(order, orderPcodes, batterInfoMap) {
  const list = document.createElement('ol');
  list.className = 'lineup-order-list';
  const pcodes = orderPcodes ?? [];
  for (const [i, name] of (order ?? []).entries()) {
    const item = document.createElement('li');
    const info = batterInfoMap ? batterInfoMap[pcodes[i]] : null;
    const positionText = info?.position ? (info.position + (info.positionIsSpecific ? '' : '(범주)')) : '-';
    const wobaText = info && info.woba != null ? `wOBA ${info.woba.toFixed(3)}` : 'wOBA —';
    item.append(
      text('b', `${i + 1}번`, 'lineup-order-num'),
      text('span', name, 'lineup-order-name'),
      text('span', positionText, 'lineup-order-pos'),
      text('span', wobaText, 'lineup-order-woba'));
    list.append(item);
  }
  return list;
}

function renderLineupAlgorithm2(box, team, date, algorithm2, key) {
  box.replaceChildren();
  if (!algorithm2 || algorithm2.status === 'error') {
    box.append(text('p', '전수조사 계산 중 오류가 발생했습니다. 잠시 후 다시 조회해 주세요.', 'player-note'));
    return;
  }
  if (algorithm2.status === 'computing') {
    const loading = text('div', '', 'loading');
    loading.hidden = false;
    const spinner = text('span', '', 'spinner');
    loading.append(spinner, document.createTextNode(' 계산 중(최대 1분 내외)…'));
    box.append(loading);
    scheduleLineupPoll(box, team, date, key);
    return;
  }
  const batterInfoMap = lineupState.columns.get(key)?.batterInfoMap ?? {};
  box.append(lineupOrderList(algorithm2.order, algorithm2.orderPcodes, batterInfoMap));
  box.append(text('p', lineupRunsLine(algorithm2.expectedRuns, algorithm2.estimatedRuns), 'player-note'));
}

// 모델의 원래 "기대 득점"(같은 9명의 서로 다른 타순끼리 비교하는 용도의 상대값)과, 리그 평균 득점에
// 맞춰 선형 보정한 "실제 환산 예상득점"을 함께 보여줍니다. 보정값이 없으면(계수 계산 실패 등) 원래
// 값만 보여줍니다.
function lineupRunsLine(expectedRuns, estimatedRuns) {
  const modelPart = `기대 득점(모델값, 상대적 비교용): ${expectedRuns ?? '—'}`;
  if (estimatedRuns == null) return modelPart;
  return `${modelPart} · 실제 환산 예상득점(리그 평균 대비 단순 선형 보정, 참고용): ${estimatedRuns}점`;
}

function scheduleLineupPoll(box, team, date, key) {
  const col = lineupState.columns.get(key);
  if (!col) return;
  col.pollAttempt = (col.pollAttempt ?? 0) + 1;
  const delay = Math.min(2000 + col.pollAttempt * 500, 5000);
  col.pollTimer = setTimeout(async () => {
    if (!lineupState.columns.has(key) || location.hash !== '#lineup') return;
    try {
      const data = await api(`/api/lineup/optimal?team=${encodeURIComponent(team)}&date=${encodeURIComponent(date)}`, undefined, col.controller.signal);
      if (!lineupState.columns.has(key) || location.hash !== '#lineup') return;
      const batterInfoMap = {};
      for (const b of data.batters ?? []) batterInfoMap[b.pcode] = { position: b.position, positionIsSpecific: !!b.positionIsSpecific, woba: b.woba };
      col.batterInfoMap = batterInfoMap;
      renderLineupAlgorithm2(box, team, date, data.algorithm2, key);
    } catch (e) {
      if (e.name !== 'AbortError' && lineupState.columns.has(key)) scheduleLineupPoll(box, team, date, key);
    }
  }, delay);
}

lineupRoute();
