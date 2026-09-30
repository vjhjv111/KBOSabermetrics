팬자이 기록 질문 카톡봇

1. AIbotUDPToPC.js를 기존 봇에 교체하세요. 기존 파일을 먼저 백업하세요.
   원본 개인 설정이 포함될 수 있으므로 이 전체 파일/ZIP은 공개 저장소에 올리지 마세요.
   일부 코드만 옮기려면 Fanzai-command.js를 사용하세요.

2. 서버 환경변수:
   OPENAI_API_KEY = OpenAI 키 (서버에만 보관)
   FANZAI_BOT_API_KEY = 무작위 공유 키 32자 이상
   RecordQuestions__Enabled = true
   RecordQuestions__DailyLimit = 1000
   RecordQuestions__PerIpDailyLimit = 10
   RecordQuestions__MaxInputTokens = 3000

3. 서버의 FANZAI_BOT_API_KEY와 같은 값만 안드로이드/녹스의
   /sdcard/Pictures/fanzai-bot-key.txt 파일에 저장하세요. OpenAI 키를 넣으면 안 됩니다.
   봇 소스의 KBO_SITE_BASE_URL을 실제 배포된 서버 HTTPS 주소로 맞추세요.

4. /팬자이 2026년 한화 홈런 1위는?
   /팬자이 2026년 WPA가 가장 높은 플레이를 알려줘
   /팬자이 2026년 한화 9회 3점차 이내 ERA 순위 최소 0.1이닝

POST /api/bot/ask
X-Fanzai-Bot-Key: 공유 키
Content-Type: application/json
본문: {"question":"2026년 한화 홈런 1위는?"}
응답: {"text":"카톡 표시용 답변", "data":{조회 조건/행/기준일/주의사항}}

웹 질문 버튼 및 기존 /api/ask 생성 기능은 숨김/비활성화했습니다.
실패 시 자동 재시도하지 않습니다. 과금 중복 방지 목적입니다.
공유 키로 인증된 봇 질문은 IP별 일일 한도에서 제외됩니다. 전체 일일 1000회 한도와 순간 요청/동시 요청 제한은 유지합니다.

로컬: Start-RecordQuestions.ps1을 재실행하면 %TEMP%/Fanzai-RecordQuestions/fanzai-bot-key.txt에
테스트용 공유 키가 자동 생성됩니다(이미 있으면 재사용). 로컬은 전체 10000/IP 1000회입니다.
기본 로컬 서버는 PC의 127.0.0.1에만 바인딩하므로 다른 기기에서 직접 접근할 수 없습니다.
폰/녹스 실사용은 서버 배포 후 HTTPS 주소로 연결하세요.

실제 카톡/안드로이드 기기에서 전송 테스트는 수행하지 않았습니다.
