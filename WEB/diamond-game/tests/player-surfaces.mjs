import fs from 'node:fs';
import {fileURLToPath} from 'node:url';
import {createRequire} from 'node:module';
import {createHash} from 'node:crypto';
import assert from 'node:assert/strict';

const require=createRequire(import.meta.url),ts=require('typescript'),THREE=require('three');
const source=fs.readFileSync(fileURLToPath(new URL('../lib/player-surfaces.ts',import.meta.url)),'utf8');
const sha=data=>createHash('sha256').update(data).digest('hex');
// Captured from the original uncached procedural maps, independently of the
// cache implementation. These fixtures do not need a work/ snapshot or browser.
const expected={
 'height:cloth':{sha256:'0f7f15255f581ff4087356fd4ee9e634895d24b51362ef2524536834bf984785',samples:[143,120,109,141,128,143]},
 'height:skin':{sha256:'8b721ab1226beea5a2e519e14007715d9d6139f40a9ee9ebc4f13c86d5c360bf',samples:[131,131,132,131,133,131]},
 'height:leather':{sha256:'8374e6a2812f97e94c7e6b2d913607a1db2dc436ccdca89420e25bae607831c1',samples:[121,136,126,123,141,120]},
 'height:wood':{sha256:'93682aa43182c1c43e3a6745450a8cb146d281f322f39da9d12ee65a8f11a866',samples:[136,143,134,130,138,121]},
 'roughness:cloth':{sha256:'0662eb8a46045b6efe68903bb53e1cc545d27f670b23dfd7d9ff237dacf68582',samples:[228,229,242,249,232,228]},
 'roughness:skin':{sha256:'544ee1ad58141e39fab5980ffd6f91ccb0db1a8f4e2946424450c35ff20cd22a',samples:[190,191,212,230,219,189]},
 'roughness:leather':{sha256:'dce0ada0239714eb6999c3eca69ad038c4676c419994f0a51cccc341c04f635e',samples:[226,226,229,222,216,226]},
 'roughness:wood':{sha256:'062b8ae232cc1d4b3ab2e0c9c5834da136003518082ae2423d28ea758988f7dc',samples:[215,220,218,224,220,209]},
};
const samplePixels=[0,257,12973,32896,65401,65535],keys=Object.keys(expected),kinds=['cloth','skin','leather','wood'];
let assertions=0;
const same=(a,b,message)=>{assertions++;assert.deepEqual(a,b,message);};
const ok=(condition,message)=>{assertions++;assert(condition,message);};

function loadModule(){
 const audit={pixels:0,noiseCalls:0};
 // Test-only counters expose recomputation without relying on timing. Template
 // inspection returns copies; no production API or writable buffer is exposed.
 const instrumented=source.replace('const value = Math.max','audit.pixels++; const value = Math.max')
  .replace('const random = randomSequence(seed);','audit.noiseCalls++; const random = randomSequence(seed);');
 assert(instrumented.includes('audit.pixels++')&&instrumented.includes('audit.noiseCalls++'),'Instrumentation still identifies the pixel and noise loops');
 const js=ts.transpileModule(instrumented,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,esModuleInterop:true}}).outputText;
 const mod={exports:{}};
 const templates=new Function('require','module','exports','audit',js+';return () => Object.fromEntries(Object.entries(pixelTemplates).map(([key,data]) => [key,data.slice()]));')(require,mod,mod.exports,audit);
 return {api:mod.exports,audit,templates};
}

