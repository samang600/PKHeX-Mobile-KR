# PKHeX 모바일 도구 (KR) — PC용 PKHeX 플러그인

PKHeX 모바일(안드로이드·iOS)에만 있던 두 기능을 PC용 PKHeX에서 쓰는 플러그인입니다.
생성 엔진은 모바일 앱과 같은 코드(`Shared/PKHeXKR.Gen`)를 씁니다.

## 기능
- **텍스트로 빠른 생성**: `빠르모트 암컷 이로치 러브볼 가장작게` 같은 한국어 문장으로 합법 개체 생성
  - 한 마리 → PKHeX 편집기로, 여러 마리 → 현재 박스부터 빈 칸
  - `어버이: 이름 TID` 줄, `x3`, 괄호 여러 폼, 배포·내 사전 등 모바일과 같은 문법
- **리빙덱스 채우기**: 이로치·모든 폼·우두머리(PLA·Z-A)·만난 날짜 구간·어버이 여러 명
- **트레이너·내 사전 설정**: 다른 게임 출신 개체의 어버이·언어("ALM"·영어 대신), 줄임말 사전

## 어버이(트레이너) 규칙
- 세이브 파일을 열었으면: 그 세이브 트레이너 (단, 설정에서 이 게임을 따로 지정했으면 그 트레이너)
- PKHeX를 세이브 없이 켠 상태(빈 세이브 "PKHeX"·123456·영어): 게임별 지정 → 기본 트레이너
- 다른 게임 출신으로 만들어질 때: 게임별 지정 → 같은 세대 세이브 트레이너 → 기본 트레이너
- 1~3세대 게임은 한글 이름을 쓸 수 없음 → 게임별로 영문·일본어 이름 지정

## 변경 사항
- v1.0.1: 세이브 없이 켠 상태에서 설정 트레이너 대신 "PKHeX"·123456·영어로 만들어지던 문제 수정, 게임별 지정이 현재 게임에도 적용, 설정 파일을 PKHeX 폴더에 못 쓰면 %AppData%\PKHeXKR에 저장, 3세대 배포(고정 어버이) 조우 후순위
- v1.0.0: 첫 버전

## 설치
1. PKHeX **26.10.18** (윈도우) 준비
2. `PKHeXKR.Plugin.dll`을 PKHeX 폴더의 `plugins` 폴더에 넣기 (없으면 만들기)
3. PKHeX 실행 → 상단 **도구(Tools) → PKHeX 모바일 도구 (KR)**

- ALM(Auto-Legality Mod) 없이도 동작 (ALM 핵심이 DLL 안에 포함, ALM과 함께 깔려 있어도 서로 영향 없음)
- 설정은 PKHeX 실행 파일 옆 `pkhexkr_plugin.json`에 저장
- PKHeX가 업데이트되면 그 버전에 맞춰 다시 빌드해야 함

## 빌드
```
dotnet build PCPlugin/PKHeXKR.Plugin/PKHeXKR.Plugin.csproj -c Release -p:UsePkhexNuget=true
```
결과: `PCPlugin/PKHeXKR.Plugin/bin/Release/net10.0-windows/PKHeXKR.Plugin.dll` (리눅스·맥에서도 빌드 가능)

- 플러그인은 PC용 PKHeX 정식 버전(26.10.18)에 맞춰 정식 PKHeX.Core 패키지로 빌드됨 (`-p:UsePkhexNuget=true` 필수). 모바일 앱은 개발 버전(`PKHeX-Core-Dev`)을 씀
- 같은 폴더에서 모바일 앱을 빌드한 직후라면 `dotnet clean` 후 빌드 (공용 프로젝트의 중간 파일이 섞이지 않게)
