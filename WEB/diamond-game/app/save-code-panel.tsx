import {useEffect,useLayoutEffect,useRef,useState,type FormEvent} from 'react';
import {Check,Copy,Download,KeyRound,RefreshCw,Save,X} from 'lucide-react';
import type {SaveCodeInfo} from '../lib/season-client';
import {fitDialogToParentViewport} from '../lib/dialog-viewport';
import './save-code.css';

export type SaveCodePanelProps={
 getSaveCode:()=>Promise<SaveCodeInfo>;
 saveCode:()=>Promise<SaveCodeInfo>;
 loadSaveCode:(code:string)=>Promise<void>;
 onClose:()=>void;
 busy:boolean;
 hasData:boolean;
};

export async function copySaveCodeToClipboard(code:string){
 let timer:ReturnType<typeof setTimeout>|undefined;
 try{
  if(!navigator.clipboard?.writeText)throw new Error('Clipboard unavailable');
  await Promise.race([navigator.clipboard.writeText(code),new Promise<never>((_,reject)=>{timer=setTimeout(()=>reject(new Error('Clipboard timeout')),1500);})]);
 }finally{if(timer!==undefined)clearTimeout(timer);}
}

export function SaveCodeNotice({code,onOpen,onDismiss}:{code:string|null;onOpen:()=>void;onDismiss:()=>void}){
 const [copied,setCopied]=useState(false);
 const copy=async()=>{try{await copySaveCodeToClipboard(code!);setCopied(true);}catch{onOpen();}};
 return <section className="save-code-notice" aria-label="저장 코드 안내">
  <div className="save-code-notice-copy"><Save size={19} aria-hidden="true"/><div><strong>{code?'저장되었습니다. 이 코드를 보관하세요.':'진행이 저장되었습니다. 저장 코드를 받아 두세요.'}</strong>{code?<code aria-label="새로 발급된 저장 코드">{code}</code>:<p>다른 기기에서도 리그와 내 선수를 이어갈 수 있습니다.</p>}</div></div>
  <div className="save-code-notice-actions">{code&&<button type="button" aria-label={copied?'저장 코드 복사됨':'저장 코드 복사'} onClick={()=>void copy()}>{copied?<Check size={15}/>:<Copy size={15}/>}<span role="status">{copied?'복사됨':'코드 복사'}</span></button>}<button type="button" onClick={onOpen}>저장 코드 보기</button><button className="save-code-icon" type="button" aria-label="저장 코드 안내 닫기" onClick={onDismiss}><X size={17}/></button></div>
 </section>;
}

