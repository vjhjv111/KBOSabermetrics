// The game keeps its own React/3D lifecycle inside the record room navigation.
const diamondRoot=text('main','','diamond-page');
diamondRoot.id='diamond-page';diamondRoot.hidden=true;
const diamondFrame=document.createElement('iframe');
diamondFrame.id='diamond-frame';diamondFrame.title='DIAMOND KBO 야구 대결';
diamondFrame.allow='fullscreen; autoplay';diamondFrame.setAttribute('allowfullscreen','');
diamondFrame.referrerPolicy='same-origin';
diamondRoot.append(diamondFrame);$('workspace').before(diamondRoot);
const diamondNav=text('button','야구게임','room');diamondNav.type='button';diamondNav.dataset.route='diamond';
diamondNav.onclick=()=>{location.hash='diamond';diamondRoute();};
document.querySelector('.room-nav').append(diamondNav);
function diamondVisibility(){
  diamondFrame.contentWindow?.postMessage({type:'saber:visibility',active:!diamondRoot.hidden&&!document.hidden},location.origin);
}
function diamondRoute(){
  const active=location.hash==='#diamond';diamondRoot.hidden=!active;
  if(active){
    abortQuery();
    for(const id of ['workspace','home-page','player-page','team-page','game-page']){const page=$(id);if(page)page.hidden=true;}
    document.title='야구게임 · FANZAI';
    if(!diamondFrame.getAttribute('src'))diamondFrame.src='/diamond/index.html';
  }
  // Existing record navigation also updates these classes; apply route ownership last.
  syncRoomNavigation();
  diamondVisibility();
}
diamondFrame.addEventListener('load',diamondVisibility);
window.addEventListener('hashchange',diamondRoute);
document.addEventListener('visibilitychange',diamondVisibility);
window.addEventListener('message',event=>{
  if(event.origin!==location.origin||event.source!==diamondFrame.contentWindow)return;
  if(event.data?.type==='diamond:ready')diamondVisibility();
  if(event.data?.type==='diamond:height'&&Number.isFinite(event.data.height)){
    diamondFrame.style.height=`${Math.max(500,Math.min(4000,event.data.height))}px`;
  }
  if(event.data?.type==='diamond:records')location.hash='records';
});
diamondRoute();
