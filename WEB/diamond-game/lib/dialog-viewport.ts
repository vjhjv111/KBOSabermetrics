/** A full-height game iframe can be much taller than the phone displaying it. */
export function visibleDialogViewport(frameTop:number,frameHeight:number,childHeight:number,viewportTop:number,viewportHeight:number){
 const scale=frameHeight/childHeight;
 if(!Number.isFinite(scale)||scale<=0||viewportHeight<=0)return null;
 const top=Math.max(0,(viewportTop-frameTop)/scale);
 const bottom=Math.min(childHeight,(viewportTop+viewportHeight-frameTop)/scale);
 if(bottom<=top)return null;
 const gap=Math.min(12,(bottom-top)/10);
 return {top:top+gap,height:bottom-top-gap*2};
}

export function fitDialogToParentViewport(dialog:HTMLDialogElement,view:Window=window){
 let frame:Element|null,parent:Window;
 try{frame=view.frameElement;parent=view.parent;if(!frame||parent===view)return()=>{};void parent.document;}
 catch{return()=>{};}
 const viewport=parent.visualViewport;
 const update=()=>{
  const bounds=frame!.getBoundingClientRect(),fit=visibleDialogViewport(bounds.top,bounds.height,view.innerHeight,viewport?.offsetTop??0,viewport?.height??parent.innerHeight);
  if(!fit)return;
  dialog.dataset.embeddedViewport='true';
  dialog.style.setProperty('--save-dialog-top',fit.top+'px');
  dialog.style.setProperty('--save-dialog-height',fit.height+'px');
 };
 update();
 parent.addEventListener('scroll',update,{passive:true});parent.addEventListener('resize',update);
 viewport?.addEventListener('scroll',update);viewport?.addEventListener('resize',update);
 const observer=new ResizeObserver(update);observer.observe(frame);
 return()=>{
  observer.disconnect();parent.removeEventListener('scroll',update);parent.removeEventListener('resize',update);
  viewport?.removeEventListener('scroll',update);viewport?.removeEventListener('resize',update);
  delete dialog.dataset.embeddedViewport;dialog.style.removeProperty('--save-dialog-top');dialog.style.removeProperty('--save-dialog-height');
 };
}
