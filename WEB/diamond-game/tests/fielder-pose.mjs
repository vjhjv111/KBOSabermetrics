import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {createHash} from 'node:crypto';
import {load} from './load-ts.mjs';
const THREE=createRequire(import.meta.url)('three'),V=(...p)=>new THREE.Vector3(...p);
const {createPlayer,dressPlayer,equipRunnerHands}=load('lib/player-model.ts'),{playerAppearance,playerDimensions}=load('lib/player-appearance.ts');
const {poseRunning,poseFielding}=load('lib/fielder-pose.ts');
const {fieldPlayTimeline,fieldBallPosition}=load('lib/field-play-timeline.ts'),{BASES,DEFENSIVE_SPOTS,fielderPosition}=load('lib/field-play.ts');
const {runnerMotion,fielderMotion}=load('lib/field-motion-path.ts');
const transitionOnly=process.argv.includes('--transitions-only');
let checks=0,poses=0,maxSoleError=0,maxSupportDrift=0,maxPocketError=0,maxReleaseError=0,minGloveY=Infinity,minHip=Infinity,maxHip=0,maxSupportKnee=0;
function ok(condition,message){checks++;assert(condition,message);}
function near(a,b,tol=1e-6,message='Values agree'){ok(Math.abs(a-b)<=tol,`${message}: ${a} / ${b}`);}
function equalPoint(a,b,tol=1e-6,message='Points agree'){ok(a.distanceTo(b)<=tol,`${message}: ${a.toArray()} / ${b.toArray()}`);}
function make(height,bodyType,hand,runner=false){const model=createPlayer('#234567',runner);if(runner)equipRunnerHands(model);dressPlayer(model,playerAppearance('LG','QA',7,{heightCm:height,bodyType}),hand);model.root.scale.set(hand*height/185,height/185,height/185);return model;}
function points(root){const result=[];root.updateWorldMatrix(true,true);root.traverse(mesh=>{if(mesh.isMesh)for(let i=0;i<mesh.geometry.attributes.position.count;i++)result.push(mesh.getVertexPosition(i,V()).applyMatrix4(mesh.matrixWorld));});return result;}
function floor(root){return Math.min(...points(root).map(p=>p.y));}
function gloveFor(model){return model.le.children.find(child=>{let found=false;child.traverse(o=>{if(o.name==='Deep leather mitt pocket'||o.userData.playerBatchParts?.some(p=>p.name==='Deep leather mitt pocket'))found=true;});return found;});}
function hash(model){const h=createHash('sha256');model.root.traverse(o=>{if(o.isMesh)for(const a of [...Object.values(o.geometry.attributes),o.geometry.index].filter(Boolean))h.update(new Uint8Array(a.array.buffer,a.array.byteOffset,a.array.byteLength));});return h.digest('hex');}
function dispose(model){const resources=new Set();model.root.traverse(o=>{if(o.isMesh){resources.add(o.geometry);if(o.skeleton)resources.add(o.skeleton);for(const m of Array.isArray(o.material)?o.material:[o.material])resources.add(m);}});for(const r of resources)r.dispose();}
const paths={straight:d=>({x:0,y:0,z:d}),corner:d=>d<2?{x:0,y:0,z:d}:{x:d-2,y:0,z:2}};
if(!process.argv.includes('--field-only')&&!transitionOnly)for(const height of [155,185,215])for(const bodyType of ['lean','athletic','power'])for(const hand of [-1,1])for(const runner of [false,true]){
 const model=make(height,bodyType,hand,runner),before=hash(model),fixed=['left','right','le','re','lk','rk','lf','rf'].map(key=>[key,model[key].position.clone()]);
 for(const [name,samplePath]of Object.entries(paths))for(const speed of [4,7.4,10]){
  let previous=null;
  for(let frame=0;frame<81;frame++){
   const travel=frame*.05,point=samplePath(travel),a=samplePath(travel+.7),b=samplePath(travel-.7),heading=Math.atan2(a.x-b.x,a.z-b.z);
   model.root.position.set(point.x,0,point.z);model.root.rotation.y=heading;
   const result=poseRunning(model,{now:travel/speed*1000,travel,speed,samplePath});poses++;
   minHip=Math.min(minHip,model.hips.position.y);maxHip=Math.max(maxHip,model.hips.position.y);if(name==='straight')ok(model.hips.position.y>=.74,`Straight sprint does not collapse into a deep squat: ${height}/${hand}/${speed}/${frame}: ${model.hips.position.y}`);
   const low=[floor(model.lf),floor(model.rf)];
   for(let i=0;i<2;i++){
    ok(low[i]>=-1e-6,`Shoe must not cross ground: ${height}/${bodyType}/${hand}/${runner}/${name}/${speed}/${frame}/${i}: ${low[i]}`);
    const planted=i===0?result.leftPlanted:result.rightPlanted;
    if(planted){maxSoleError=Math.max(maxSoleError,Math.abs(low[i]));near(low[i],0,1e-6,'Real planted sole meets ground');maxSupportKnee=Math.max(maxSupportKnee,2*Math.acos(Math.min(1,Math.abs((i===0?model.lk:model.rk).quaternion.w))));}
    const current=points(i===0?model.lf:model.rf),cycle=Math.floor(travel/result.periodDistance+i*.5);
    if(previous&&planted&&previous[i].planted&&previous[i].cycle===cycle){
     // Compare every actual shoe vertex, including around a 90-degree turn.
     const drift=Math.max(...current.map((p,index)=>p.distanceTo(previous[i].points[index])));maxSupportDrift=Math.max(maxSupportDrift,drift);near(drift,0,1e-6,'Support shoe stays at its world plant, including rotation');
    }
    if(i===0)result.audit=[];result.audit.push({planted,cycle,points:current});
   }
   previous=result.audit;
   for(const [key,p]of fixed)equalPoint(model[key].position,p,1e-7,`${key} original bone/foot anchor is unchanged`);
  }
 }
 // A stopped runner has two grounded feet regardless of the last stride phase.
 for(const now of [0,1000,2000]){poseRunning(model,{now,travel:4,speed:0});near(floor(model.lf),0);near(floor(model.rf),0);}
 ok(hash(model)===before,'Locomotion preserves every geometry attribute and index');dispose(model);
}