function installCanvas(mode='normal'){
 globalThis.document={createElement(tag){
  assert.equal(tag,'canvas');
  const canvas={width:0,height:0,rgba:null,lastImageData:null};
  canvas.getContext=kind=>{
   assert.equal(kind,'2d');if(mode==='no-2d')return null;
   return {
    createImageData(width,height){return {width,height,data:new Uint8ClampedArray(width*height*4)};},
    putImageData(image,x,y){
     assert.equal(x,0);assert.equal(y,0);
     canvas.lastImageData=image;canvas.rgba=image.data.slice();
     // A caller-owned ImageData must never alias the cached template.
     if(mode==='mutate-input')image.data.fill(73);
    },
   };
  };
  return canvas;
 }};
}
const retained=new Set();
function texture(api,key){
 const [type,kind]=key.split(':');
 const value=type==='height'?api.createPlayerSurface(kind):api.createPlayerRoughness(kind);
 if(value)retained.add(value);return value;
}
function bytesMatch(bytes,key){
 same(bytes.byteLength,262144,`${key}: full 256² RGBA image`);
 same(sha(bytes),expected[key].sha256,`${key}: every byte matches the original map`);
 same(samplePixels.map(pixel=>bytes[pixel*4]),expected[key].samples,`${key}: fixed representative values`);
 ok(bytes.every((value,index)=>index%4===3?value===255:value===bytes[index-index%4]),`${key}: opaque grayscale data`);
}
function properties(t){return {
 colorSpace:t.colorSpace,wrapS:t.wrapS,wrapT:t.wrapT,repeat:t.repeat.toArray(),
 offset:t.offset.toArray(),center:t.center.toArray(),rotation:t.rotation,
 magFilter:t.magFilter,minFilter:t.minFilter,anisotropy:t.anisotropy,
 generateMipmaps:t.generateMipmaps,flipY:t.flipY,premultiplyAlpha:t.premultiplyAlpha,
 unpackAlignment:t.unpackAlignment,mapping:t.mapping,format:t.format,type:t.type,
 width:t.image.width,height:t.image.height,version:t.version,
};}
function expectedProperties(kind){const repeat=kind==='cloth'?3:kind==='wood'?1:2;return {
 colorSpace:THREE.NoColorSpace,wrapS:THREE.RepeatWrapping,wrapT:THREE.RepeatWrapping,repeat:[repeat,repeat],
 offset:[0,0],center:[0,0],rotation:0,magFilter:THREE.LinearFilter,minFilter:THREE.LinearMipmapLinearFilter,
 anisotropy:4,generateMipmaps:true,flipY:true,premultiplyAlpha:false,unpackAlignment:4,
 mapping:THREE.UVMapping,format:THREE.RGBAFormat,type:THREE.UnsignedByteType,width:256,height:256,version:1,
};}

