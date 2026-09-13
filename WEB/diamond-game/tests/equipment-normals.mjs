import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';

const THREE=createRequire(import.meta.url)('three');
const {shoe,hand,createMitt,createBat}=load('lib/player-equipment.ts');
const {restoreSeamNormals}=load('lib/player-geometry.ts');
const {createPlayer,equipCatcher}=load('lib/player-model.ts');
const {posePitcher}=load('lib/player-pose.ts');
const {pitchingPose,PITCH_WINDUP_MS}=load('lib/player-motion.ts');
const v=(...values)=>new THREE.Vector3(...values);
const root=new THREE.Group(),mat=new THREE.MeshStandardMaterial();
shoe(root,mat,mat,mat);hand(root,mat);createMitt(root);const bat=createBat(root);
const expected=[['Shaped cleat upper',19],['Cleat outsole',17],['Cleat quarter panels',18],['Tapered articulated fingers',65],['Batting glove tapered finger grip',166]];
let seamsChecked=0;
for(const [name,count] of expected){
 const geometry=root.getObjectByName(name).geometry,pairs=geometry.userData.normalSeamPairs;
 assert.equal(pairs.length,count,`${name}: every original and rounded-cap/digit ring is recorded`);
 const snapshot={positions:Array.from(geometry.attributes.position.array),uv:Array.from(geometry.attributes.uv.array),indices:Array.from(geometry.index.array)};
 const normal=geometry.attributes.normal;
 for(const [a,b] of pairs){
  assert(v().fromBufferAttribute(geometry.attributes.position,a).distanceTo(v().fromBufferAttribute(geometry.attributes.position,b))<1e-7);
  assert.equal(geometry.attributes.uv.getX(a),0);assert.equal(geometry.attributes.uv.getX(b),1);
  assert(v().fromBufferAttribute(normal,a).distanceTo(v().fromBufferAttribute(normal,b))<1e-7,`${name}: fresh seam is smooth`);
 }
 geometry.scale(1.13,1,.94);geometry.computeVertexNormals();
 const members=new Set(pairs.flat()),before=Array.from(normal.array);
 restoreSeamNormals(geometry);
 for(const [a,b] of pairs)assert(v().fromBufferAttribute(normal,a).distanceTo(v().fromBufferAttribute(normal,b))<1e-7,`${name}: regenerated seam is smooth`);
 for(let i=0;i<normal.count;i++)if(!members.has(i))for(let c=0;c<3;c++)assert.equal(normal.getComponent(i,c),before[i*3+c],'No smoothing of unrelated cap faces or intentional edges');
 assert.deepEqual(Array.from(geometry.attributes.uv.array),snapshot.uv);assert.deepEqual(Array.from(geometry.index.array),snapshot.indices);
 const positions=geometry.attributes.position;
 for(let i=0;i<positions.count;i++){
  assert(Math.abs(positions.getX(i)-snapshot.positions[i*3]*1.13)<1e-7);
  assert.equal(positions.getY(i),snapshot.positions[i*3+1]);
  assert(Math.abs(positions.getZ(i)-snapshot.positions[i*3+2]*.94)<1e-7);
 }
 seamsChecked+=pairs.length;
}

// A concave end cup must be visible from above with ordinary front-face culling.
// Check actual triangle winding as well as interpolated shading normals.
const cup=bat.getObjectByName('Recessed bat end cup'),p=cup.geometry.attributes.position,index=cup.geometry.index;
assert.equal(cup.material.side,THREE.FrontSide);assert.equal(cup.material.userData.playerSurface,'bat');
let faces=0,minNormalY=1;
for(let i=0;i<index.count;i+=3){
 const a=v().fromBufferAttribute(p,index.getX(i)),b=v().fromBufferAttribute(p,index.getX(i+1)),c=v().fromBufferAttribute(p,index.getX(i+2));
 const normal=b.sub(a).cross(c.sub(a));if(normal.lengthSq()<1e-18)continue;
 normal.normalize();assert(normal.y>0,'The recessed cup front faces must point upward');faces++;minNormalY=Math.min(minNormalY,normal.y);
}
assert(faces>100);for(let i=0;i<cup.geometry.attributes.normal.count;i++)assert(cup.geometry.attributes.normal.getY(i)>0);
root.updateMatrixWorld(true);
for(const radius of [.004,.013,.023])for(const angle of [0,.8,1.7,3.3,4.9]){
 const ray=new THREE.Raycaster(v(Math.cos(angle)*radius,1.1,Math.sin(angle)*radius),v(0,-1,0));
 const hits=ray.intersectObject(cup,false);assert(hits.length>0,'A ray from above must hit the cup without DoubleSide');
 assert(hits[0].point.y>=.9609&&hits[0].point.y<=.98);
 const below=new THREE.Raycaster(v(Math.cos(angle)*radius,.9,Math.sin(angle)*radius),v(0,1,0));
 assert.equal(below.intersectObject(cup,false).length,0,'The interior is not accidentally rendered from underneath');
}

// Real factory attachment: wrist enters the cuff and the lower arm ends behind
// the cup. Test skinned vertices in actual prepared, release and catcher poses.
let attachmentCases=0,maxCuffGap=0,forearmVertices=0;
for(const sign of [-1,1])for(const scenario of ['overhand ready','overhand release','sidearm ready','sidearm release','underhand ready','underhand release','catcher']){
 const model=createPlayer('#25465b'),glove=model.le.children.find(object=>object.getObjectByName('Deep leather mitt pocket'));
 model.root.scale.x=sign;
 if(scenario==='catcher'){
  equipCatcher(model);model.root.scale.multiplyScalar(.9);model.root.position.set(0,-.66,1.1);model.root.rotation.y=Math.PI;
  model.ll.rotation.x=model.rl.rotation.x=-1.65;model.lk.rotation.x=model.rk.rotation.x=2.1;
 }else{
  const [delivery,phase]=scenario.split(' ');posePitcher(model,pitchingPose(phase==='ready'?-PITCH_WINDUP_MS:0,delivery),sign);
 }
 model.root.updateMatrixWorld(true);
 const skin=model.left.children.find(object=>object.name==='Continuous anatomical arm');skin.skeleton.update();
 const wrist=glove.worldToLocal(model.le.localToWorld(v(0,-.341,.006))),gap=wrist.distanceTo(v(0,-.118,-.036));
 assert(gap<1e-7,`${scenario} ${sign}: actual forearm endpoint seats in cuff`);maxCuffGap=Math.max(maxCuffGap,gap);
 const positions=skin.geometry.attributes.position,weights=skin.geometry.attributes.skinWeight;
 for(let i=0;i<positions.count;i++){
  if(weights.getY(i)<1-1e-7)continue;
  const point=glove.worldToLocal(skin.localToWorld(skin.getVertexPosition(i,v())));
  assert(point.y<=-.1179,`${scenario} ${sign}: lower-arm skin must stay on the cuff side, away from the pocket centre`);forearmVertices++;
 }
 attachmentCases++;
}
console.log('PASS equipment normals: complete capped/digit seams, geometry invariants, upward FrontSide bat cup rays and real mirrored mitt cuff attachment');
console.log(JSON.stringify({seamsChecked,cupFaces:faces,minCupFaceNormalY:minNormalY,attachmentCases,forearmVertices,maxCuffGap}));
