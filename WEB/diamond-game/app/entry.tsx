import { createRoot } from "react-dom/client";
import { Component, type ReactNode } from "react";
import Home from "./season-page";
import "./globals.css";
import "./site-theme.css";
import "./integration-theme.css";

const container = document.getElementById("diamond-root");
if (!container) throw new Error("게임 화면을 표시할 위치를 찾지 못했습니다.");

class GameRecovery extends Component<{children:ReactNode},{failed:boolean}> {
  state={failed:false};
  static getDerivedStateFromError(){return {failed:true};}
  componentDidCatch(error:Error){console.error("DIAMOND 화면을 복구해야 합니다.",error);}
  render(){
    if(!this.state.failed)return this.props.children;
    return <main className="season-app"><div className="season-content"><section className="season-panel" role="alert">
      <h1>경기 화면을 다시 열어 주세요</h1>
      <p>저장된 경기와 선수 기록을 다시 불러올 수 있습니다.</p>
      <button className="season-primary" onClick={()=>window.location.reload()}>저장된 경기 다시 열기</button>
    </section></div></main>;
  }
}
createRoot(container).render(<GameRecovery><Home /></GameRecovery>);
