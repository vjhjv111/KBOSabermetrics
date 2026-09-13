import {ArrowRight,ChevronRight,CloudCheck,RefreshCw,Save,Swords,Target,Trophy,UserRound} from 'lucide-react';
import type {SeasonTab} from '../lib/title-navigation';
import type {StadiumTimeOfDay} from '../lib/korea-daylight';
import {useKoreaTimeOfDay} from '../lib/use-korea-daylight';
import './title-screen.css';

type Props={
 onNavigate:(tab:SeasonTab)=>void;
 onOpenSave:()=>void;
 onRetry:()=>void;
 loading:boolean;
 error:string;
 savedTeam:string|null;
 careerName:string|null;
};

/** A static vector stadium keeps the menu instant and creates no game clock. */
function TitleStadium({timeOfDay}:{timeOfDay:StadiumTimeOfDay}){
 const day=timeOfDay==='day';
 return <svg className="title-stadium" viewBox="0 0 1200 800" aria-hidden="true" focusable="false">
  <defs>
   <linearGradient id="title-sky" x2="0" y2="1"><stop stopColor={day?'#56b6ec':'#142638'}/><stop offset=".65" stopColor={day?'#b7e6f3':'#365d65'}/><stop offset="1" stopColor={day?'#e8f1cd':'#142c32'}/></linearGradient>
   <linearGradient id="title-turf" x1="0" y1="0" x2="0" y2="1"><stop stopColor={day?'#82b54e':'#477752'}/><stop offset="1" stopColor={day?'#427c3e':'#163d35'}/></linearGradient>
   <linearGradient id="title-dirt" x1="0" y1="0" x2="0" y2="1"><stop stopColor={day?'#d9ad77':'#bf9270'}/><stop offset="1" stopColor={day?'#b98b60':'#886e57'}/></linearGradient>
   <linearGradient id="title-stands" x2="0" y2="1"><stop stopColor={day?'#9fb9be':'#507178'}/><stop offset="1" stopColor={day?'#638998':'#1c3c43'}/></linearGradient>
   <radialGradient id="title-glow"><stop stopColor="#efffde" stopOpacity=".7"/><stop offset=".2" stopColor="#dcebc4" stopOpacity=".18"/><stop offset="1" stopColor="#dcebc4" stopOpacity="0"/></radialGradient>
   <linearGradient id="title-beam" x2="0" y2="1"><stop stopColor="#e6f9cf" stopOpacity=".12"/><stop offset="1" stopColor="#e6f9cf" stopOpacity="0"/></linearGradient>
   <pattern id="title-crowd" width="14" height="11" patternUnits="userSpaceOnUse"><circle cx="3" cy="4" r="1.8" fill="#b0b8a2"/><circle cx="10" cy="8" r="1.5" fill="#567b7d"/></pattern>
   <pattern id="title-mowing" width="70" height="800" patternUnits="userSpaceOnUse" patternTransform="rotate(33)"><rect width="35" height="800" fill="#d2edaf" opacity=".075"/></pattern>
   <clipPath id="title-field"><path d="M130 380Q655 83 1180 380L680 765Z"/></clipPath>
  </defs>
  <rect width="1200" height="800" fill="url(#title-sky)"/>
  {day?<g className="title-day-sky"><circle cx="1010" cy="75" r="53" fill="#fff5bc" opacity=".17"/><circle cx="1010" cy="75" r="34" fill="#fff1a7"/><g fill="#fff" opacity=".86"><path d="M537 86a19 19 0 0 1 21-18 31 31 0 0 1 58-9 25 25 0 0 1 35 27Z"/><path d="M1087 175a17 17 0 0 1 18-19 27 27 0 0 1 51-8 22 22 0 0 1 29 27Z"/><path d="M738 35a12 12 0 0 1 15-12 21 21 0 0 1 40-2 15 15 0 0 1 23 14Z"/></g></g>:<g className="title-night-stars" fill="#a2c6c5" opacity=".55"><circle cx="440" cy="57" r="1.2"/><circle cx="735" cy="85" r="1"/><circle cx="1035" cy="44" r="1.3"/><circle cx="914" cy="155" r="1"/><circle cx="593" cy="136" r=".8"/></g>}
  <path d="M40 257V221H76V196H114V239H168V208H207V244H243V211H281V249H1016V222H1052V199H1088V232H1140V210H1190V262Z" fill={day?'#88b4c2':'#192e3d'}/>
  <path d="M59 356Q655 37 1251 356L1144 444Q655 171 166 444Z" fill={day?'#47727d':'#132b37'}/>
  <path d="M98 326Q655 47 1212 326L1183 369Q655 101 127 369Z" fill="url(#title-stands)"/>
  <path d="M98 326Q655 47 1212 326L1183 369Q655 101 127 369Z" fill="url(#title-crowd)" opacity=".6"/>
  <path d="M99 325Q655 46 1211 325M127 369Q655 101 1183 369" fill="none" stroke="#97b9ae" strokeWidth="3" opacity=".5"/>
  <path d="M133 379Q655 121 1177 379L1149 418Q655 173 161 418Z" fill="url(#title-stands)"/>
  <path d="M133 379Q655 121 1177 379L1149 418Q655 173 161 418Z" fill="url(#title-crowd)" opacity=".85"/>
  <path d="M135 379Q655 121 1175 379" fill="none" stroke="#d5cf9b" strokeWidth="4" opacity=".6"/>
  <path d="M163 423Q655 184 1147 423" fill="none" stroke="#d0e5b5" strokeWidth="2" opacity=".45"/>
  <path d="M150 426Q655 177 1160 426L1215 800H115Z" fill={day?'#467e45':'#163c36'}/>
  <g clipPath="url(#title-field)"><rect y="185" width="1200" height="615" fill="url(#title-turf)"/><rect y="185" width="1200" height="615" fill="url(#title-mowing)"/></g>
  <path d="M680 706L440 509Q680 307 920 509Z" fill="url(#title-dirt)"/>
  <path d="M680 667L489 510L680 393L871 510Z" fill={day?'#57963c':'#2d6347'}/>
  <path d="M680 667L489 510L680 393L871 510Z" fill="url(#title-mowing)"/>
  <ellipse cx="680" cy="548" rx="30" ry="17" fill="#b28e6b"/>
  <path d="M670 545H690" stroke="#e1e1c8" strokeWidth="4"/>
  <ellipse cx="680" cy="716" rx="61" ry="27" fill="url(#title-dirt)"/>
  <path d="M233 345L680 716L1127 345" fill="none" stroke="#e7eacb" strokeWidth="2.5" opacity=".85"/>
  <path d="M666 710H680V720L673 725L666 720Z" fill="#f0edda"/>
  <g fill="#eee9d0"><path d="M477 507L489 499L501 508L489 517Z"/><path d="M668 393L680 385L692 393L680 401Z"/><path d="M859 508L871 499L883 507L871 517Z"/></g>
  <path d="M640 700L652 691L668 705L656 714ZM687 691L701 700L688 714L674 705" fill="none" stroke="#ece9cb" strokeWidth="1.5" opacity=".7"/>
  <g stroke="#728e92" strokeWidth="5"><path d="M242 283V139M456 210V90M901 216V97M1100 292V147"/></g>
  {!day&&<g className="title-floodlight-beams" fill="url(#title-beam)"><path d="M240 137L80 583L480 606Z"/><path d="M456 88L285 565L644 608Z"/><path d="M901 95L710 600L1115 562Z"/><path d="M1100 145L915 620L1260 510Z"/></g>}
  {[{x:242,y:135},{x:456,y:86},{x:901,y:93},{x:1100,y:143}].map(({x,y})=><g key={x}>{!day&&<ellipse className="title-floodlight-glow" cx={x} cy={y} rx="123" ry="100" fill="url(#title-glow)"/>}<rect x={x-31} y={y-8} width="62" height="14" rx="2" fill={day?'#9aacad':'#d6e5cc'}/><path d={`M${x-20} ${y-6}v10m14-10v10m14-10v10m14-10v10`} stroke="#789392" strokeWidth="2"/></g>)}
  <path d="M0 770Q680 527 1200 792V800H0Z" fill="#0a2228" opacity=".3"/>
 </svg>;
}

