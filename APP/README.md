# FANZAI Android

기존 [FANZAI 웹사이트](https://kbosabermetrics.onrender.com/)를 Android WebView에서 여는 앱 프로젝트입니다. 기록실·선수 프로필·분석실·웹 야구게임은 연결한 서버가 제공하는 버전으로 표시됩니다. 이 저장소의 `WEB` 코드를 수정했다면 서버에도 배포해야 앱에서 변경 사항을 볼 수 있습니다.

APK에는 경기 SQLite DB나 선수 사진 묶음을 넣지 않습니다. 기록 조회·사진·게임 데이터는 서버에서 불러오므로 인터넷 연결이 필요합니다. WebView가 일부 리소스와 로그인 정보를 기기에 캐시할 수 있지만 오프라인 기록 조회나 게임 실행을 보장하지 않습니다.

## 프로젝트와 도구 버전

| 항목 | 값 |
|---|---|
| 프로젝트 폴더 / 모듈 | `APP` / `app` |
| 패키지 / applicationId | `kr.kbosabermetrics.app` |
| 최소 Android | Android 8.0, API 26 |
| compileSdk / targetSdk | 36 / 36 |
| Android Gradle Plugin | 8.11.1 |
| Gradle Wrapper | 8.13 |
| 빌드 JDK | 17 |
| SDK Build Tools | 35.0.0 |
| 기본 서버 | `https://kbosabermetrics.onrender.com/` |

AGP 8.11의 Gradle·JDK·SDK 호환성은 [Android 공식 릴리스 문서](https://developer.android.com/build/releases/agp-8-11-0-release-notes)에 맞췄습니다. Android SDK Platform 36과 Build Tools 35.0.0은 서로 다른 패키지입니다.

## Android Studio에서 열기

1. AGP 8.11을 지원하는 Android Studio를 설치하고 **Open**으로 이 저장소의 `APP` 폴더를 선택합니다.
2. SDK Manager에서 **Android SDK Platform 36**, **Android SDK Build-Tools 35.0.0**, **Android SDK Platform-Tools**를 설치하고 SDK 라이선스를 확인합니다.
3. Gradle 설정의 **Gradle JDK**를 JDK 17로 지정한 뒤 Gradle Sync를 실행합니다. Wrapper에 고정된 Gradle을 사용합니다.
4. Android 8.0 이상 기기 또는 에뮬레이터를 선택하고 `app`의 **Run**을 누릅니다.

SDK 경로는 Android Studio가 만드는 `local.properties`의 `sdk.dir` 또는 `ANDROID_HOME` 환경 변수로 설정합니다. `local.properties`는 컴퓨터마다 다르므로 커밋하지 않습니다.

## 명령줄에서 검증·APK 생성

아래 명령은 `APP` 폴더에서 실행합니다. 첫 실행에는 Gradle과 Android 빌드 의존성을 내려받기 위한 네트워크가 필요합니다.

Windows PowerShell:

```powershell
.\gradlew.bat --no-daemon testDebugUnitTest lintDebug assembleDebug
```

macOS / Linux:

```sh
chmod +x gradlew
./gradlew --no-daemon testDebugUnitTest lintDebug assembleDebug
```

결과물은 `APP/app/build/outputs/apk/debug/app-debug.apk`입니다. Debug APK는 개발용 키로 서명되어 테스트 기기에 설치할 수 있습니다. Gradle 명령과 APK 산출물 위치는 [공식 명령줄 빌드 안내](https://developer.android.com/build/building-cmdline)를 참고하세요.

USB 디버깅을 켠 기기를 연결하고 기기에 표시되는 PC 연결 허용을 확인한 뒤, `APP`에서 다음 명령을 실행합니다.

```sh
adb devices
adb install -r app/build/outputs/apk/debug/app-debug.apk
adb shell am start -n kr.kbosabermetrics.app/.MainActivity
```

단위 테스트 결과는 `app/build/reports/tests/testDebugUnitTest/index.html`, Lint 결과는 `app/build/reports/lint-results-debug.html`에서 확인합니다. 이 검증은 실제 기기의 WebView 렌더링·터치 동작을 실행하지 않으므로 아래 기기 확인도 함께 진행합니다.

## 연결 서버 변경

`siteBaseUrl` Gradle 속성으로 빌드할 때 서버를 지정합니다. 기본값은 위 운영 주소입니다.

```powershell
.\gradlew.bat assembleDebug -PsiteBaseUrl=https://kbosabermetrics.onrender.com/
```

계속 같은 별도 서버를 사용할 때는 개인 Gradle 사용자 설정 파일 `~/.gradle/gradle.properties`에 `siteBaseUrl=https://your-server.example/`을 지정할 수도 있습니다. 이 설정에는 공개 가능한 HTTPS 서버 주소만 넣습니다. 앱에 암호나 토큰을 포함하지 않습니다.

유효한 HTTPS 주소만 허용하며 값은 빌드 시 `BuildConfig.SITE_BASE_URL`에 반영됩니다. 서버 주소 변경 후에는 APK를 다시 빌드·설치해야 합니다. 로컬 HTTP 주소나 자체 서명 인증서를 사용하기 위해 보안 검사를 끄는 방법은 제공하지 않습니다. 개발 서버도 기기에서 접근할 수 있는 신뢰된 HTTPS 주소를 사용하세요.

## WebView 동작과 기기 확인

앱은 설정된 서버의 같은 출처를 WebView 안에서 탐색하고 외부 HTTP(S) 링크는 확인 후 브라우저로 엽니다. JavaScript와 DOM 저장소는 사이트 실행에 사용하며, 파일·콘텐츠 URI 접근과 HTTP 혼합 콘텐츠는 차단합니다. SSL 인증서 오류를 무시하지 않고 오류 화면에서 재시도할 수 있게 처리합니다.

CSV 등 파일 내보내기는 앱 안에서 저장하지 않습니다. 다운로드 안내 또는 **더 보기 → 브라우저에서 열기**로 현재 페이지를 연 뒤 브라우저에서 내보내기를 다시 실행하세요. 웹에서 만드는 `blob:` 파일도 이 방법을 사용합니다. 브라우저는 앱 WebView와 로그인 상태를 공유하지 않을 수 있으므로 다시 로그인이 필요할 수 있습니다.

뒤로가기는 웹 탐색 기록을 우선 사용합니다. Android 13 이상에서는 플랫폼 뒤로가기 콜백을 사용하고 이전 버전에서는 기존 뒤로가기 이벤트를 처리합니다. Android 15·16의 시스템 바와 화면 잘림 영역은 여백으로 반영합니다. targetSdk 36에서는 기존 `onBackPressed`에만 의존할 수 없다는 점은 [Android 16 동작 변경](https://developer.android.com/about/versions/16/behavior-changes-16)에 설명되어 있습니다.

화면 회전에는 기존 WebView를 유지합니다. Android가 앱 프로세스를 종료해 화면을 다시 만들 때는 마지막으로 확인한 서버 URL만 다시 엽니다. 이전 탐색 기록이나 웹 게임의 진행 중 상태까지 복원하지는 않습니다.

실제 기기에서는 다음을 확인합니다.

- 첫 실행, 로그인, 기록 조회, 선수 프로필, 분석실과 비교 화면.
- 웹 링크·분석 탭 이동 후 뒤로가기, 앱 첫 화면에서 뒤로가기, 회전 후 페이지 유지.
- 키보드를 연 검색·입력 화면, 제스처/3버튼 내비게이션, 노치와 가로 화면에서 버튼이 가려지지 않는지.
- 네트워크를 끊었다가 복구했을 때 오류 안내와 재시도.
- 야구게임의 로딩·스윙 터치·애니메이션·효과음, 전체화면 진입·뒤로가기로 복귀, 다른 앱으로 전환했다가 돌아온 상태.
- 외부 링크가 앱의 로그인 화면으로 오인될 여지 없이 브라우저로 열리는지.

하드웨어 가속을 사용하지만 게임의 WebGL·오디오·터치 동작은 기기 GPU와 설치된 Android System WebView/Chrome 버전에 영향을 받습니다. OS 최소 지원 버전만으로 모든 게임 기능을 보장하지 않습니다. 게임이 검게 보이거나 입력이 작동하지 않으면 WebView/Chrome을 업데이트하고 같은 주소를 브라우저에서 비교해 확인하세요. 앱이 웹 게임의 구현을 별도로 복제하지 않으므로 서버와 WebView 양쪽 상태를 확인해야 합니다.

WebView의 키보드·시스템 바 처리도 버전에 따라 다릅니다. 네이티브에서 적용한 여백이 웹에 중복 적용되지 않아야 한다는 내용은 [WebView window insets 공식 문서](https://developer.android.com/develop/ui/views/layout/webapps/understand-window-insets)를 참고하세요.

## GitHub Actions

저장소 루트의 `.github/workflows/android.yml`은 `APP/**` 또는 해당 workflow 변경의 push/PR 및 수동 실행에 반응합니다. Ubuntu에서 JDK 17, Platform 36, Build Tools 35.0.0을 설치하고 SDK 라이선스를 수락한 뒤 아래 작업을 실행합니다.

```sh
./gradlew --no-daemon --stacktrace testDebugUnitTest lintDebug assembleDebug
```

[`gradle/actions/setup-gradle`](https://github.com/gradle/actions/blob/v4.4.4/setup-gradle/action.yml)이 Gradle Wrapper JAR 검증을 함께 수행합니다. 성공한 작업의 **Artifacts → kbo-sabermetrics-debug-apk**에서 APK를 내려받을 수 있으며, 테스트·Lint 보고서도 별도로 보관합니다. 보관 기간은 7일입니다. 이 workflow는 실제 기기나 에뮬레이터에서 앱을 실행하지 않습니다.

GitHub Actions의 임시 실행 환경에서 만드는 debug 서명 키는 실행마다 달라질 수 있습니다. 다른 실행에서 받은 APK로 기존 앱 업데이트가 거부되면 같은 개발용 키로 다시 빌드하거나, 필요한 로그인·설정 상태를 확인한 뒤 기존 테스트 앱을 제거하고 설치하세요. 제거하면 앱의 로컬 상태가 지워집니다.

## Release 서명

서명 설정을 제공하지 않은 상태의 `assembleRelease` 결과는 `app/build/outputs/apk/release/app-release-unsigned.apk`이며 그대로 기기에 배포할 수 없습니다. 실제 배포 파일이 필요하면 Android Studio의 **Generate Signed Bundle / APK**에서 로컬 키 저장소를 선택해 서명합니다. 키 생성·보관·서명 절차는 [Android 앱 서명 문서](https://developer.android.com/studio/publish/app-signing)를 따릅니다.

키 저장소(`.jks`, `.keystore`), 비밀번호, 개인 서명 설정은 저장소에 커밋하지 않습니다. 업데이트에는 기존 앱과 일치하는 서명이 필요하므로 키를 안전한 별도 위치에 보관합니다. 현재 CI에는 release 서명 키와 Google Play 업로드 작업이 없습니다.
