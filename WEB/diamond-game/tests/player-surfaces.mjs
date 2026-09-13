import fs from 'node:fs';
import {fileURLToPath} from 'node:url';
import {createRequire} from 'node:module';
import {createHash} from 'node:crypto';
import assert from 'node:assert/strict';

const require=createRequire(import.meta.url),ts=require('typescript'),THREE=require('three');
const source=fs.readFileSync(fileURLToPath(new URL('../lib/player-surfaces.ts',import.meta.url)),'utf8');
const sha=data=>createHash('sha256').update(data).digest('hex');
// Deterministic pixels for the reviewed surface maps. Alongside these fixtures,
// seam, material contrast and ownership checks guard their rendering contract.
const expected={
 'height:cloth':{sha256:'6cf776cf223434bae47d84ae8423d16062be95efb16b4dfdd0b60e9cdd05ea1f',samples:[142,125,120,137,133,142]},
 'height:skin':{sha256:'8b721ab1226beea5a2e519e14007715d9d6139f40a9ee9ebc4f13c86d5c360bf',samples:[131,131,132,131,133,131]},
 'height:leather':{sha256:'4ab73ba519757cfacb0b687203e268b8100f23f2f83da052786b6c9fcfc540bb',samples:[137,134,135,143,136,141]},
 'height:wood':{sha256:'f5ce25e10d5bafed5cc14ec7d541194e5ac09d021095fb82014e68003ddf9ca3',samples:[134,125,129,132,130,131]},
 'roughness:cloth':{sha256:'96cf37fb1cd2e5b31bcbb9d3499889d9479a9d9390dcf6b91f54ef25a62349fe',samples:[240,238,245,252,244,240]},
 'roughness:skin':{sha256:'544ee1ad58141e39fab5980ffd6f91ccb0db1a8f4e2946424450c35ff20cd22a',samples:[190,191,212,230,219,189]},
 'roughness:leather':{sha256:'dce0ada0239714eb6999c3eca69ad038c4676c419994f0a51cccc341c04f635e',samples:[226,226,229,222,216,226]},
 'roughness:wood':{sha256:'062b8ae232cc1d4b3ab2e0c9c5834da136003518082ae2423d28ea758988f7dc',samples:[215,220,218,224,220,209]},
 'albedo:cloth':{sha256:'fec35026892a6c40615aa38f546c29c6cae7f9ffa2f062e7d2e4bfb5fab4e4df',samples:[254,250,248,253,252,254]},
 'albedo:leather':{sha256:'4263320303bf3911ae1a8519e0afa0b05288e990c5380e760fd722de38765296',samples:[246,244,246,251,247,249]},
 'albedo:wood':{sha256:'f8afb366f0fb91f26a3f79cc32755f14d43e9d210991bd648d10b8d02a43cc6b',samples:[252,236,247,253,252,253]},
};
const samplePixels=[0,257,12973,32896,65401,65535],keys=Object.keys(expected),kinds=['cloth','skin','leather','wood'];
let assertions=0;
const same=(a,b,message)=>{assertions++;assert.deepEqual(a,b,message);};
const ok=(condition,message)=>{assertions++;assert(condition,message);};

