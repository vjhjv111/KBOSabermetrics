(() => {
  const trigger=text('button','기록 질문','button outline');trigger.type='button';
  document.querySelector('.header-actions').prepend(trigger);
  const dialog=document.createElement('dialog');dialog.className='record-questions';dialog.setAttribute('aria-labelledby','ask-title');
  dialog.innerHTML='<div class="ask-heading"><div><span class="ask-beta">FANZAI · 시험 기능</span><h2 id="ask-title">기록에 물어보세요</h2></div><button type="button" class="button outline ask-close" aria-label="기록 질문 닫기">닫기</button></div><p>연도와 궁금한 기록을 적어 주세요. 해석한 조건과 DB 조회 결과를 함께 보여드립니다.</p><div class="ask-examples"></div><form><label for="ask-question">질문</label><textarea id="ask-question" maxlength="600" rows="3" required placeholder="2026년 5월 홈런이 가장 많은 선수는?"></textarea><p class="ask-privacy">질문은 최대 600자입니다. 전체 입력 토큰 한도를 넘으면 조회가 거절됩니다. 질문은 OpenAI로 전달됩니다. 개인정보를 입력하지 마세요. DB 기록은 서버에서 계산하며 질문마다 독립적으로 처리합니다.</p><button class="button primary" type="submit">기록 조회</button></form><p class="ask-status" role="status" aria-live="polite"></p><section class="ask-result" aria-label="기록 질문 답변" aria-live="polite"></section>';
  document.body.append(dialog);
  const input=dialog.querySelector('textarea'),form=dialog.querySelector('form'),submit=form.querySelector('button'),status=dialog.querySelector('.ask-status'),result=dialog.querySelector('.ask-result');
  let busy=false,ready=false,controller=null;
  for(const example of ['2026년 5월 홈런 1위는?','2026년 삼성 안타 상위 5명','2026년 100이닝 이상 투수 평균자책점 상위 5명']){
    const b=text('button',example,'button outline');b.type='button';b.onclick=()=>{input.value=example;input.focus();};dialog.querySelector('.ask-examples').append(b);
  }
  trigger.onclick=async()=>{
    dialog.showModal();input.focus();submit.disabled=true;
    try {const s=await api('/api/ask/status');ready=s.enabled;status.textContent=s.message;}
    catch {ready=false;status.textContent='연결 상태를 확인하지 못했습니다. 창을 다시 열어 주세요.';}
    submit.disabled=!ready||busy;
  };
  dialog.querySelector('.ask-close').onclick=()=>dialog.close();
  dialog.addEventListener('close',()=>{controller?.abort();trigger.focus();});
  form.onsubmit=async event=>{
    event.preventDefault();if(busy||!ready||!input.value.trim())return;
    busy=true;submit.disabled=true;controller=new AbortController();result.replaceChildren();result.setAttribute('aria-busy','true');status.textContent='질문을 해석하고 기록을 조회하고 있습니다…';
    try {
      const d=await api('/api/ask',{question:input.value.trim()},controller.signal);
      result.append(text('p',d.answer,'ask-answer'));
      if(!d.clarification){
        result.append(text('p',d.applied,'ask-applied'),text('p',`DB 수집 기준일: ${d.asOf??'확인 불가'}`,'ask-asof'));
        if(d.rows?.length){
          const scroll=text('div','','ask-table-scroll'),table=document.createElement('table'),head=document.createElement('thead'),tr=document.createElement('tr');
          for(const c of d.columns)tr.append(text('th',c.label));head.append(tr);table.append(head);
          const body=document.createElement('tbody');for(const row of d.rows){const r=document.createElement('tr');for(const c of d.columns){const value=row.cells[c.key]??'—';r.append(text('td',c.key==='TeamCode'?String(value).split(',').map(x=>teamNames[x.trim()]??x).join(', '):value));}body.append(r);}table.append(body);scroll.append(table);result.append(scroll);
        }
        const notes=document.createElement('ul');for(const warning of d.warnings??[])notes.append(text('li',warning));result.append(notes);
      }
      status.textContent=d.clarification?'조건을 보충해 전체 질문을 다시 입력해 주세요.':'조회가 완료되었습니다.';
    } catch(e){status.textContent=e.name==='AbortError'?'조회가 취소되었습니다.':e.message;}
    finally{busy=false;submit.disabled=!ready;result.setAttribute('aria-busy','false');}
  };
})();
