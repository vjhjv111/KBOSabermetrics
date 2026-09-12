import type {Position3} from "./pitch-feedback";
export type Trajectory="ground"|"line"|"fly"|"foul";
export type Contact={at:number;position:Position3};
export type BattedFlight={contact?:Contact;trajectory?:Trajectory;exitSpeed:number;launchAngle:number;direction:number};
const GRAVITY=9.81,SPEED_SCALE=Math.sqrt(.63);
export function carryDistance(exitSpeed:number,angle:number,height=1.05){
 const speed=exitSpeed/3.6*SPEED_SCALE,radians=angle*Math.PI/180,up=speed*Math.sin(radians);
 return Math.round(speed*Math.cos(radians)*(up+Math.sqrt(up*up+2*GRAVITY*Math.max(0,height)))/GRAVITY);
}
export function battedBallPosition(result:BattedFlight,now:number):Position3|null{
 const contact=result.contact;if(!contact||now<contact.at)return null;
 const t=(now-contact.at)/1000,speed=result.exitSpeed/3.6*SPEED_SCALE,angle=result.launchAngle*Math.PI/180;
 const horizontal=speed*Math.cos(angle),vertical=speed*Math.sin(angle),height=contact.position.y;
 const landing=(vertical+Math.sqrt(vertical*vertical+2*GRAVITY*Math.max(0,height)))/GRAVITY;
 let distance:number,y:number;
 if(t<=landing){distance=horizontal*t;y=height+vertical*t-GRAVITY*t*t/2;}
 else{const roll=t-landing,rollSpeed=horizontal*(result.trajectory==="ground"?.72:.35),friction=7,rolling=Math.min(roll,rollSpeed/friction);distance=horizontal*landing+rollSpeed*rolling-friction*rolling*rolling/2;y=.065+Math.abs(Math.sin(roll*13))*.16*Math.exp(-roll*2.8);}
 return {x:contact.position.x+Math.sin(result.direction)*distance,y:Math.max(.065,y),z:contact.position.z-Math.cos(result.direction)*distance};
}
