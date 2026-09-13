# KBO 공식 선수 사진·소개

개인 페이지는 기존 경기 기록과 별도로 `OfficialPlayerProfiles`에 저장한 KBO 공식 소개와 로컬 사진을 표시합니다. 프로필이 없는 기존 DB도 계속 사용할 수 있습니다. 사진 파일이 없거나 유효하지 않으면 기본 표시를 사용합니다.

## 수집

Python 3.10 이상에서 추가 패키지 없이 실행합니다. 기존 DB의 `Players.Pcode` 중 숫자로 된 KBO 선수 ID가 대상입니다. 이름만으로 만들어진 임시 ID는 다른 선수와 잘못 연결하지 않도록 건너뜁니다.

```powershell
python WEB/tools/sync_player_profiles.py --db "D:/KBO/season.db"
```

사진은 기본적으로 DB 옆 `player-photos` 폴더에 저장됩니다. JSON 경기 수집과 별도의 관리 명령이며 웹 요청에서는 KBO 사이트에 접속하지 않습니다. 새 PC에서 사용하려면 DB와 사진 폴더를 함께 옮기세요.

```text
KBO/
  season.db
  player-photos/
    62404.jpg
  player-profile-sync-report.json
```

최근 7일 이내 성공한 선수는 건너뜁니다. 기본 요청 간격은 0.5초이며 HTML과 이미지 각각에 적용됩니다. 연속 오류는 제한적으로 재시도합니다. 사진 실패 시 기존 사진을 유지하고, 페이지 실패·선수 ID 불일치·HTML 구조 변경 시 기존 프로필을 유지합니다. 처리 결과와 보류 사유는 보고서에 남습니다. 정상 응답에 항목이 있으나 내용이 비어 있으면 미제공 값으로 저장합니다.

```powershell
# 한 선수 먼저 확인
python WEB/tools/sync_player_profiles.py --db "D:/KBO/season.db" --player 62404
# 지정 선수 강제 갱신 (여러 --player 사용 가능)
python WEB/tools/sync_player_profiles.py --db "D:/KBO/season.db" --player 62404 --force
# 사진 폴더·보고서 위치 변경
python WEB/tools/sync_player_profiles.py --db "D:/KBO/season.db" --photos-dir "D:/KBO/photos" --report "D:/KBO/profile-report.json"
```

실제 운영 DB를 처음 갱신할 때는 기존 백업을 확보하고 경기 가져오기 작업과 시간을 분리하세요. 수집기는 네트워크 작업 후 선수 한 명씩 짧은 트랜잭션으로 저장하며 경기·타석·투구·WAR 테이블을 변경하지 않습니다.

## 저장하는 정보

`OfficialPlayerProfiles`는 `Pcode`가 기본 키인 별도 테이블입니다.

| 정보 | 컬럼 |
|---|---|
| 선수명·현재 소개의 팀명·등번호 | `Name`, `TeamName`, `UniformNumber` |
| 생년월일·포지션·투타·신체 | `BirthDate`, `Position`, `BatsThrows`, `HeightCm`, `WeightKg` |
| 경력·입단 계약금·연봉·지명·입단년도 | `Career`, `SigningBonusText`, `SalaryText`, `DraftText`, `EntryYearText` |
| 출처·사진 원본·로컬 파일명 | `SourceUrl`, `PhotoSourceUrl`, `PhotoFileName` |
| 수집·확인 시각(UTC) | `FetchedUtc`, `LastCheckedUtc` |
| 명확히 확인된 프로필 기준 시즌 | `ProfileSeason` (현재 수집기는 NULL) |

계약금·연봉·입단 정보는 단위와 표기를 보존한 원문입니다. 빈 값을 0원으로 바꾸지 않습니다. KBO의 성적 제목이나 사진 URL의 연도만으로 연봉 기준 연도를 추정하지 않습니다. 현재 프로필은 과거 시즌의 소속·사진·연봉 이력이 아니며, 개인 페이지에서 이 점과 수집일을 표시합니다. 경기 DB의 기존 선수 기본정보는 덮어쓰지 않습니다.

## 웹/Render 배포

1. 새 웹 소스를 빌드·배포합니다. `player-profile.css`는 .NET 빌드에서 함께 복사됩니다.
2. 프로필이 들어간 SQLite DB와 `player-photos` 폴더를 기존 DB 배포 경로의 같은 영구 디스크에 배치합니다. 소스 코드나 Git에 선수 사진을 넣을 필요는 없습니다.
3. 기본 구조를 유지하면 추가 설정은 없습니다. 사진 폴더가 다르면 `Site__PlayerPhotoDirectory=/var/data/player-photos` 같은 환경 변수를 지정합니다. 실제 영구 디스크 마운트 경로에 맞추세요.
4. 구자욱 등 개인 페이지에서 사진과 프로필 수집일을 확인합니다. 웹 앱은 DB와 사진을 읽기 전용으로 사용합니다.

사진은 `/api/player-photo/{code}`로 제공하며 허용된 선수 ID 파일명과 JPEG/PNG 형식을 검증합니다. API 프로필 응답에 실제 사용 가능한 로컬 사진이 있을 때만 `photoUrl`이 포함됩니다. DB만 옮겨도 통계와 소개는 표시되지만 사진도 보려면 사진 폴더를 함께 옮겨야 합니다.

## 출처·검증

[KBO 구자욱 공식 기록실](https://www.koreabaseball.com/Record/Player/HitterDetail/Basic.aspx?playerId=62404)의 공개 소개와 그 페이지에 실제 연결된 사진 URL을 사용합니다. 사진·선수 정보의 출처는 KBO이며, 각 개인 페이지에 원문 링크를 표시합니다. 별도 로그인이나 접근 제한을 우회하지 않습니다.

```powershell
python -m unittest discover -s WEB/tools -p "test_sync_player_profiles.py" -v
```

파서 항목·빈 값·선수 ID 불일치·HTML 일부 변경·허용 사진 URL·이미지 형식·통신 실패 시 기존 자료 보존을 검사합니다.
