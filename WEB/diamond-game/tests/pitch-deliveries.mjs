import assert from 'node:assert/strict';
import fs from 'node:fs';
import ts from 'typescript';
import {createRequire} from 'node:module';
import {load} from './load-ts.mjs';
import path from 'node:path';
import os from 'node:os';
import {spawnSync} from 'node:child_process';
const THREE=createRequire(import.meta.url)('three'),V=(...a)=>new THREE.Vector3(...a);
const motion=load('lib/player-motion.ts'),engine=load('lib/action-engine.ts'),roster=load('lib/roster.ts'),feedback=load('lib/pitch-feedback.ts');
const {createPlayer}=load('lib/player-model.ts');
const {posePitcher}=load('lib/player-pose.ts');
const styleCases=[['우투우타','overhand'],['좌투좌타','overhand'],['우사우타','sidearm'],['좌사우타','sidearm'],['사이드암','sidearm'],['Side-arm','sidearm'],['우언우타','underhand'],['좌언좌타','underhand'],['언더핸드','underhand'],['submarine','underhand']];
for(const [description,style]of styleCases)assert.equal(motion.normalizeDelivery(undefined,description),style);
assert.equal(motion.normalizeDelivery('overhand','우언우타'),'overhand');
assert.equal(motion.normalizeDelivery('underhand','좌사우타'),'underhand');
assert.equal(motion.normalizeDelivery('사이드암','좌투좌타'),'sidearm');
assert.equal(motion.normalizeDelivery(true),'underhand');assert.equal(motion.normalizeDelivery(false),'overhand');
for(const [setting,description,expected]of [['L','우투우타','L'],['left','우사우타','L'],['우투','좌투좌타','R'],[undefined,' 좌사우타 ','L'],['unknown','우언우타','R']])assert.equal(motion.profileThrowingHand(setting,description),expected);
for(const bad of[NaN,Infinity,-20,0,251])assert.equal(motion.pitcherBodyScale(bad),1);
const styles=['overhand','sidearm','underhand'],pitchers=[];
for(const style of styles)for(const hand of[1,-1])pitchers.push({id:`2025:${style}-${hand}`,playerId:`${style}-${hand}`,name:`fixture ${style}`,team:'TEST',profile:{batsThrows:'우투우타',throws:hand===1?'R':'L',delivery:style,heightCm:185},discipline:{},tbf:100,bb:5,so:20,outs:75,er:5,era:1.8,whip:1.2,arsenal:[{type:'fastball',velocity:145,usage:100}],arsenalSource:'default'});
roster.registerRoster({season:2025,seasons:[2025],asOf:'test',revision:'deliveries',teams:[],batters:[],pitchers});
let maxWrist=0,maxFoot=0,maxVelocityGap=0,frames=0;const summaries=[];
for(const style of styles){const poses=[];
 for(const hand of[1,-1]){
  const id=`2025:${style}-${hand}`;assert.equal(engine.deliveryStyle(id),style);assert.equal(engine.throwsLeft(id),hand===-1);
  const model=createPlayer('#224466');model.root.position.z=-18.44;model.root.scale.set(hand,1,1);
  for(let time=-motion.PITCH_WINDUP_MS-100;time<=motion.PITCH_RECOVERY_MS+100;time+=5){
   const pose=motion.pitchingPose(time,style);posePitcher(model,pose,hand,style==='underhand');
   for(const value of Object.values(pose))if(typeof value==='number')assert(Number.isFinite(value));else if(Array.isArray(value))assert(value.every(Number.isFinite));
   for(const [upper,lower,position]of[[model.right,model.re,pose.hand],[model.left,model.le,pose.glove]]){
    const wrist=model.root.localToWorld(V(...position)),actual=lower.localToWorld(V(0,-.34,0)),error=actual.distanceTo(wrist);maxWrist=Math.max(maxWrist,error);
    assert(error<.01,`${style}/${hand} wrist ${time}: ${error}`);
    assert(upper.getWorldPosition(V()).distanceTo(wrist)<.681,`${style}/${hand} unreachable ${time}`);
   }
   assert(pose.lead[1]>=.034-1e-10&&pose.trail[1]>=.034-1e-10,'Feet must stay above ground');
   if(time>=0&&time<=350){const error=model.lf.getWorldPosition(V()).distanceTo(model.root.localToWorld(V(...pose.lead)));maxFoot=Math.max(maxFoot,error);assert(error<.01,'Lead foot must stay planted');}
   frames++;
  }
  for(const height of[160,185,210]){
   const scale=motion.pitcherBodyScale(height);model.root.scale.set(hand*scale,scale,scale);
   const pose=motion.pitchingPose(0,style);posePitcher(model,pose,hand,style==='underhand');
   const expected=V(...motion.pitchReleasePosition(style,hand,height)),actual=model.re.localToWorld(V(0,-.34,0));assert(actual.distanceTo(expected)<1e-10,'Release hand must equal server baseball center');
   if(height===185){const pitch=engine.createPitch({pitcher:id,pitchCount:0,pace:'full',mode:'ai'},'fastball',{x:0,y:0},1,1000),ball=engine.ballPosition(pitch,pitch.releaseAt);assert(V(ball.x,ball.y,ball.z).distanceTo(expected)<1e-10);assert.equal(Math.sign(ball.x),-hand);}
  }
  const pose=motion.pitchingPose(0,style);poses.push({hand,release:motion.pitchReleasePosition(style,hand,185)});
  const shoulder=model.root.worldToLocal(model.right.getWorldPosition(V()));const arm=V(...pose.hand).sub(shoulder);summaries.push({style,hand,release:pose.hand,armSlotDegrees:Math.atan2(arm.y,Math.hypot(arm.x,arm.z))*180/Math.PI});
 }
 assert.equal(poses[0].release[0],-poses[1].release[0]);assert.deepEqual(poses[0].release.slice(1),poses[1].release.slice(1));
 for(const time of[-motion.PITCH_WINDUP_MS,-1570,-1270,-1000,-800,-560,-330,-150,-65,0,85,210,350,550,770,motion.PITCH_RECOVERY_MS]){
  const h=.001,before=motion.pitchingPose(time-h,style),at=motion.pitchingPose(time,style),after=motion.pitchingPose(time+h,style);
  for(const track of['hand','glove','lead','trail','throwElbow','gloveElbow','lean','coil','hips','drop','forward','heel','sideBend']){
   const values=p=>Array.isArray(p[track])?p[track]:[p[track]],a=values(before),b=values(at),c=values(after);
   const gap=Math.hypot(...b.map((v,i)=>((v-a[i])-(c[i]-v))/(h/1000)));maxVelocityGap=Math.max(maxVelocityGap,gap);assert(gap<.01,`${style}/${track} velocity discontinuity at ${time}: ${gap}`);
  }
 }
 for(const time of[-Infinity,Infinity,NaN])assert(motion.pitchingPose(time,style).hand.every(Number.isFinite));
 assert.deepEqual(motion.pitchingPose(-motion.PITCH_WINDUP_MS-100,style),motion.pitchingPose(motion.PITCH_RECOVERY_MS+100,style),'Every style returns to ready');
}
assert(motion.PITCH_RELEASE.overhand[1]>motion.PITCH_RELEASE.sidearm[1]&&motion.PITCH_RELEASE.sidearm[1]>motion.PITCH_RELEASE.underhand[1]);
assert(motion.pitchingPose(0,'underhand').drop>motion.pitchingPose(0,'sidearm').drop&&motion.pitchingPose(0,'sidearm').drop>motion.pitchingPose(0,'overhand').drop);
assert.equal(engine.pitcherHandLabel('2025:sidearm--1'),'좌투 · 사이드암');
console.log(JSON.stringify({pass:true,combinations:6,frames,maxWrist,maxFoot,maxVelocityGap,summaries},null,2));

