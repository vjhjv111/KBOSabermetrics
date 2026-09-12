import { createRequire } from 'node:module';
import fs from 'node:fs/promises';
import assert from 'node:assert/strict';

const require = createRequire(import.meta.url);
const { chromium } = require('playwright');
const base = process.env.QA_URL || 'http://127.0.0.1:5111';
const out = process.env.QA_OUTPUT || './analysis-qa';
await fs.mkdir(out, { recursive: true });
const browser = await chromium.launch({ headless: true,
  ...(process.env.CHROMIUM_EXECUTABLE ? { executablePath: process.env.CHROMIUM_EXECUTABLE } : {}) });
const report = { api: [], browser: [] };
const sections = ['zones', 'sequences', 'trend', 'times', 'workload', 'expectancy', 'replay', 'provenance'];
try {
  const context = await browser.newContext();
  const page = await context.newPage();
  await page.goto(base + '/#analysis');
  await page.waitForFunction(() => typeof analysisState !== 'undefined' && analysisState.result?.section === 'zones', null, { timeout: 60000 });
  const request = body => page.evaluate(async body => {
    const start = performance.now();
    const result = await api('/api/analysis', body);
    return { result, elapsed: Math.round(performance.now() - start) };
  }, body);
  const pitchers = (await request({section:'catalog',year:2026,role:'pitcher'})).result.tables.find(t => t.key === 'players').rows;
  const batters = (await request({section:'catalog',year:2026,role:'batter'})).result.tables.find(t => t.key === 'players').rows;
  const pitcher = pitchers.find(p => p.Code === '68220');
  const batter = batters.find(p => p.Name === '김도영');
  assert(pitcher && batter, 'Real 2026 validation players are present');
  for (const section of sections) {
    const role = ['zones','provenance','replay'].includes(section) ? 'batter' : 'pitcher';
    const code = section === 'expectancy' || section === 'workload' ? '' : role === 'batter' ? batter.Code : pitcher.Code;
    const { result, elapsed } = await request({section,year:2026,role,code,window:5});
    assert.equal(result.section, section);
    assert(result.tables.length > 0, section + ' tables');
    assert(result.tables.every(t => t.rows.length <= 500), section + ' bounded response');
    if (section === 'zones') assert.equal(result.tables.find(t=>t.key==='zones').rows.length,25);
    if (section === 'expectancy') assert.equal(result.chart.states.length,24);
    if (section === 'replay') assert(result.chart.pitches.some(p=>p.points.length===25), 'Real replay contains valid trajectory');
    if (section === 'provenance') assert(result.tables.find(t=>t.key==='boxComparisons').rows.some(r=>r.Official!==null), 'Stored official box fields present');
    report.api.push({section,elapsed,tables:result.tables.map(t=>({key:t.key,rows:t.rows.length}))});
    console.log('PASS API',section,elapsed+'ms');
  }
  for (const body of [{section:'bad'}, {section:'zones',year:2026,code:"' OR 1=1--"}, {section:'replay',page:0}, {section:'trend',start:'2026-09-11',end:'2026-03-28'}, {section:'zones',count:'4-3'}]) {
    const response = await page.evaluate(async body => {
      const s = await fetch('/api/session').then(r=>r.json());
      const r = await fetch('/api/analysis', {method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':s.csrfToken},body:JSON.stringify(body)});
      return r.status;
    },body);
    assert.equal(response,400,'Reject invalid analysis request');
  }
  await context.close();
  for (const width of [390,1440]) {
    const context = await browser.newContext({viewport:{width,height:900},hasTouch:width<600,isMobile:width<600});
    const page = await context.newPage(), errors=[];
    page.on('pageerror',e=>errors.push(e.message));
    page.on('console',m=>{if(m.type()==='error'&&m.text().includes('Content Security Policy'))errors.push(m.text());});
    await page.goto(base+'/#analysis');
    await page.waitForFunction(()=>analysisState.result?.section==='zones' && document.querySelector('#analysis-form').getAttribute('aria-busy')==='false',null,{timeout:60000});
    await page.selectOption('#an-code',pitcher.Code);
    await page.locator('#an-submit').click();
    await page.waitForFunction(()=>document.querySelector('#analysis-status').textContent.includes('조회 완료') && document.querySelector('#analysis-form').getAttribute('aria-busy')==='false');
    for (const section of sections) {
      if(section!=='zones') {
        await page.locator(`.analysis-tabs a[href="#analysis=${section}"]`).click();
        await page.waitForFunction(s=>analysisState.result?.section===s && document.querySelector('#analysis-form').getAttribute('aria-busy')==='false',section,{timeout:60000});
      }
      assert(await page.locator('#analysis-page').isVisible());
      assert.equal(await page.locator('.analysis-error').count(),0,section+' no error');
      const metrics = await page.evaluate(()=>({viewport:innerWidth,doc:document.documentElement.scrollWidth,cards:document.querySelectorAll('#analysis-content .analysis-card').length}));
      assert(metrics.doc<=width+1,`${width} ${section} horizontal overflow ${metrics.doc}`);
      assert(metrics.cards>0,section+' renders cards');
      if(width===390) {
        const inputs=await page.locator('#analysis-form input:visible,#analysis-form select:visible').evaluateAll(es=>es.map(e=>({font:getComputedStyle(e).fontSize,height:e.getBoundingClientRect().height})));
        for(const input of inputs){assert.equal(input.font,'16px');assert(input.height>=44);}
      }
      if(section==='zones') {
        await page.locator('.analysis-zone-cell').first().click();
        assert.equal(await page.locator('.analysis-zone-cell[aria-pressed=true]').count(),1);
        await page.locator('.analysis-chart-control select').first().selectOption('SLG');
      }
      if(section==='trend') {
        await page.locator('.analysis-chart-control select').first().selectOption('RollingKPct');
        assert(await page.locator('.analysis-chart .chart-series').count()>0);
      }
      if(section==='replay') {
        await page.locator('.analysis-row-action').first().click();
        await page.waitForFunction(()=>document.querySelector('#analysis-form').getAttribute('aria-busy')==='false');
        const slider=page.locator('.analysis-replay-slider');
        await slider.fill('500');
        assert.match(await slider.getAttribute('aria-valuetext'),/50/);
        await page.getByRole('button',{name:'▶ 재생',exact:true}).click();
        await page.waitForFunction(()=>Number(document.querySelector('.analysis-replay-slider').value)===1000);
      }
      if(section==='provenance') {
        const choose=page.locator('.analysis-row-action').filter({hasText:'이 경기 대조'});
        assert(await choose.count()>0);
        await choose.nth(Math.min(1,await choose.count()-1)).click();
        await page.waitForFunction(()=>document.querySelector('#analysis-form').getAttribute('aria-busy')==='false');
        assert((await page.locator('#an-gameId').inputValue()).length>0);
      }
      await page.locator('#analysis-page').scrollIntoViewIfNeeded();
      await page.screenshot({path:`${out}/${width}-${section}.png`,fullPage:false});
      report.browser.push({width,section,...metrics});
    }
    // Existing navigation and game must survive the new route and its animation lifecycle.
    await page.getByRole('button',{name:'시즌기록실',exact:true}).click();
    await page.locator('#records tbody tr').first().waitFor({timeout:60000});
    assert(await page.locator('#analysis-page').isHidden());
    await page.getByRole('button',{name:'야구게임',exact:true}).click();
    await page.frameLocator('#diamond-frame').locator('.enter-button:enabled').waitFor({timeout:60000});
    assert(await page.locator('#analysis-page').isHidden());
    assert.deepEqual(errors,[]);
    await context.close();
    console.log('PASS',width,'all eight panels and existing records/game');
  }
  await fs.writeFile(out+'/report.json',JSON.stringify(report,null,2));
} finally { await browser.close(); }
