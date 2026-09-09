# 숫자 표시 형식 수정

DataGridView의 실제 값은 변경하지 않고 화면 표시만 야구 통계 관례에 맞게 적용했습니다.

- AVG / OBP / SLG / OPS / BABIP / ISO / wOBA / WPA: 소수 셋째 자리
- BB% / K% / Swing% / Contact% / Whiff% / CSW% 등 비율: 백분율 소수 첫째 자리
- FIP / xFIP / K/9 / BB/9 / HR/9 / BB/K / P/PA: 소수 둘째 자리
- 구속 / IP / wRAA / wRC: 소수 첫째 자리
- wRC+ / OPS+ / FIP- / xFIP-: 정수
- Plate X / Plate Z: 소수 셋째 자리
- null: `-`

CSV 내보내기는 원본 숫자 정밀도를 유지합니다.
