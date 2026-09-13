import assert from 'node:assert/strict';
import fs from 'node:fs';
import ts from 'typescript';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';
const THREE=createRequire(import.meta.url)('three');
const field=load('lib/field-play.ts'),motion=load('lib/player-motion.ts'),feedback=load('lib/pitch-feedback.ts'),appearance=load('lib/player-appearance.ts');
const positions=['DH','RF','CF','LF','SS','3B','2B','1B','C','P'];
const profiles=Object.fromEntries(positions.map(position=>[position,{position,heightCm:position==='C'?210:185,throws:position==='C'?'L':'R',bodyType:position==='C'?'power':'athletic'}]));
profiles.P.heightCm=188;
let result=field.defensiveAssignments(positions,'P',id=>profiles[id]);
assert.equal(result.catcher,'C');assert.deepEqual(result.fielders,field.DEFENSIVE_SLOTS);assert.equal(result.pitcher,'P');
const unknown=['u0','u1','u2','u3','u4','u5','u6','u7','u8'];
result=field.defensiveAssignments([...unknown,'CF','C','DH','P','CF'],'P',id=>profiles[id]);
assert.equal(result.catcher,'C');assert.equal(result.fielders[5],'CF');
assert.equal(new Set([result.catcher,...result.fielders]).size,8);assert(!result.fielders.includes('DH'));assert(!result.fielders.includes('P'));
assert.equal(result.fielders[0],'u0','Unknown profiles fill only unreserved slots');
assert.deepEqual(field.defensiveAssignments(['u0','u0','DH'],'P',id=>profiles[id]),{pitcher:'P',catcher:'u0',fielders:Array(7).fill(undefined)});
assert.deepEqual(field.defensiveAssignments([], 'P',()=>undefined),{pitcher:'P',catcher:undefined,fielders:Array(7).fill(undefined)});
assert.equal(field.defensiveAssignments(['custom'],'P',()=>({position:' cf '})).fielders[5],'custom');
for(const half of ['top','bottom']){const game={half,homeTeam:'LG',awayTeam:'HH',homeLineup:positions,awayLineup:unknown};assert.equal(field.fieldingTeam(game),half==='top'?'LG':'HH');assert.equal(field.defenseLineup(game),half==='top'?positions:unknown);}

// Execute the Scene's real appearance closure with render-free model stubs.
const sceneSource=fs.readFileSync('app/action-scene.tsx','utf8'),closure=sceneSource.slice(sceneSource.indexOf(' let appearanceKey='),sceneSource.indexOf(' const render=()=>'));
assert(closure.includes('defensiveAssignments'));
const js=ts.transpileModule(closure+'\nreturn refreshAppearance;', {compilerOptions:{target:ts.ScriptTarget.ES2022}}).outputText;
const model=()=>({root:new THREE.Group()}),batter=model(),pitcher=model(),catcher=model(),fielders=Array.from({length:7},model);
const inputs={batter,pitcher,catcher,fielders,bat:{},playerProfile:id=>profiles[id],batterStats:id=>{if(!profiles[id])throw Error('No profile');return {team:'LG',name:'선수 '+id}},pitcherStats:()=>({team:'LG',name:'투수'}),playerAppearance:appearance.playerAppearance,dressPlayer:(model,look,sign)=>{model.look=look;model.sign=sign},dressBat:()=>{},pitcherBodyScale:motion.pitcherBodyScale,profileThrowingHand:motion.profileThrowingHand,defenseLineup:field.defenseLineup,fieldingTeam:field.fieldingTeam,defensiveAssignments:field.defensiveAssignments};
const refresh=new Function(...Object.keys(inputs),js)(...Object.values(inputs));
const props={batterId:'RF',pitcherId:'P',view:{roster:{revision:'test'}},seasonGame:{half:'top',homeTeam:'LG',awayTeam:'HH',homeLineup:positions,awayLineup:unknown,playerStats:{}},appearances:{C:{bodyType:'lean',heightCm:160,jerseyNumber:'42',skinTone:'#654321',gloveColor:'#abcdef'},P:{heightCm:155}}};
refresh(props,1,1);
assert.equal(catcher.root.userData.playerId,'C');assert.equal(catcher.look.number,'42');assert.equal(catcher.look.bodyType,'power','Canonical profile bodyType must match the server collider');assert.equal(catcher.look.skinTone,'#654321');assert.equal(catcher.look.gloveColor,'#abcdef');
assert.equal(pitcher.look.heightCm,188,'The hand geometry must use the same roster height as the live pitcher scale, even when saved customization differs');
assert.equal(catcher.look.heightCm,210,'Equipment shape and the actual defender scale share the same canonical height');
assert.equal(catcher.sign,-1);assert.equal(catcher.root.scale.x,-210/185);assert.equal(catcher.root.scale.y,210/185,'Catcher uses the same actual-height scale as every other defender');assert.equal(catcher.root.position.y,0,'Crouching is articulated through the legs, not by burying the model below ground');
assert.deepEqual(fielders.map(m=>m.root.userData.playerId),field.DEFENSIVE_SLOTS);
props.seasonGame={...props.seasonGame,half:'bottom'};refresh(props,1,1);
assert.equal(catcher.root.userData.playerId,'u0');assert.equal(catcher.look.team,'HH');assert.equal(catcher.look.name,'');assert.equal(catcher.look.number,undefined);
props.seasonGame={...props.seasonGame,awayLineup:[]};refresh(props,1,1);
assert.equal(catcher.root.userData.playerId,null);assert(fielders.every(m=>m.root.userData.playerId===null));assert(fielders.every(m=>m.look.team==='HH'));

const capsules=JSON.parse(fs.readFileSync('lib/batter-colliders.json','utf8'));
for(const bodyType of ['lean','athletic','power']){const dimensions=appearance.playerDimensions({bodyType});assert.equal(feedback.bodyColliderRadiusFactor(0,bodyType),Math.max(dimensions.widthScale,dimensions.depthScale));assert(Math.abs(feedback.bodyColliderRadiusFactor(10,bodyType)-Math.max(1+(dimensions.widthScale-1)*.18,1+(dimensions.depthScale-1)*.12))<1e-12);assert.equal(feedback.bodyColliderRadiusFactor(15,bodyType),1);}
for(const hand of ['left','right'])for(const height of [155,185,215]){
 const scale=height/185,base=feedback.styleBodyCapsules(capsules[hand],scale,'athletic'),power=feedback.styleBodyCapsules(capsules[hand],scale,'power'),lean=feedback.styleBodyCapsules(capsules[hand],scale,'lean');
 // Isolate the hips: straight swept paths between old/new boundaries prove that power widens and lean narrows collision.
 const center={x:base[0].a.x,y:(base[0].a.y+base[0].b.y)/2,z:base[0].a.z};
 for(const [inside,outside]of [[power,base],[base,lean]]){const x=center.x+(inside[0].radius+outside[0].radius)/2+.065,from={x,y:center.y,z:center.z-1},to={x,y:center.y,z:center.z+1};assert(feedback.segmentTouchesBody(from,to,[inside[0]]));assert(!feedback.segmentTouchesBody(from,to,[outside[0]]));}
 for(let i=0;i<base.length;i++){assert.deepEqual(power[i].a,base[i].a);assert.deepEqual(lean[i].b,base[i].b);assert(Number.isFinite(power[i].radius));}
}
console.log('PASS explicit defensive positions, DH exclusion, deduplication/fallback, both innings, real Scene catcher identity/appearance/height/hand, bodyType mesh factors and bilateral scaled collision boundaries');