export default function TitleScreen({onNavigate,onOpenSave,onRetry,loading,error,savedTeam,careerName}:Props){
 const timeOfDay=useKoreaTimeOfDay(),day=timeOfDay==='day';
 const cards=[
  {tab:'friendly' as const,number:'01',name:'친선 경기',detail:'AI와 한 경기, 친구와 한 승부.',icon:Swords,tag:'AI · 친구 초대'},
  {tab:'career' as const,number:'02',name:'마이 플레이어',detail:careerName?`${careerName} 선수의 다음 성장을 준비하세요.`:'내 선수를 만들고, 나만의 커리어를.',icon:UserRound,tag:careerName?'선수 육성':'선수 생성 · 육성'},
  {tab:'practice' as const,number:'03',name:'타석 연습',detail:'보고, 노리고, 스윙. 감각을 깨우세요.',icon:Target,tag:'타격 · 투구'},
 ];
 return <div className="title-screen" data-time-of-day={timeOfDay}>
  <section className="title-hero" aria-labelledby="diamond-title-heading">
   <TitleStadium timeOfDay={timeOfDay}/>
   <div className="title-hero-shade"/>
   <div className="title-hero-copy"><p className="title-eyebrow"><span/>YOUR GAME STARTS HERE</p><h1 id="diamond-title-heading">나의 야구가<br/><em>시작되는 곳.</em></h1><p className="title-description">한 번의 스윙부터, 나만의 시즌까지.<br/>{day?'햇살 가득한 그라운드에서 다음 이야기를 시작하세요.':'불이 켜진 그라운드에서 다음 이야기를 시작하세요.'}</p>
    <div className="title-start"><button type="button" className="title-primary" disabled={loading} onClick={()=>onNavigate('game')}><Trophy size={21} aria-hidden="true"/><span>{loading?'저장 기록 확인 중':savedTeam?'이어하기':'리그 시작'}</span>{loading?<RefreshCw className="title-loading-icon" size={19} aria-hidden="true"/>:<ArrowRight size={21} aria-hidden="true"/>}</button><p className="title-save-status" role="status">{loading?'메뉴를 둘러보는 동안 기록을 불러옵니다.':savedTeam?<><CloudCheck size={14} aria-hidden="true"/>{savedTeam} · 저장된 시즌을 이어갑니다.</>:<>10개 구단 · 9이닝 · 나만의 시즌</>}</p></div>
   </div>
   <div className="title-hero-caption" aria-hidden="true"><span>{day?'UNDER THE BLUE SKY':'UNDER THE LIGHTS'}</span><b>PLAY BALL.</b><i/></div>
  </section>
  {error&&<div className="title-connection" role="alert"><p>저장 기록 연결을 확인해 주세요. 저장 코드로 불러오기도 가능합니다.</p><button type="button" onClick={onRetry}><RefreshCw size={15} aria-hidden="true"/>다시 연결</button></div>}
  <nav className="title-modes" aria-label="플레이 방식 선택">{cards.map(card=><button type="button" className="title-mode" key={card.tab} onClick={()=>onNavigate(card.tab)}><div className="title-mode-top"><span className="title-mode-number">{card.number}</span><span className="title-mode-tag">{card.tag}</span><card.icon size={25} strokeWidth={1.5} aria-hidden="true"/></div><div className="title-mode-bottom"><div><h2>{card.name}</h2><p>{card.detail}</p></div><ChevronRight size={21} aria-hidden="true"/></div></button>)}</nav>
  <footer className="title-footer"><p><span/>진행은 자동 저장됩니다. 저장 코드로 다른 기기에서도 이어가세요.</p><button type="button" onClick={onOpenSave} aria-haspopup="dialog"><Save size={16} aria-hidden="true"/>저장·불러오기<ArrowRight size={15} aria-hidden="true"/></button></footer>
 </div>;
}