const originalDocument=globalThis.document,module=loadModule();
try{
 delete globalThis.document;
 same(module.api.createPlayerSurfaces(),{cloth:null,skin:null,leather:null,wood:null},'SSR grouped fallback');
 for(const key of keys)same(texture(module.api,key),null,'SSR standalone fallback');
 same(module.templates(),{},'No DOM means no lazy cache population');same(module.audit,{pixels:0,noiseCalls:0},'No DOM means no noise/pixel generation');
 installCanvas('no-2d');
 for(const key of keys)same(texture(module.api,key),null,'Unavailable 2d context remains null');
 same(module.api.createPlayerSurfaces(),{cloth:null,skin:null,leather:null,wood:null});
 same(module.templates(),{},'Failed context does not retain unused templates');same(module.audit,{pixels:0,noiseCalls:0});
 installCanvas();
 for(const [index,key]of keys.entries()){
  const first=texture(module.api,key),kind=key.split(':')[1];
  bytesMatch(first.image.rgba,key);same(properties(first),expectedProperties(kind),'Original sampler and data texture settings');
  same(Object.keys(module.templates()),keys.slice(0,index+1),'Only the requested kind is evaluated');
  same(module.audit.pixels,(index+1)*65536,'One pixel pass per newly requested map');
  const noiseCalls=module.audit.noiseCalls,second=texture(module.api,key);
  ok(first!==second&&first.image!==second.image&&first.source!==second.source,'Texture, Canvas and Source ownership is per call');
  ok(first.image.rgba!==second.image.rgba&&first.image.lastImageData.data!==second.image.lastImageData.data,'Independent canvas/ImageData storage');
  const secondVersion=second.version,secondSourceVersion=second.source.version;
  let firstDisposals=0,secondDisposals=0;
  first.addEventListener('dispose',()=>firstDisposals++);second.addEventListener('dispose',()=>secondDisposals++);
  first.repeat.set(99,17);first.wrapS=THREE.ClampToEdgeWrapping;first.image.rgba.fill(0);
  first.image.lastImageData.data.fill(255);first.image.width=1;first.needsUpdate=true;first.dispose();retained.delete(first);
  same(firstDisposals,1);same(secondDisposals,0,'Disposing one actor leaves a sibling alive');
  same(second.version,secondVersion);same(second.source.version,secondSourceVersion,'Texture uploads remain independent');
  bytesMatch(second.image.rgba,key);same(properties(second),expectedProperties(kind),'Caller sampler mutation is isolated');
  const regenerated=texture(module.api,key);
  bytesMatch(regenerated.image.rgba,key);same(properties(regenerated),expectedProperties(kind),'Construction after disposal restores all original settings');
  same(sha(module.templates()[key]),expected[key].sha256,'Caller mutations cannot poison the private template');
  same(module.audit.pixels,(index+1)*65536,'Warm construction does not calculate pixels again');
  same(module.audit.noiseCalls,noiseCalls,'Warm construction does not rebuild noise grids');
 }
 const computed={...module.audit};
 same(Object.values(module.templates()).reduce((sum,data)=>sum+data.byteLength,0),2097152,'Eight-map cache retains at most two MiB');
 // Verify isolation even if canvas code actively changes the supplied ImageData.
 installCanvas('mutate-input');
 for(const key of keys){bytesMatch(texture(module.api,key).image.rgba,key);same(sha(module.templates()[key]),expected[key].sha256);}
 installCanvas();
 for(const kind of ['unknown','__proto__','constructor',...Array.from({length:16},(_,i)=>`arbitrary-${i}`)]){
  same(module.api.createPlayerSurface(kind),undefined,'Original unknown JS height-kind fallthrough');
  const roughness=module.api.createPlayerRoughness(kind);retained.add(roughness);
  same(sha(roughness.image.rgba),expected['roughness:cloth'].sha256,'Unknown JS roughness kind preserves original cloth pixels');
  same(properties(roughness),expectedProperties(kind),'Unknown JS kind keeps repeat=2');
 }
 same(Object.keys(module.templates()),keys,'Arbitrary runtime strings cannot grow the cache');same(module.audit,computed,'All repeated requests avoid procedural work');
 const all=module.api.createPlayerSurfaces();same(Object.keys(all),kinds);
 for(const kind of kinds){retained.add(all[kind]);bytesMatch(all[kind].image.rgba,`height:${kind}`);}
 delete globalThis.document;for(const key of keys)same(texture(module.api,key),null,'Warm templates cannot bypass document availability');
 installCanvas('no-2d');for(const key of keys)same(texture(module.api,key),null,'Warm templates cannot bypass 2d context availability');same(module.audit,computed);
 installCanvas();const reloaded=loadModule();same(reloaded.templates(),{},'A new module starts with zero retained images');
 for(const key of keys)bytesMatch(texture(reloaded.api,key).image.rgba,key);
 same(reloaded.audit.pixels,8*65536,'A new module regenerates each requested map exactly once');
}finally{
 for(const t of retained)t.dispose();
 if(originalDocument===undefined)delete globalThis.document;else globalThis.document=originalDocument;
}
console.log(`PASS player-surfaces: ${assertions} assertions; 8 original RGBA SHA fixtures, lazy 2 MiB bound, isolated Canvas/Texture/Source ownership, disposal/mutation recovery, null paths and original samplers`);
