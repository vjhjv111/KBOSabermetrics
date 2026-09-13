import fs from 'node:fs';
import path from 'node:path';
import {fileURLToPath} from 'node:url';
import {createRequire} from 'node:module';
import {createHash} from 'node:crypto';
import assert from 'node:assert/strict';
import {load} from './load-ts.mjs';

const require=createRequire(import.meta.url),ts=require('typescript'),T=require('three');
const headPath=fileURLToPath(new URL('../lib/player-head.ts',import.meta.url)),source=fs.readFileSync(headPath,'utf8');
const sha=data=>createHash('sha256').update(data).digest('hex');
// Original uncached 256 × 128 pigment, including pupil, iris, iris edge and
// sclera. Fixed data fixtures require no baseline source or work/ directory.
const expectedSha='c246b5947a79f29ec382ad62615932d5310be4464d69fdc38a106ae82d006358';
const samplePixels=[0,127,128,14207,16255,17562,17574,32767];
const expectedSamples=[[210,201,184,255],[210,201,184,255],[210,201,184,255],[21,25,24,255],[21,25,24,255],[60,51,42,255],[46,39,32,255],[210,201,184,255]];
let assertions=0;
const equal=(a,b,message)=>{assertions++;assert.deepEqual(a,b,message);};
const check=(condition,message)=>{assertions++;assert(condition,message);};
function loadModule(){
 const audit={pixels:0},mod={exports:{}};
 const localRequire=id=>id.startsWith('.')?load(path.resolve(path.dirname(headPath),id+'.ts')):require(id);
 // Test-only inspection returns a copy of the private template, never a
 // writable alias. The counter verifies lazy generation without timing tests.
 const input=source.replace('const color=eyePigment','audit.pixels++; const color=eyePigment');
 assert(input.includes('audit.pixels++'),'Pixel-loop instrumentation remains valid');
 const js=ts.transpileModule(input,{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,esModuleInterop:true}}).outputText;
 const helpers=new Function('require','module','exports','audit',js+';return {texture:eyeTexture,template:()=>eyePixelTemplate?.slice()??null};')(localRequire,mod,mod.exports,audit);
 return {api:mod.exports,audit,...helpers};
}
const samplers=t=>({prototype:Object.getPrototypeOf(t).constructor.name,isDataTexture:t.isDataTexture,width:t.image.width,height:t.image.height,dataType:t.image.data.constructor.name,
 colorSpace:t.colorSpace,format:t.format,type:t.type,wrapS:t.wrapS,wrapT:t.wrapT,magFilter:t.magFilter,minFilter:t.minFilter,anisotropy:t.anisotropy,
 flipY:t.flipY,generateMipmaps:t.generateMipmaps,unpackAlignment:t.unpackAlignment,premultiplyAlpha:t.premultiplyAlpha,
 repeat:t.repeat.toArray(),offset:t.offset.toArray(),center:t.center.toArray(),rotation:t.rotation,version:t.version,sourceVersion:t.source.version});
const expectedSamplers={prototype:'DataTexture',isDataTexture:true,width:256,height:128,dataType:'Uint8Array',
 colorSpace:T.SRGBColorSpace,format:T.RGBAFormat,type:T.UnsignedByteType,wrapS:T.ClampToEdgeWrapping,wrapT:T.ClampToEdgeWrapping,
 magFilter:T.LinearFilter,minFilter:T.LinearMipmapLinearFilter,anisotropy:1,flipY:false,generateMipmaps:true,unpackAlignment:1,premultiplyAlpha:false,
 repeat:[1,1],offset:[0,0],center:[0,0],rotation:0,version:1,sourceVersion:1};
