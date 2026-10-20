# PKHeX 모바일 (한국어)

[PKHeX](https://github.com/kwsch/PKHeX)를 바탕으로 만든 **안드로이드·iOS용 포켓몬 세이브 편집기**입니다.
한국어 인터페이스, 자동 합법화(ALM), 라이브헥스, HOME Live, 인카운터·시드 검색, 텍스트로 빠른 생성 등을 지원합니다.

> 비공식 개인 제작 앱입니다. 세이브는 반드시 백업한 뒤 사용하세요.

## 다운로드
[Releases](../../releases) 페이지에서 받습니다.

| 기기 | 파일 | 설치 |
|---|---|---|
| 안드로이드 (7.0 이상, 64비트 ARM) | `PKHeX_Mobile_v*.apk` | 파일을 열어 설치 (출처를 알 수 없는 앱 설치 허용 필요) |
| iPhone·iPad (iOS 15 이상) | `PKHeX_Mobile_iOS_v*_unsigned.ipa` | AltStore·SideStore·Sideloadly로 본인 애플 계정에 서명해 설치 (무료 계정은 7일마다 재서명) |

## 주요 기능
- 1세대~레전즈 Z-A 세이브·포켓몬 파일 편집 (한국어·일본어·영어 데이터)
- 자동 합법화(ALM), 합법성 보고서 (한국어 교정)
- 인카운터 검색: 조건(성별·이로치·성격·특성·개체값·크기·증표) 탐색, 시드 순차·무작위 탐색
- 텍스트로 빠른 생성: "빠르모트 암컷 이로치 러브볼 가장작게"처럼 적으면 합법 개체 생성
- 라이브헥스(스위치 sys-botbase), HOME Live, 자동 어버이작
- 스위치 세이브 무선 FTP·HTTP (DBI 등)
- PokeLDN 교환 내장 (ESP32 보드, 안드로이드 8 이상): 리빙덱스 교환(커펌 없이 콘솔 세이브 채우기), SV 자동 어버이작
- 레이드 편집 (SV 테라 레이드·소드실드 맥스 레이드 굴): 보상·이벤트 레이드 미리보기, 조건으로 시드 찾기
- 배포 데이터 자동 업데이트 (PKHeX 최신 배포 DB에서 새 카드만 받아 합법성 검사에 반영)
- 박스 검색·정렬·여러 칸 선택·즐겨찾기·리빙덱스 채우기

## 빌드
- 안드로이드: .NET 10 SDK + MAUI 안드로이드 워크로드
  `dotnet build PKHeXKR/PKHeXKR.csproj -f net10.0-android36.0 -c Release`
  기본은 PokeLDN 교환 내장판 (`PKHeXKR/Platforms/Android/ldn/pokeldn-trade-kr.aar` 필요, 소스는 PokeLDN 교환 앱 저장소의 `embed`).
  내장하지 않은 일반판: 끝에 `-p:WithLdn=false` (판을 바꿀 때는 `obj/Release`·`bin/Release`를 지우고 빌드)
  (구버전과 함께 설치하는 별도 설치판: 끝에 `-p:SideBySide=true`)
- iOS: macOS + Xcode 필요. 저장소의 **Actions → iOS 빌드**로 서명 없는 IPA를 만들 수 있습니다.

폴더 구성: `PKHeXKR`(앱), `PKHeX-Core-Dev`(PKHeX.Core 개발 버전 소스, kwsch/PKHeX master 2026-10-19 — 정식 릴리스 이후 수정 반영), `Shared/PKHeXKR.Gen`(텍스트 생성·리빙덱스 엔진, 앱과 PC 플러그인 공용), `PCPlugin`(PC용 PKHeX 플러그인), `PKHeX-Plugins`(ALM·라이브헥스 라이브러리), `HOMELive`(HOME Live 라이브러리)

## PC용 PKHeX 플러그인
텍스트로 빠른 생성·리빙덱스 채우기를 PC용 PKHeX에서 쓰는 플러그인. 설치·빌드는 [PCPlugin/README.md](PCPlugin/README.md) 참고.

## 라이선스
GNU GPL v3 ([LICENSE](LICENSE)). PKHeX와 플러그인들의 라이선스를 따릅니다.
포켓몬 관련 이미지·명칭의 권리는 Nintendo·Creatures·GAME FREAK·The Pokémon Company에 있습니다.

## 크레딧
- [PKHeX](https://github.com/kwsch/PKHeX) — kwsch 외
- [PKHeX-Plugins (AutoLegalityMod, Injection)](https://github.com/santacrab2/PKHeX-Plugins) — santacrab2, architdate, kwsch 외
- [HOME-Live-Plugin](https://github.com/Manu098vm/HOME-Live-Plugin) — Manu098vm
- [PKHeXMAUI](https://github.com/santacrab2/PKHeXMAUI) — 스프라이트 에셋 (santacrab2)
- [SysBot.NET](https://github.com/kwsch/SysBot.NET) — 교환 상대 읽기 오프셋
- [SWSHSeedFinderPlugin](https://github.com/hexbyt3/SWSHSeedFinderPlugin) — 소드실드 야생 시드 계산 순서 (hexbyt3)
- [sv-research / RaidCalc](https://github.com/MewTracker/sv-research) — SV 레이드 시드 선택 검사 (MewTracker)


## 서명
- 기존 디버그 키로 서명 (키 백업: PKHeX_Mobile_debug_key.zip, SHA-256 87bc40c7…). 새 키는 보안 검사에서 똑같이 차단돼 쓰지 않음.
- 앱 업데이트 알림: GitHub 릴리스(태그 v1.5.1 형식)에 `.apk` 파일을 그대로 첨부하면 "새 버전 APK 받기"로 브라우저에서 받음 (이름에 side가 들어간 파일은 제외).
