import assert from 'node:assert/strict';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';

const THREE=createRequire(import.meta.url)('three');
const {shoe,createMitt,setMittStyle,mittPalette}=load('lib/player-equipment.ts');
const {createPlayer,dressPlayer}=load('lib/player-model.ts');
const {playerAppearance}=load('lib/player-appearance.ts');
const root=new THREE.Group(),mat=new THREE.MeshStandardMaterial();shoe(root,mat,mat,mat);root.updateMatrixWorld(true);
const upper=root.getObjectByName('Shaped cleat upper'),sole=root.getObjectByName('Cleat outsole');
const ray=new THREE.Raycaster(),origin=new THREE.Vector3(),down=new THREE.Vector3(0,-1,0),up=new THREE.Vector3(0,1,0);
let samples=0,maxGap=-Infinity;
// The upper must reach the sole through the forefoot. Previously the small toe
// radius left a visible 2–3 cm strip of empty space above the outsole.
for(const z of [.13,.15,.17,.19,.21,.23])for(const x of [-.025,0,.025]){
 ray.set(origin.set(x,-1,z),up);const leather=ray.intersectObject(upper,false)[0];
 ray.set(origin.set(x,0,z),down);const rubber=ray.intersectObject(sole,false)[0];
 assert(leather&&rubber,'Both surfaces cover the forefoot sample');
 const gap=leather.point.y-rubber.point.y;maxGap=Math.max(maxGap,gap);samples++;
 assert(gap<=.0015,`Forefoot upper/sole gap at ${x},${z}: ${gap}`);
}
// All existing ground contacts stay in their original ankle-local plane.
const studs=root.getObjectByName('Cleat contact studs');studs.geometry.computeBoundingBox();
assert(Math.abs(studs.geometry.boundingBox.min.y+.469)<1e-7);
assert(Math.abs(studs.geometry.boundingBox.max.y+.453)<1e-7);
// Sample the rendered stitch centreline, including points between its original
// anchors. Seams must follow the leather instead of hovering over the low toe.
const stitched=root.getObjectByName('Cleat stitched panels').geometry.getAttribute('position');
const upperPositions=upper.geometry.getAttribute('position'),upperIndices=upper.geometry.index,triangles=[];
for(let i=0;i<upperIndices.count;i+=3)triangles.push(new THREE.Triangle(...[0,1,2].map(j=>new THREE.Vector3().fromBufferAttribute(upperPositions,upperIndices.getX(i+j)))));
const centre=new THREE.Vector3(),point=new THREE.Vector3();let stitchSamples=0,maxStitchGap=0;
for(let start=0;start<stitched.count;start+=6){
 centre.set(0,0,0);for(let j=0;j<5;j++)centre.add(point.fromBufferAttribute(stitched,start+j));centre.multiplyScalar(.2);
 let nearest=Infinity;for(const triangle of triangles){triangle.closestPointToPoint(centre,point);nearest=Math.min(nearest,point.distanceTo(centre));}
 maxStitchGap=Math.max(maxStitchGap,nearest);stitchSamples++;
 assert(nearest<=.0006,`Stitch centreline must stay against leather: ${nearest}`);
}
const surfaces=['glove','gloveTrim','glovePocket'];
// Lazy left/right fitting offsets must never retain a caller's live geometry.
for(const ankleName of ['Left ankle','Right ankle']){
 const make=()=>{const ankle=new THREE.Group(),foot=new THREE.Group();ankle.name=ankleName;ankle.add(foot);shoe(foot,mat,mat,mat);return foot;};
 const first=make(),second=make(),a=first.getObjectByName('Cleat stitched panels').geometry,b=second.getObjectByName('Cleat stitched panels').geometry;
 assert.notEqual(a,b);assert.notEqual(a.attributes.position.array,b.attributes.position.array);
 const expected=Float32Array.from(a.attributes.position.array);
 assert.deepEqual(b.attributes.position.array,expected,'Warm left/right shoe fitting keeps identical vertices');
 a.attributes.position.array.fill(99);a.dispose();
 const third=make(),c=third.getObjectByName('Cleat stitched panels').geometry;
 assert.deepEqual(b.attributes.position.array,expected,'Editing and disposing one shoe cannot change another');
 assert.deepEqual(c.attributes.position.array,expected,'Editing a shoe cannot poison future cached construction');
 for(const foot of [first,second,third])foot.traverse(o=>{if(o.isMesh&&o.geometry!==a)o.geometry.dispose();});
}
let palettes=0;
for(const color of ['#915b31','#15171b','#ece9e1','#b82536','#164e94']){
 const expected=mittPalette(color),model=createPlayer('#eff0e9');
 const baseLightness=new THREE.Color(color).getHSL({}).l,pocketLightness=new THREE.Color(expected.glovePocket).getHSL({}).l;
 assert(pocketLightness<baseLightness,'Even black leather keeps its darker pocket');
 dressPlayer(model,playerAppearance('OB','QA',27,{gloveColor:color}));
 const found=new Map();model.root.traverse(o=>{if(!o.isMesh)return;for(const m of Array.isArray(o.material)?o.material:[o.material])if(surfaces.includes(m.userData.playerSurface))found.set(m.userData.playerSurface,m.color.getHexString());});
 for(const surface of surfaces)assert.equal(found.get(surface),expected[surface].slice(1),`Dressing updates ${surface} from the selected leather dye`);
 const mitt=createMitt(new THREE.Group());
 for(const style of ['fielding','catcher','fielding']){
  setMittStyle(mitt,style,{gloveColor:color});
  mitt.traverse(o=>{if(o.isMesh&&surfaces.includes(o.material.userData.playerSurface))assert.equal(o.material.color.getHexString(),expected[o.material.userData.playerSurface].slice(1),'Catcher/fielding swaps preserve all three dye surfaces');});
 }
 palettes++;
}
console.log('PASS equipment fit: connected forefoot and sole, preserved stud contacts, custom leather dye across dress/style changes');
console.log(JSON.stringify({samples,maxGap,stitchSamples,maxStitchGap,palettes}));
