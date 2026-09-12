import {createRequire} from 'node:module';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
const here=path.dirname(fileURLToPath(import.meta.url));
const require=createRequire(import.meta.url);
const {build}=require(path.resolve(here,'../../diamond-game/node_modules/esbuild'));
const lib=path.resolve(process.argv[2]??path.resolve(here,'../../diamond-game/lib'));
const output=path.resolve(process.argv[3]??path.join(here,'fixtures.json'));
const temp=fs.mkdtempSync(path.join(os.tmpdir(),'diamond-engine-fixtures-'));
const compiled=path.join(temp,'engine.cjs');
const bundled=await build({entryPoints:[path.join(lib,'action-engine.ts')],bundle:true,platform:'node',format:'cjs',write:false,
 tsconfigRaw:{compilerOptions:{target:'ES2022'}},plugins:[{name:'fixture-source',setup(b){
  b.onResolve({filter:/.*/},args=>{
   const target=path.isAbsolute(args.path)?args.path:path.resolve(path.dirname(args.importer),args.path);
   const relative=path.relative(lib,target);if(relative.startsWith('..')||path.isAbsolute(relative))throw new Error('Fixture import outside original lib');
   const file=[target,target+'.ts',target+'.json'].find(p=>fs.existsSync(p)&&fs.statSync(p).isFile());
   if(!file)throw new Error('Missing fixture dependency '+target);
   return {path:file,namespace:'fixture-source'};
  });
  b.onLoad({filter:/.*/,namespace:'fixture-source'},args=>({contents:fs.readFileSync(args.path,'utf8'),loader:args.path.endsWith('.json')?'json':'ts'}));
 }}]});
fs.writeFileSync(compiled,bundled.outputFiles[0].contents);
let seed=1;
Object.defineProperty(globalThis,'crypto',{configurable:true,value:{getRandomValues(a){for(let i=0;i<a.length;i++){seed=(Math.imul(seed,1664525)+1013904223)>>>0;a[i]=seed;}return a;}}});
const engine=require(compiled);
const players=JSON.parse(fs.readFileSync(path.join(lib,'players.json'),'utf8'));
const cases=[];let index=0;
for(const pitcher of players.pitchers){
 for(const arsenal of engine.arsenal(pitcher.id)){
  const batter=players.batters[index%players.batters.length];
  const game={format:'action-v2',code:'DABCDEFG',mode:index%2?'pvp':'ai',host:'host',guest:null,hostRole:'batter',batter:batter.id,pitcher:pitcher.id,pace:['practice','real','full'][index%3],round:0,balls:0,strikes:0,score:0,pitchCount:0,pitch:null,history:[],createdAt:1700000000000,expiresAt:1700086400000};
  const initialSeed=1100+index;seed=initialSeed;
  const aim={x:[0,-1.6,1.6,.8][index%4],y:[0,.6,-.4,1.5][index%4]},quality=.65;
  game.pitch=engine.createPitch(game,arsenal.type,aim,quality,1700000000000);
  const arrival=game.pitch.releaseAt+game.pitch.flightMs;
  const swings=[null,{at:arrival,aim:{...game.pitch.target}},{at:arrival-175,aim:{...game.pitch.target}},{at:arrival+170,aim:{...game.pitch.target}},{at:arrival,aim:{x:2,y:2}},{at:arrival+65,aim:{x:game.pitch.target.x+.6,y:game.pitch.target.y}},{at:arrival,aim:{x:game.pitch.target.x,y:game.pitch.target.y+.5}}];
  const results=swings.map(s=>engine.evaluatePitch(game,s,Math.round(arrival+400)));
  const aiSeed=initialSeed+991;seed=aiSeed;const ai=engine.aiSwing(game);
  cases.push({game,seed:initialSeed,type:arsenal.type,aim,quality,now:1700000000000,pitch:game.pitch,attributes:engine.attributes(batter.id,pitcher.id),positions:[0,.5,1,1.065].map(t=>engine.ballPosition(game.pitch,game.pitch.releaseAt+game.pitch.flightMs*t)),swings,results,aiSeed,ai});index++;
 }
}
fs.writeFileSync(output,JSON.stringify({source:'Original action-engine.ts bundled without modifications',cases},null,2));
console.log(`Wrote ${cases.length} pitch cases / ${cases.length*7} results to ${output}`);
