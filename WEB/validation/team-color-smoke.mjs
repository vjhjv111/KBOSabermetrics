// Run with Playwright on NODE_PATH, or set PLAYWRIGHT_MODULE to its package path.
// BASE_URL defaults to the local QA server; output is kept outside deployable assets.
import {createRequire} from 'node:module';
import fs from 'node:fs/promises';
import path from 'node:path';
import assert from 'node:assert/strict';
const require=createRequire(import.meta.url);
const {chromium}=require(process.env.PLAYWRIGHT_MODULE||'playwright');
const base=process.env.BASE_URL||'http://127.0.0.1:5111';
const output=path.resolve(process.env.QA_OUTPUT||'../team-color-qa');
const expected={HH:'#b94410',HT:'#bf2538',LG:'#a61847',LT:'#b72740',SS:'#1b5da5',SK:'#bb2639',WO:'#71263c',KT:'#252931',NC:'#224b79',OB:'#192b47',neutral:'#536174'};
const rgb=hex=>'rgb('+hex.slice(1).match(/../g).map(v=>parseInt(v,16)).join(', ')+')';
function contrast(hex){const c=hex.slice(1).match(/../g).map(v=>parseInt(v,16)/255).map(v=>v<=.04045?v/12.92:((v+.055)/1.055)**2.4);return 1.05/(.05+c.reduce((n,v,i)=>n+v*[.2126,.7152,.0722][i],0));}
await fs.mkdir(output,{recursive:true});
const report={base,started:new Date().toISOString(),palette:Object.fromEntries(Object.entries(expected).map(([k,v])=>[k,{hex:v,contrastOnWhite:contrast(v)}])),checks:[],errors:[]};
const browser=await chromium.launch({headless:true,...(process.env.CHROME_PATH?{executablePath:process.env.CHROME_PATH}:{})});
try{
  for(const width of [1440,390]){
    const context=await browser.newContext({viewport:{width,height:960},hasTouch:width===390,isMobile:width===390,deviceScaleFactor:1});
    const page=await context.newPage(),errors=[];page.on('pageerror',error=>errors.push(error.message));page.setDefaultTimeout(60000);
    const activate=locator=>width===390?locator.tap():locator.click();
    async function check(name,selector,screenshot=true){
      const found=await page.locator(selector+' .team-name[data-team], '+selector+'.team-name[data-team]').evaluateAll(nodes=>nodes.filter(n=>n.getBoundingClientRect().width&&n.tagName!=='OPTION').map(n=>({team:n.dataset.team,text:n.textContent,color:getComputedStyle(n).color,fill:getComputedStyle(n).fill,tag:n.tagName})));
      assert.ok(found.length,`${width} ${name} has no semantic team labels`);
      for(const item of found){assert.ok(expected[item.team],`Unknown class ${item.team}`);assert.equal(item.color,rgb(expected[item.team]),`${width} ${name} ${item.text}`);if(item.tag==='text')assert.equal(item.fill,rgb(expected[item.team]));}
      const overflow=await page.evaluate(()=>({viewport:innerWidth,document:document.documentElement.scrollWidth}));assert.ok(overflow.document<=width+1,`${name} body overflow ${overflow.document}`);
      report.checks.push({width,name,labelCount:found.length,teams:[...new Set(found.map(x=>x.team))],samples:found.slice(0,15),overflow});
      if(screenshot){await page.locator(selector).first().scrollIntoViewIfNeeded();await page.screenshot({path:path.join(output,`${width}-${name}.png`)});}
    }
    await page.goto(base+'/?preview=team-colors');await page.locator('#home-standings tbody tr').first().waitFor();await page.locator('#home-war .home-player a').first().waitFor();
    const allTeams=await page.locator('#home-standings .team-name').evaluateAll(nodes=>[...new Set(nodes.map(n=>n.dataset.team))]);assert.equal(allTeams.length,10,'Home must contain all 10 actual team colors');
    await check('home','#home-page');await check('monthly','#home-monthly',false);await check('latest-results','#home-results',false);
    const aliases=await page.evaluate(()=>{
      const samples=['SSG','SK 와이번스','SSG/SK','HT','해태 타이거즈','넥센 히어로즈','키움','삼성 라이온즈','LG 트윈스','두산','빙그레','알 수 없는 팀'];
      const host=document.createElement('div');host.id='team-color-test-only';document.body.append(host);
      const results=samples.map(name=>{const node=teamNameNode(name,name);host.append(node);return {name,team:node.dataset.team,label:node.textContent,color:getComputedStyle(node).color};});
      const multiple=teamNamesNode('SS,LG,SK');host.append(multiple);results.push({name:'multiple',teams:[...multiple.querySelectorAll('.team-name')].map(x=>x.dataset.team),text:multiple.textContent});host.remove();return results;
    });
    const aliasCodes=['SK','SK','SK','HT','HT','WO','WO','SS','LG','OB','HH','neutral'];for(let i=0;i<aliasCodes.length;i++){assert.equal(aliases[i].team,aliasCodes[i]);assert.equal(aliases[i].label,aliases[i].name);assert.equal(aliases[i].color,rgb(expected[aliasCodes[i]]));}assert.deepEqual(aliases.at(-1).teams,['SS','LG','SK']);report.checks.push({width,name:'aliases-and-multiple-teams',aliases});
    if(width===390){const nav=await page.locator('.room-nav .room').evaluateAll(nodes=>nodes.map(n=>({text:n.textContent,...n.getBoundingClientRect().toJSON()})));assert.equal(nav.length,8);for(const n of nav)assert.ok(n.height>=44&&n.left>=0&&n.right<=391,`${n.text} navigation tap target`);}
    const playerHref=await page.locator('#home-war .home-player a').first().getAttribute('href');
    await activate(page.getByRole('button',{name:'시즌기록실',exact:true}));await page.locator('#records tbody tr').first().waitFor();await check('records','#workspace');
    await page.locator('#team').selectOption('SS');assert.equal(await page.locator('#team').evaluate(n=>getComputedStyle(n).color),rgb(expected.SS));await page.locator('#detail-toggle').click();await page.locator('#reset').click();assert.equal(await page.locator('#team').getAttribute('data-team'),null,'Reset must remove previous team color');
    await page.evaluate(hash=>location.hash=hash,playerHref);await page.locator('#player-bio .team-name').first().waitFor();await page.waitForFunction(()=>document.querySelector('#player-content')?.children.length>0);await check('player','#player-page');
    await activate(page.getByRole('button',{name:'날짜별',exact:true}));await page.locator('#player-content tbody tr').first().waitFor();await check('player-games','#player-content',false);
    await page.evaluate(()=>location.hash='team=SS&year=2026');await page.locator('#tp-content .team-grid').waitFor();await check('team','#team-page');
    await activate(page.getByRole('button',{name:'경기기록',exact:true}));await page.locator('.calendar-game').first().waitFor();await check('calendar','#game-page');
    await activate(page.locator('.calendar-game').first());await page.locator('.game-probability').waitFor();await check('game','#game-page');
    const titleBackgrounds=await page.locator('#game-page h2:has(.team-name)').evaluateAll(nodes=>nodes.map(node=>{let ancestor=node,background='';while(ancestor){background=getComputedStyle(ancestor).backgroundColor;if(background!=='rgba(0, 0, 0, 0)'&&background!=='transparent')break;ancestor=ancestor.parentElement;}return {title:node.textContent,background};}));
    assert.equal(titleBackgrounds.length,4);for(const item of titleBackgrounds)assert.equal(item.background,'rgb(243, 246, 250)',`Team heading requires the pale background: ${item.title}`);report.checks.push({width,name:'team-heading-effective-backgrounds',titleBackgrounds});
    await page.evaluate(()=>location.hash='analysis=workload');await page.waitForFunction(()=>document.querySelector('#analysis-status')?.textContent.includes('조회 완료'));await check('analysis','#analysis-page');
    await page.locator('#an-team').selectOption('SS');await page.waitForFunction(()=>document.querySelector('#analysis-status')?.textContent.includes('조회 완료'));assert.equal(await page.locator('#an-team').evaluate(n=>getComputedStyle(n).color),rgb(expected.SS));
    if(width===390){assert.equal(await page.locator('#an-team').evaluate(n=>getComputedStyle(n).fontSize),'16px');const targets=await page.locator('#analysis-form button,#analysis-form select').evaluateAll(nodes=>nodes.filter(n=>n.getBoundingClientRect().width).map(n=>n.getBoundingClientRect().height));assert.ok(targets.every(n=>n>=44));}
    assert.deepEqual(errors,[]);report.checks.push({width,name:'browser-console',errors});console.log(`PASS ${width}: all 10 colors, aliases, home/records/player/team/game/analysis, responsive controls`);await context.close();
  }
  report.status='PASS';
}catch(error){report.status='FAIL';report.errors.push(error.stack||String(error));throw error;}
finally{report.finished=new Date().toISOString();await fs.writeFile(path.join(output,'report.json'),JSON.stringify(report,null,2));await browser.close();}