function bytesMatch(bytes){
 equal(bytes.byteLength,131072,'Complete original pigment size');equal(sha(bytes),expectedSha,'Every original pigment byte is preserved');
 equal(samplePixels.map(i=>Array.from(bytes.slice(i*4,i*4+4))),expectedSamples,'Original pupil/iris/sclera samples');
 check(bytes.every((value,index)=>index%4!==3||value===255),'Eye pigment remains opaque');
}
const oldDocument=globalThis.document,retained=new Set(),heads=[],module=loadModule();
const get=(source,...args)=>{const texture=source.texture(...args);if(texture)retained.add(texture);return texture;};
function disposeHead(head,extra=[]){
 const geometries=new Set(),materials=new Set(extra),textures=new Set();
 head.traverse(o=>{if(!o.isMesh)return;geometries.add(o.geometry);for(const m of Array.isArray(o.material)?o.material:[o.material])materials.add(m);});
 for(const m of materials)for(const v of Object.values(m))if(v?.isTexture)textures.add(v);
 for(const g of geometries)g.dispose();for(const m of materials)m.dispose();for(const t of textures)t.dispose();
}
const geometrySnapshot=geometry=>({index:Array.from(geometry.index.array),attributes:Object.fromEntries(Object.entries(geometry.attributes).map(([key,a])=>[key,Array.from(a.array)]))});
try{
 equal(Object.keys(module.api).sort(),['capProfile','hairGeometry','headDetails'],'Original public exports');
 equal(module.api.headDetails.length,5,'Original headDetails arity');equal(module.api.hairGeometry.length,0,'Original optional hair-style argument');
 delete globalThis.document;
 equal(get(module),null,'SSR returns null');equal(module.template(),null,'SSR never populates an unused cache');equal(module.audit.pixels,0);
 // This is a DataTexture, not CanvasTexture. The original document-presence
 // guard does not require a functioning canvas or a 2d context.
 let domCalls=0;globalThis.document={createElement(){domCalls++;throw new Error('Eye pigment must not require Canvas2D');}};
 const first=get(module),second=get(module);bytesMatch(first.image.data);equal(samplers(first),expectedSamplers,'Original DataTexture and sampler contract');
 check(first instanceof T.DataTexture,'Original texture prototype');equal(domCalls,0,'No Canvas2D calls');equal(module.audit.pixels,32768,'One lazy pigment pass');
 check(first!==second&&first.source!==second.source&&first.image!==second.image,'Independent Texture, Source and image descriptor');
 check(first.image.data!==second.image.data&&first.image.data.buffer!==second.image.data.buffer,'Independent pixel upload buffers');
 let firstDisposals=0,secondDisposals=0;first.addEventListener('dispose',()=>firstDisposals++);second.addEventListener('dispose',()=>secondDisposals++);
 first.image.data.fill(0);first.repeat.set(7,11);first.wrapS=T.RepeatWrapping;first.source.data={data:new Uint8Array(4),width:1,height:1};first.needsUpdate=true;first.dispose();retained.delete(first);
 equal(firstDisposals,1);equal(secondDisposals,0,'One head cannot dispose another head texture');
 bytesMatch(second.image.data);equal(samplers(second),expectedSamplers,'Sibling sampler/image/upload state remains unchanged');
 const later=get(module);bytesMatch(later.image.data);equal(samplers(later),expectedSamplers,'Later construction recovers all original settings');
 const inspected=module.template();inspected.fill(55);bytesMatch(module.template());
 for(let i=0;i<12;i++)equal(sha(get(module,'unused-'+i).image.data),expectedSha,'Unexpected JS arguments do not add pigment variants');
 equal(module.template().byteLength,131072,'One fixed 128 KiB template');equal(module.audit.pixels,32768,'Repeated requests never calculate pigment pixels again');
 delete globalThis.document;equal(get(module),null,'A warm cache cannot bypass the document gate');equal(module.audit.pixels,32768);
 globalThis.document={};const reloaded=loadModule();equal(reloaded.template(),null,'A newly loaded module starts empty');bytesMatch(get(reloaded).image.data);equal(reloaded.audit.pixels,32768,'Independent module regenerates pigment once');

 // Exercise the real public construction path in both SSR and browser modes.
 // Only the pigment representation changes; eye positions, UVs, colors and
 // indices must be identical, including the vertex-color SSR fallback.
 const eyeShapes=new Map(),eyeMaps=[];
 for(const browser of [false,true])for(const isBatter of [false,true]){
  if(browser)globalThis.document={};else delete globalThis.document;
  const head=new T.Group(),skin=new T.MeshStandardMaterial({color:'#c89675',roughness:.7}),cap=new T.MeshStandardMaterial({color:'#142638',roughness:.8});
  heads.push({head,extra:[skin,cap]});module.api.headDetails(head,skin,cap,null,isBatter);
  const eye=head.getObjectByName('Recessed almond eyes');check(!!eye,'Public head construction includes the eye surface');equal(eye.material.vertexColors,!browser,'Original SSR color fallback');
  if(!browser){equal(eye.material.map,null,'SSR has no texture');eyeShapes.set(isBatter,geometrySnapshot(eye.geometry));}
  else{
   bytesMatch(eye.material.map.image.data);equal(samplers(eye.material.map),expectedSamplers,'Public head receives the original map settings');
   equal(geometrySnapshot(eye.geometry),eyeShapes.get(isBatter),'Cache availability cannot change eye geometry or its fallback colors');eyeMaps.push(eye.material.map);
  }
 }
 check(eyeMaps[0]!==eyeMaps[1]&&eyeMaps[0].source!==eyeMaps[1].source&&eyeMaps[0].image.data!==eyeMaps[1].image.data,'Separate public head calls own separate maps');
 equal(module.audit.pixels,32768,'Public head construction reuses the single pigment template');
}finally{
 for(const t of retained)t.dispose();for(const entry of heads)disposeHead(entry.head,entry.extra);
 if(oldDocument===undefined)delete globalThis.document;else globalThis.document=oldDocument;
}
console.log(`PASS player-eye-texture: ${assertions} assertions; original pigment SHA/samples, DataTexture/API/samplers, independent lifetime and mutation recovery, lazy 128 KiB bound, public head/SSR eye geometry`);
