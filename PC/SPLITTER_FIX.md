# SplitContainer 시작 오류 수정

수정 대상:
- `src/NaverRelay.Gui/MainForm.Designer.cs`
- `src/NaverRelay.Gui/MainForm.cs`

수정 내용:
- 디자이너 초기화 중 `SplitterDistance = 410` 제거
- 초기 `Panel1MinSize`, `Panel2MinSize`를 0으로 설정
- 폼이 표시되고 실제 크기가 확정된 뒤 `ApplySafeMainSplitterLayout()`에서 안전한 범위로 설정
- DPI 배율 또는 창 크기 변경 시 최소 크기와 SplitterDistance 자동 보정

이 수정으로 다음 예외를 방지합니다.

`System.InvalidOperationException: SplitterDistance는 Panel1MinSize와 너비 - Panel2MinSize 사이여야 합니다.`