const fixtures=[{kind:'out',outcome:'OUT',trajectory:'ground',exitSpeed:110,launchAngle:6,direction:.18},{kind:'out',outcome:'OUT',trajectory:'fly',exitSpeed:125,launchAngle:35,direction:-.3},{kind:'hit',outcome:'2B',trajectory:'fly',exitSpeed:125,launchAngle:25,direction:.5}].map(r=>({...r,contact:{at:1000,position:{x:0,y:1.05,z:0}}}));
if(!transitionOnly)for(const height of [155,185,215])for(const bodyType of ['lean','athletic','power'])for(const hand of [-1,1]){
 const model=make(height,bodyType,hand),glove=gloveFor(model),geometryBefore=hash(model),scale=height/185,depth=playerDimensions(model.appearance).depthScale;
 const anchor=V(0,-.02,-.052*depth+.078/scale),cuff=V(0,-.118,-.036*depth),wrist=V(0,-.341,.006*depth),fingerAnchor=V(0,-.378,.088*depth);
 for(const result of fixtures){
  const timeline=fieldPlayTimeline(result),original=JSON.stringify(timeline);
  for(const receiving of [false,true]){
   if(receiving&&!timeline.throwAt)continue;
   const at=receiving?timeline.throwArrivesAt:timeline.fieldedAt,target=receiving?timeline.throwTarget:timeline.fieldTarget;
   model.root.position.set(target.x,0,target.z-.3);model.root.rotation.y=0;
   let contact;
   for(const offset of [-500,-220,0,160,350,700]){
    const pose=poseFielding(model,timeline,at+offset,receiving);poses++;
    near(floor(model.lf),0,1e-6,'Fielding left sole stays grounded');near(floor(model.rf),0,1e-6,'Fielding right sole stays grounded');
    equalPoint(pose.pocket,glove.localToWorld(anchor.clone()),1e-7,'Fielding uses real mitt ball-centre anchor');
    equalPoint(glove.localToWorld(cuff.clone()),model.le.localToWorld(wrist.clone()),1e-7,'Mitt cuff stays on the original forearm');
    equalPoint(pose.releasePoint,model.re.localToWorld(fingerAnchor.clone()),1e-7,'Release anchor is attached to the modeled fingers');
    ok([...pose.pocket.toArray(),...pose.releasePoint.toArray(),pose.reachError].every(Number.isFinite),'Animation stays finite');
   if(offset===0){contact=pose;maxPocketError=Math.max(maxPocketError,pose.pocket.distanceTo(V().copy(target)));equalPoint(pose.pocket,V().copy(target),.002,'Pocket meets the existing catch target');ok(pose.catchable,'Representative catches remain reachable');const gloveY=floor(glove);minGloveY=Math.min(minGloveY,gloveY);ok(gloveY>=-1e-6,'Actual mitt leather must remain above the ground at pickup');ok(pose.releasePoint.distanceTo(pose.pocket)<.14*scale,'Bare hand helps directly above the mitt instead of rising behind the crouched head');}
    if(contact){equalPoint(pose.catchPocket,contact.catchPocket,1e-7,'Contact reach is independent of recovery animation');near(pose.reachError,contact.reachError);}
   }
   // First render after the transfer must report the same original contact.
   const cold=poseFielding(model,timeline,at+900,receiving);equalPoint(cold.catchPocket,contact.catchPocket,1e-7);
  }
  if(timeline.throwAt){
   model.root.position.set(timeline.fieldTarget.x,0,timeline.fieldTarget.z-.3);model.root.rotation.y=0;
   const pose=poseFielding(model,timeline,timeline.throwAt),ball=V().copy(fieldBallPosition(result,timeline.throwAt,timeline));
   const error=pose.releasePoint.distanceTo(ball);maxReleaseError=Math.max(maxReleaseError,error);equalPoint(pose.releasePoint,ball,.002,'Actual finger anchor meets unchanged world throw origin at every height');
  }
  ok(JSON.stringify(timeline)===original,'Posing never changes field timeline, ball targets or completion clocks');
 }
 // Include the exact lowest roll height and intermediate low-line catches,
 // translated across the field; no specially chosen comfortable contact height.
 for(const x of [-30,0,30])for(const y of [.065,.1,.3,.6,1.4]){
  const target={x,y,z:-30},timeline={kind:'throw',fielder:0,fieldTarget:target,fieldedAt:1000,physicalFieldMs:1000,completeAt:2400,throwAt:1350,throwArrivesAt:2000,throwTarget:{x:20,y:1.2,z:-20}};
  model.root.position.set(x,0,-30.3);model.root.rotation.y=0;
  const pose=poseFielding(model,timeline,1000);poses++;maxPocketError=Math.max(maxPocketError,pose.reachError);
  equalPoint(pose.pocket,V().copy(target),.002,`Low-to-high field grid: ${height}/${bodyType}/${hand}/${x}/${y}`);ok(pose.catchable,'Translated field contact is reachable');
  near(floor(model.lf),0);near(floor(model.rf),0);const gloveY=floor(glove);minGloveY=Math.min(minGloveY,gloveY);ok(gloveY>=-1e-6,'Even the lowest .065 m rolling ball keeps mitt leather out of the ground');
  if(y/scale<=.25){near(model.hips.position.y,.39);ok(model.hips.position.z<-.12,'Low gather uses a backward hip hinge instead of sitting on the heels');}
 }
 ok(hash(model)===geometryBefore,'Receiving/throwing preserves geometry, topology and skinning');dispose(model);
}
let maxRunnerStopStep=0,maxFieldHandoffStep=0;
function sceneRun(model,motion,now){model.root.position.copy(motion.position);model.root.rotation.y=motion.heading;return poseRunning(model,{...motion,now});}
function snapshot(model){return ['hips','torso','head','left','right','le','re','ll','rl','lk','rk','lf','rf'].flatMap(key=>[...model[key].position.toArray(),...model[key].quaternion.toArray()]);}
function surfaces(model){model.root.updateWorldMatrix(true,true);model.root.updateMatrixWorld(true);const skeletons=new Set();model.root.traverse(o=>{if(o.isSkinnedMesh)skeletons.add(o.skeleton);});for(const s of skeletons)s.update();return points(model.root);}
function change(a,b){return Math.max(...a.map((p,i)=>p.distanceTo(b[i])));}
if(!process.argv.includes('--field-only'))for(const height of [155,185,215])for(const hand of [-1,1]){
 for(const nominalSpeed of [7.4,14.8]){
  const model=make(height,'power',hand,true),plan={playerId:'qa',from:0,to:2,out:false},arrival=2*27.432/nominalSpeed*1000;
  let before;
  for(const offset of [-1,0,80,180,349,350,500]){
   const motion=runnerMotion(plan,arrival+offset,nominalSpeed/7.4);sceneRun(model,motion,arrival+offset);poses++;
   const shoes=[...points(model.lf),...points(model.rf)];if(offset===-1)before=shoes;
   if(offset===0){const delta=change(before,shoes);maxRunnerStopStep=Math.max(maxRunnerStopStep,delta);ok(delta<.035,`Arrival uses a continuous final stride, not a half-metre symmetric-pose snap: ${height}/${hand}/${nominalSpeed}: ${delta}`);}
   ok(floor(model.lf)>=-1e-6&&floor(model.rf)>=-1e-6,'Stop steps stay above ground');if(offset>=350){near(floor(model.lf),0);near(floor(model.rf),0);}
  }
  const now=arrival+180,motion=runnerMotion(plan,now,nominalSpeed/7.4);sceneRun(model,motion,now);const live=snapshot(model),cold=make(height,'power',hand,true);sceneRun(cold,motion,now);snapshot(cold).forEach((v,i)=>near(v,live[i],1e-7,'Mid-settle cold restore matches a live sequence'));
  const stationary=runnerMotion({playerId:'qa',from:1,to:1,out:false},0);sceneRun(cold,stationary,0);near(floor(cold.lf),0);near(floor(cold.rf),0);dispose(model);dispose(cold);
 }
 for(const result of [fixtures[0],{kind:'out',outcome:'OUT',trajectory:'line',exitSpeed:105,launchAngle:10,direction:.2,contact:{at:1000,position:{x:0,y:1.05,z:0}}}]){
  const model=make(height,'power',hand),timeline=fieldPlayTimeline(result),start=DEFENSIVE_SPOTS[timeline.fielder],goal={...timeline.fieldTarget,z:timeline.fieldTarget.z-.3};
  const arrival=180+Math.hypot(goal.x-start.x,goal.z-start.z)/7*1000;let before;
  const render=offset=>{const now=result.contact.at+arrival+offset,motion=fielderMotion(start,goal,arrival+offset);sceneRun(model,motion,now);const pose=offset>=0?poseFielding(model,timeline,now,false,{movement:{...motion,now}}):null;return{now,motion,pose};};
  for(const offset of [-1,0,40,80,180]){
   const {pose}=render(offset);poses++;const surface=surfaces(model);if(offset===-1)before=surface;
   if(offset===0){const delta=change(before,surface);maxFieldHandoffStep=Math.max(maxFieldHandoffStep,delta);ok(delta<.035,`Running-to-fielding body and glove remain continuous: ${height}/${hand}/${timeline.kind}: ${delta}`);}
   ok(floor(model.lf)>=-1e-6&&floor(model.rf)>=-1e-6,'Field handoff feet do not tunnel below ground');
  }
  const offset=timeline.fieldedAt-result.contact.at-arrival,{pose}=render(offset);equalPoint(pose.pocket,V().copy(timeline.fieldTarget),.002,'Contact remains exact at the unchanged fieldedAt after handoff');
  const frame=render(60),live=snapshot(model),cold=make(height,'power',hand);sceneRun(cold,frame.motion,frame.now);poseFielding(cold,timeline,frame.now,false,{movement:{...frame.motion,now:frame.now}});snapshot(cold).forEach((v,i)=>near(v,live[i],1e-7,'Cold field handoff matches live sequence'));
  dispose(model);dispose(cold);
 }
}
// A caller must receive current descendant matrices without needing the
// points()/floor() helpers above to refresh them first. Covers the cold cache,
// breathing, and a changed parent/root transform after a moving pose.
{
 const parent=new THREE.Group(),model=make(155,'power',-1);parent.add(model.root);parent.position.set(3,.2,-4);parent.rotation.y=.37;parent.scale.setScalar(1.1);
 for(const options of [{now:0,travel:0,speed:0},{now:1300*Math.PI/2,travel:0,speed:0},{now:700,travel:3.64,speed:7},{now:1200,travel:3.64,speed:0,nominalSpeed:7,stoppedFor:80}]){
  model.root.position.x+=.13;model.root.rotation.y+=.1;
  const result=poseRunning(model,options);poses++;const cached=[];model.root.traverse(object=>cached.push([object,object.matrixWorld.clone()]));
  model.root.updateWorldMatrix(true,true);
  for(const [object,matrix] of cached)matrix.elements.forEach((value,index)=>near(value,object.matrixWorld.elements[index],1e-11,'Pose returns already-current descendant matrices'));
  equalPoint(result.leftFoot,model.lf.getWorldPosition(V()),1e-11);equalPoint(result.rightFoot,model.rf.getWorldPosition(V()),1e-11);
 }
 dispose(model);
}
console.log(`PASS fielder poses: ${checks} assertions / ${poses} poses; real planted soles, distance-driven corner support, pocket/cuff and exact-height throw origin, immutable geometry/timeline`);
console.log(JSON.stringify({maxSoleError,maxSupportDrift,maxPocketError,maxReleaseError,minGloveY,hipRange:[minHip,maxHip],maxSupportKneeDegrees:maxSupportKnee*180/Math.PI,maxRunnerStopStep,maxFieldHandoffStep}));
