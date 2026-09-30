// /팬자이: OpenAI 키가 아닌 서버 전용 공유 키를 파일로 보관합니다.
const FANZAI_BOT_KEY_FILE = "/sdcard/Pictures/fanzai-bot-key.txt";
function GetFanzaiAnswer(message) {
    var question = String(message).trim().replace(/^\/팬자이(?:\s+|$)/, "").trim();
    if (!question) return "사용법: /팬자이 2026년 한화 홈런 1위는?";
    if (question.length > 600) return "질문은 600자 이내로 입력해 주세요.";
    var key = readFile(FANZAI_BOT_KEY_FILE);
    if (!key || key.length < 32) return "팬자이 봇 인증 키 파일을 설정해 주세요.";
    var conn = null;
    try {
        conn = new java.net.URL(KBO_SITE_BASE_URL + "/api/bot/ask").openConnection();
        conn.setInstanceFollowRedirects(false);
        conn.setRequestMethod("POST");
        conn.setConnectTimeout(15000);
        conn.setReadTimeout(180000);
        conn.setDoOutput(true);
        conn.setRequestProperty("Content-Type", "application/json; charset=UTF-8");
        conn.setRequestProperty("Accept", "application/json");
        conn.setRequestProperty("X-Fanzai-Bot-Key", String(key).trim());
        var bytes = new javaLang.String(JSON.stringify({ question: question })).getBytes("UTF-8");
        var output = conn.getOutputStream();
        try { output.write(bytes); output.flush(); } finally { output.close(); }
        var status = conn.getResponseCode();
        var stream = status >= 200 && status < 300 ? conn.getInputStream() : conn.getErrorStream();
        if (!stream) return "기록 서버에서 응답을 받지 못했습니다.";
        var reader = new javaIo.BufferedReader(new javaIo.InputStreamReader(stream, "UTF-8"));
        var body = "", line;
        try { while ((line = reader.readLine()) != null) body += String(line); } finally { reader.close(); }
        var data;
        try { data = JSON.parse(body); } catch (parseError) { return "기록 서버 응답을 확인하지 못했습니다."; }
        if (status >= 200 && status < 300 && data.text) return String(data.text);
        return data.message ? String(data.message) : "기록 조회에 실패했습니다. 잠시 후 다시 시도해 주세요.";
    } catch (error) {
        // No automatic retry: a timed-out model request may already have incurred usage.
        return "기록 서버 연결이 끊겼거나 응답 시간이 초과되었습니다. 잠시 후 확인해 주세요.";
    } finally { if (conn != null) { try { conn.disconnect(); } catch (ignored) {} } }
}

// responseFix 시작 부분에 추가:
    if (/^\/팬자이(?:\s|$)/.test(String(msg).trim())) {
        new javaLang.Thread({run: function() {replier.reply(GetFanzaiAnswer(msg));}}).start();
        return;
    }