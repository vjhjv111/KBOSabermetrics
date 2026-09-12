import { createRoot } from "react-dom/client";
import Home from "./page";
import "./globals.css";
import "./site-theme.css";

const container = document.getElementById("diamond-root");
if (!container) throw new Error("게임 화면을 표시할 위치를 찾지 못했습니다.");
createRoot(container).render(<Home />);