// Opt-in C# parity check compiles the real server helper in an isolated temporary project.
if(process.argv.includes('--server')){
 const directory=fs.mkdtempSync(path.join(os.tmpdir(),'diamond-deliveries-'));
 const data=fs.readFileSync('../server/src/NaverSabermetrics.Web/DiamondData.cs','utf8');
 const helper=data.slice(data.indexOf('public static class DiamondDelivery'),data.indexOf('public sealed class DiamondDiscipline'));
 const requests=[];
 for(const [description]of styleCases)requests.push({value:null,description,height:185,hand:1});
 for(const style of styles)for(const hand of[1,-1])for(const height of[160,185,210,0,251])requests.push({value:style,description:'우언우타',height,hand});
 for(const bodyType of ['lean','athletic','power'])requests.push({value:'overhand',description:'좌투좌타',height:185,hand:-1,bodyType});
 fs.writeFileSync(path.join(directory,'checks.json'),JSON.stringify(requests));
 fs.writeFileSync(path.join(directory,'Program.cs'),`using System;using System.IO;using System.Collections.Generic;using System.Text.Json;
public sealed record DiamondPosition(double X,double Y,double Z);
${helper}
public static class Program { public static void Main(string[] args) {
using var doc=JsonDocument.Parse(File.ReadAllText(args[0]));var results=new List<object>();
foreach(var item in doc.RootElement.EnumerateArray()) { var value=item.GetProperty("value").GetString();var description=item.GetProperty("description").GetString();var style=DiamondDelivery.Normalize(value,description);var release=DiamondDelivery.Release(style,item.GetProperty("hand").GetInt32(),item.GetProperty("height").GetDouble());var factors=new List<double>();var bodyType=item.TryGetProperty("bodyType",out var body)?body.GetString():null;for(var index=0;index<23;index++)factors.Add(DiamondDelivery.ColliderRadiusFactor(index,bodyType));results.Add(new {style,release=new[]{release.X,release.Y,release.Z},hand=DiamondDelivery.ThrowingHand(null,description),factors}); }
Console.WriteLine(JsonSerializer.Serialize(results)); }}
`);
 // Compile against the installed SDK reference pack, without restoring packages or reading user NuGet settings.
 const sdkRoot=process.env.DOTNET_ROOT??(process.platform==='win32'?path.join(process.env.ProgramFiles??'C:/Program Files','dotnet'):'/usr/share/dotnet');
 const version=(dir)=>fs.readdirSync(dir).filter(x=>x.startsWith('8.0.')).sort((a,b)=>a.localeCompare(b,undefined,{numeric:true})).at(-1);
 const sdk=path.join(sdkRoot,'sdk',version(path.join(sdkRoot,'sdk'))),ref=path.join(sdkRoot,'packs','Microsoft.NETCore.App.Ref',version(path.join(sdkRoot,'packs','Microsoft.NETCore.App.Ref')),'ref','net8.0');
 const dll=path.join(directory,'Parity.dll'),response=path.join(directory,'compiler.rsp');
 fs.writeFileSync(response,['/nologo','/nullable:enable','/target:exe',`/out:"${dll}"`,...fs.readdirSync(ref).filter(f=>f.endsWith('.dll')).map(f=>`/reference:"${path.join(ref,f)}"`),`"${path.join(directory,'Program.cs')}"`].join('\n'));
 const compile=spawnSync('dotnet',[path.join(sdk,'Roslyn','bincore','csc.dll'),'@'+response],{encoding:'utf8',timeout:30000,windowsHide:true});assert.equal(compile.status,0,compile.stderr+'\n'+compile.stdout);
 fs.writeFileSync(path.join(directory,'Parity.runtimeconfig.json'),JSON.stringify({runtimeOptions:{tfm:'net8.0',framework:{name:'Microsoft.NETCore.App',version:'8.0.0'}}}));
 const run=spawnSync('dotnet',[dll,path.join(directory,'checks.json')],{encoding:'utf8',timeout:30000,windowsHide:true});
 assert.equal(run.status,0,run.stderr+'\n'+run.stdout);
 const line=run.stdout.trim().split(/\r?\n/).findLast(x=>x.startsWith('[{')),actual=JSON.parse(line);
 requests.forEach((request,i)=>{const style=motion.normalizeDelivery(request.value,request.description);assert.equal(actual[i].style,style);assert.equal(actual[i].hand,motion.profileThrowingHand(undefined,request.description)??null);const expected=motion.pitchReleasePosition(style,request.hand,request.height);assert(V(...actual[i].release).distanceTo(V(...expected))<1e-12,'C#/TypeScript release mismatch');assert.deepEqual(actual[i].factors,Array.from({length:23},(_,index)=>feedback.bodyColliderRadiusFactor(index,request.bodyType)),'C#/TypeScript body width mismatch');});
 console.log(`PASS ${requests.length} C#/TypeScript delivery profile, release and body width parity cases`);
}
