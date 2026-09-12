type ScenePointer={pointerId:number;pointerType:string;button:number;isPrimary:boolean;preventDefault:()=>void};
type Callbacks={side:()=>"batter"|"pitcher";aim:(event:ScenePointer)=>void;swing:()=>void;chargeStart:()=>void;chargeEnd:()=>void;focus:()=>void;capture:(id:number)=>void;release:(id:number)=>void};
export function scenePointerControls(callbacks:Callbacks){
 let active:{id:number;touch:boolean;side:"batter"|"pitcher"}|null=null;
 return {
  down(event:ScenePointer){
   if(event.button!==0||!event.isPrimary||active)return;
   const touch=event.pointerType==="touch",side=callbacks.side();
   active={id:event.pointerId,touch,side};
   if(touch)event.preventDefault();
   callbacks.focus();callbacks.capture(event.pointerId);
   // Both the aim ref and parent callback are updated before the swing freezes its aim.
   callbacks.aim(event);
   if(side==="batter")callbacks.swing();else if(!touch)callbacks.chargeStart();
  },
  move(event:ScenePointer){
   if(!event.isPrimary||active&&active.id!==event.pointerId)return;
   if(active?.touch&&active.side==="batter"){event.preventDefault();return;}
   callbacks.aim(event);
  },
  up(event:ScenePointer){
   if(!active||active.id!==event.pointerId)return;
   const previous=active;active=null;
   if(!previous.touch&&previous.side==="pitcher")callbacks.chargeEnd();
   callbacks.release(event.pointerId);
  }
 };
}
