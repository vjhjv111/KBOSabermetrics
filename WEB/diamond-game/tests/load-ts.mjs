import fs from 'node:fs';
import path from 'node:path';
import ts from 'typescript';
import {createRequire} from 'node:module';
const cache=new Map();
export function load(file){
 file=path.resolve(file);if(cache.has(file))return cache.get(file).exports;
 const mod={exports:{}};cache.set(file,mod);const native=createRequire(file);
 const require=id=>{if(id.startsWith('.')){const target=path.resolve(path.dirname(file),id+'.ts');if(fs.existsSync(target))return load(target);}return native(id);};
 const js=ts.transpileModule(fs.readFileSync(file,'utf8'),{compilerOptions:{module:ts.ModuleKind.CommonJS,target:ts.ScriptTarget.ES2022,esModuleInterop:true}}).outputText;
 new Function('require','module','exports',js)(require,mod,mod.exports);return mod.exports;
}