function loadModule(){
 const audit={pixels:0,noiseCalls:0},samplers={};
 // Test-only counters expose recomputation without relying on timing. Template
 // inspection returns copies; no production API or writable buffer is exposed.
 const instrumented=source.replace('const value = Math.max','audit.pixels++; const value = Math.max')
  .replaceAll('const random = randomSequence(seed);','audit.noiseCalls++; const random = randomSequence(seed);')
  .replace('const height = buildHeight();','const height = buildHeight(); samplers[key] = height;');
 assert(instrumented.includes('audit.pixels++')&&instrumented.includes('audit.noiseCalls++'),'Instrumentation still identifies the pixel and noise loops');
 const js=ts.transpileModule(instrumented,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,esModuleInterop:true}}).outputText;
 const mod={exports:{}};
 const templates=new Function('require','module','exports','audit','samplers',js+';return () => Object.fromEntries(Object.entries(pixelTemplates).map(([key,data]) => [key,data.slice()]));')(require,mod,mod.exports,audit,samplers);
 return {api:mod.exports,audit,templates,samplers};
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
 const value=type==='height'?api.createPlayerSurface(kind):type==='albedo'?api.createPlayerAlbedo(kind):api.createPlayerRoughness(kind);
 if(value)retained.add(value);return value;
}
function bytesMatch(bytes,key){
 same(bytes.byteLength,262144,`${key}: full 256² RGBA image`);
 same(sha(bytes),expected[key].sha256,`${key}: deterministic pixel fixture`);
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
function expectedProperties(kind,type='height'){const repeat=kind==='cloth'?3:kind==='wood'?1:2;return {
 colorSpace:type==='albedo'?THREE.SRGBColorSpace:THREE.NoColorSpace,wrapS:THREE.RepeatWrapping,wrapT:THREE.RepeatWrapping,repeat:[repeat,repeat],
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
  const first=texture(module.api,key),[type,kind]=key.split(':');
  bytesMatch(first.image.rgba,key);same(properties(first),expectedProperties(kind,type),'Color/data spaces and sampler settings');
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
  bytesMatch(second.image.rgba,key);same(properties(second),expectedProperties(kind,type),'Caller sampler mutation is isolated');
  const regenerated=texture(module.api,key);
  bytesMatch(regenerated.image.rgba,key);same(properties(regenerated),expectedProperties(kind,type),'Construction after disposal restores all sampler settings');
  same(sha(module.templates()[key]),expected[key].sha256,'Caller mutations cannot poison the private template');
  same(module.audit.pixels,(index+1)*65536,'Warm construction does not calculate pixels again');
  same(module.audit.noiseCalls,noiseCalls,'Warm construction does not rebuild noise grids');
 }
 const computed={...module.audit};
 same(Object.values(module.templates()).reduce((sum,data)=>sum+data.byteLength,0),2883584,'Eleven-map cache retains at most 2.75 MiB');
 // UV seams are sampled continuously, not inferred from the first/last pixel
 // (whose centres are deliberately half a texel inside the tile).
 for(const key of keys)for(let i=0;i<32;i++){
  const sample=module.samplers[key],at=(i+.37)/32;
  ok(Math.abs(sample(0,at)-sample(1,at))<1e-8,`${key}: horizontal tile seam`);
  ok(Math.abs(sample(at,0)-sample(at,1))<1e-8,`${key}: vertical tile seam`);
 }
 const templates=module.templates();
 const distribution=bytes=>{
  let min=255,max=0,sum=0,gx=0,gy=0;
  for(let y=0;y<256;y++)for(let x=0;x<256;x++){
   const value=bytes[(y*256+x)*4];min=Math.min(min,value);max=Math.max(max,value);sum+=value;
   gx+=Math.abs(value-bytes[(y*256+(x+1)%256)*4]);gy+=Math.abs(value-bytes[(((y+1)%256)*256+x)*4]);
  }
  return {min,max,mean:sum/65536,gx,gy};
 };
 const cloth=distribution(templates['albedo:cloth']),leather=distribution(templates['albedo:leather']),wood=distribution(templates['albedo:wood']);
 ok(cloth.min>=245&&cloth.mean>=249&&cloth.max<=255,'White uniforms retain a clean neutral base');
 ok(cloth.gx>cloth.gy*1.2&&cloth.gx<cloth.gy*3,'Knit courses have direction without coarse striping');
 ok(leather.min>=228&&leather.max-leather.min>=12,'Leather pigment remains subtle but nonuniform');
 ok(leather.gx/leather.gy>.8&&leather.gx/leather.gy<1.2,'Leather cells do not introduce a directional UV seam');
 ok(wood.gx>wood.gy*8&&wood.max-wood.min>=25,'Wood pigment follows the bat length with narrow growth lines');
 // Verify isolation even if canvas code actively changes the supplied ImageData.
 installCanvas('mutate-input');
 for(const key of keys){bytesMatch(texture(module.api,key).image.rgba,key);same(sha(module.templates()[key]),expected[key].sha256);}
 installCanvas();
 for(const kind of ['unknown','__proto__','constructor',...Array.from({length:16},(_,i)=>`arbitrary-${i}`)]){
  same(module.api.createPlayerSurface(kind),undefined,'Original unknown JS height-kind fallthrough');
  const roughness=module.api.createPlayerRoughness(kind);retained.add(roughness);
  same(sha(roughness.image.rgba),expected['roughness:cloth'].sha256,'Unknown JS roughness kind preserves original cloth pixels');
  same(properties(roughness),expectedProperties(kind),'Unknown JS kind keeps repeat=2');
  same(module.api.createPlayerAlbedo(kind),null,'Unknown albedo kind cannot allocate a template');
 }
 same(module.api.createPlayerAlbedo('skin'),null,'No unsolicited skin tint or unused skin albedo map');
 same(Object.keys(module.templates()),keys,'Arbitrary runtime strings cannot grow the cache');same(module.audit,computed,'All repeated requests avoid procedural work');
 const all=module.api.createPlayerSurfaces();same(Object.keys(all),kinds);
 for(const kind of kinds){retained.add(all[kind]);bytesMatch(all[kind].image.rgba,`height:${kind}`);}
 delete globalThis.document;for(const key of keys)same(texture(module.api,key),null,'Warm templates cannot bypass document availability');
 installCanvas('no-2d');for(const key of keys)same(texture(module.api,key),null,'Warm templates cannot bypass 2d context availability');same(module.audit,computed);
 installCanvas();const reloaded=loadModule();same(reloaded.templates(),{},'A new module starts with zero retained images');
 for(const key of keys)bytesMatch(texture(reloaded.api,key).image.rgba,key);
 same(reloaded.audit.pixels,keys.length*65536,'A new module regenerates each requested map exactly once');
}finally{
 for(const t of retained)t.dispose();
 if(originalDocument===undefined)delete globalThis.document;else globalThis.document=originalDocument;
}
console.log(`PASS player-surfaces: ${assertions} assertions; 11 RGBA SHA fixtures, seamless knit/leather/wood tint, clean white uniforms, lazy 2.75 MiB bound, isolated ownership/disposal, null paths and correct color/data spaces`);