export default function SaveCodePanel({getSaveCode,saveCode,loadSaveCode,onClose,busy,hasData}:SaveCodePanelProps){
 const dialog=useRef<HTMLDialogElement>(null),codeField=useRef<HTMLTextAreaElement>(null),alive=useRef(true),operation=useRef<'save'|'load'|null>(null);
 const [info,setInfo]=useState<SaveCodeInfo|null>(null),[checking,setChecking]=useState(true),[pending,setPending]=useState<'save'|'load'|null>(null);
 const [loadCode,setLoadCode]=useState(''),[error,setError]=useState(''),[message,setMessage]=useState(''),[copied,setCopied]=useState(false);
 const blocked=busy||pending!==null,canSave=info?.hasData??hasData,code=info?.code??null;
 useLayoutEffect(()=>{
  const element=dialog.current;if(!element)return;
  const releaseViewport=fitDialogToParentViewport(element);
  element.showModal();
  // Opening a modal ends any held game input before its key-up is intercepted.
  window.dispatchEvent(new Event('blur'));
  return()=>{if(element.open)element.close();releaseViewport();};
 },[]);
 useEffect(()=>{
  alive.current=true;let cancelled=false;
  void getSaveCode().then(next=>{if(!cancelled)setInfo(next);}).catch(e=>{if(!cancelled)setError(e instanceof Error?e.message:'저장 코드 정보를 확인하지 못했습니다. 아래에서 코드를 불러올 수 있습니다.');}).finally(()=>{if(!cancelled)setChecking(false);});
  return()=>{cancelled=true;alive.current=false;};
 },[getSaveCode]);
 const issue=async()=>{
  if(blocked||checking||!canSave||operation.current)return;operation.current='save';setPending('save');setError('');setMessage('');
  try{const next=await saveCode();if(!alive.current)return;setInfo(next);setCopied(false);if(!next.code)throw new Error('저장 코드를 발급하지 못했습니다. 다시 시도해 주세요.');setMessage('저장 코드가 준비되었습니다. 이후 자동 저장에도 같은 코드를 사용합니다.');}
  catch(e){if(alive.current)setError(e instanceof Error?e.message:'저장 코드를 발급하지 못했습니다.');}
  finally{operation.current=null;if(alive.current)setPending(null);}
 };
 const copy=async()=>{
  if(!code)return;
  try{await copySaveCodeToClipboard(code);if(alive.current){setCopied(true);setMessage('저장 코드를 복사했습니다. 개인적으로 보관하세요.');}}
  catch{codeField.current?.focus();codeField.current?.select();setError('코드를 선택했습니다. 복사 메뉴나 Ctrl/Cmd+C로 직접 복사해 주세요.');}
 };
 const restore=async(event:FormEvent)=>{
  event.preventDefault();const value=loadCode.trim().toUpperCase();if(blocked||!value||operation.current)return;
  operation.current='load';setPending('load');setError('');setMessage('저장 코드를 확인하고 있습니다.');
  try{await loadSaveCode(value);if(alive.current)setMessage('저장된 프로필을 불러왔습니다. 게임을 다시 열고 있습니다.');}
  catch(e){if(alive.current){setMessage('');setError(e instanceof Error?e.message:'코드를 불러오지 못했습니다. 코드를 확인해 주세요.');}}
  finally{operation.current=null;if(alive.current)setPending(null);}
 };
 const close=()=>{if(!operation.current)onClose();};
 const savedDate=info?.updatedAt==null?null:new Date(info.updatedAt);
 const updated=savedDate&&Number.isFinite(savedDate.getTime())?new Intl.DateTimeFormat('ko-KR',{month:'long',day:'numeric',hour:'2-digit',minute:'2-digit'}).format(savedDate):null;
 return <dialog ref={dialog} className="save-code-dialog" aria-labelledby="save-code-title" aria-describedby="save-code-summary" onCancel={event=>{event.preventDefault();close();}} onKeyDownCapture={event=>event.stopPropagation()} onKeyUpCapture={event=>event.stopPropagation()}>
  <header className="save-code-heading"><div><span>KEEP YOUR SEASON</span><h2 id="save-code-title">저장·불러오기</h2></div><button className="save-code-icon" type="button" disabled={pending!==null} aria-label="저장 창 닫기" onClick={close} autoFocus><X size={22}/></button></header>
  <div className="save-code-body">
   <p id="save-code-summary">리그, 진행 중인 리그 경기와 커스텀 선수를 코드 하나로 이어갑니다.</p>
   <section className="save-code-section" aria-labelledby="save-code-current-title" aria-busy={checking||pending==='save'}>
    <h3 id="save-code-current-title"><KeyRound size={18} aria-hidden="true"/>현재 프로필 저장 코드</h3>
    {checking?<p className="save-code-muted" role="status">저장 정보를 확인하고 있습니다…</p>:code?<>
     <label className="save-code-label" htmlFor="current-save-code">선택해서 복사하거나 아래 복사 버튼을 누르세요.</label>
     <textarea ref={codeField} id="current-save-code" className="save-code-value" value={code} readOnly rows={2} spellCheck={false} onFocus={event=>event.currentTarget.select()}/>
     <div className="save-code-code-actions"><button type="button" className="save-code-primary" onClick={()=>void copy()}>{copied?<Check size={17}/>:<Copy size={17}/>} {copied?'복사됨':'코드 복사'}</button>{updated&&<span>최근 저장 {updated}</span>}</div>
    </>:<p className="save-code-muted">{canSave?'코드를 발급받아 다른 기기에서도 현재 프로필을 이어가세요.':'아직 저장할 리그나 선수가 없습니다. 아래에서 기존 코드는 불러올 수 있습니다.'}</p>}
    {!code&&<button type="button" className="save-code-primary" disabled={blocked||checking||!canSave} onClick={()=>void issue()}>{pending==='save'?<RefreshCw className="save-code-spin" size={17}/>:<Save size={17}/>} {pending==='save'?'코드 발급 중…':'저장 코드 발급'}</button>}
    <p className="save-code-muted">한 번 발급한 코드는 이후 자동 저장에도 계속 유효합니다. 이 코드로 같은 서버·사이트에서 불러올 수 있습니다.</p>
   </section>
   <form className="save-code-section" onSubmit={event=>void restore(event)} aria-labelledby="save-code-load-title" aria-busy={pending==='load'}>
    <h3 id="save-code-load-title"><Download size={18} aria-hidden="true"/>코드로 불러오기</h3>
    {canSave&&<p className="save-code-switch-note">{code?'현재 프로필을 다시 열려면 먼저 위의 코드를 보관하세요.':'현재 프로필을 다시 열려면 먼저 위에서 코드를 발급받아 보관하세요.'}</p>}
    <label className="save-code-label" htmlFor="load-save-code">불러올 저장 코드</label>
    <textarea id="load-save-code" className="save-code-value" value={loadCode} onChange={event=>setLoadCode(event.target.value.toUpperCase())} rows={2} maxLength={128} autoComplete="off" autoCapitalize="characters" spellCheck={false} placeholder="보관한 저장 코드를 붙여 넣으세요" disabled={pending==='load'} required/>
    <p className="save-code-muted">불러오면 이 브라우저의 활성 프로필이 전환됩니다. 이전 프로필의 저장 데이터는 삭제되지 않습니다.</p>
    <button type="submit" className="save-code-primary" disabled={blocked||!loadCode.trim()}>{pending==='load'?<RefreshCw className="save-code-spin" size={17}/>:<Download size={17}/>} {pending==='load'?'불러오는 중…':'저장 코드 불러오기'}</button>
   </form>
   {error&&<p className="save-code-feedback save-code-error" role="alert">{error}</p>}
   <p className={'save-code-feedback'+(message?' save-code-success':'')} role="status" aria-live="polite">{message}</p>
   <footer className="save-code-footnotes"><p>코드를 아는 사람은 저장 데이터에 접근할 수 있습니다. 개인적으로 보관하세요.</p><p>친선 경기와 6타석 대결방은 각 방의 별도 초대 코드로 접속합니다.</p></footer>
  </div>
 </dialog>;
}
