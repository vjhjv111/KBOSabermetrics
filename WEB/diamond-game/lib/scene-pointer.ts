type ScenePointer={pointerId:number;pointerType:string;button:number;isPrimary:boolean;clientX:number;clientY:number;timeStamp:number;preventDefault:()=>void};
type Callbacks={side:()=>"batter"|"pitcher";aim:(event:ScenePointer)=>void;swing:()=>void;chargeStart:()=>void;chargeEnd:()=>void;chargeCancel:()=>void;focus:()=>void;capture:(id:number)=>void;release:(id:number)=>void};
export function scenePointerControls(callbacks:Callbacks){
 let active:{id:number;touch:boolean;side:"batter"|"pitcher";start:ScenePointer;moved:boolean}|null=null;
 const isTap=(start:ScenePointer,event:ScenePointer)=>Math.hypot(event.clientX-start.clientX,event.clientY-start.clientY)<=12&&event.timeStamp-start.timeStamp>=0&&event.timeStamp-start.timeStamp<=350;
 function cancel(event:ScenePointer){
  if(!active||active.id!==event.pointerId)return;
  const previous=active;active=null;
  if(!previous.touch){if(previous.side==="pitcher")callbacks.chargeCancel();callbacks.release(event.pointerId);}
 }
 return {
  down(event:ScenePointer){
   if(event.pointerType==="touch"&&!event.isPrimary&&active?.touch){cancel(active.start);return;}
   if(event.button!==0||!event.isPrimary||active)return;
   const touch=event.pointerType==="touch",side=callbacks.side();
   active={id:event.pointerId,touch,side,start:event,moved:false};
   // Let the browser distinguish a tap from scrolling or pinch zoom first.
   if(touch)return;
   callbacks.focus();callbacks.capture(event.pointerId);
   callbacks.aim(event);
   if(side==="batter")callbacks.swing();else callbacks.chargeStart();
  },
  move(event:ScenePointer){
   if(!event.isPrimary||active&&active.id!==event.pointerId)return;
   if(event.pointerType==="touch"){
    if(active&&!isTap(active.start,event))active.moved=true;
    return;
   }
   callbacks.aim(event);
  },
  up(event:ScenePointer){
   if(!active||active.id!==event.pointerId)return;
   const previous=active;active=null;
   if(previous.touch){
    if(previous.moved||!event.isPrimary||previous.side!==callbacks.side()||!isTap(previous.start,event))return;
    callbacks.focus();
    // Freeze the initially touched location before the accepted tap starts its swing.
    callbacks.aim(previous.start);
    if(previous.side==="batter")callbacks.swing();
   }else{if(previous.side==="pitcher")callbacks.chargeEnd();callbacks.release(event.pointerId);}
  },
  cancel
 };
}
