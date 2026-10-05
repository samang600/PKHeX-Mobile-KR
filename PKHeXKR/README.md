# PKHeX 모바일 (v1.4.4)

UI를 새로 설계한 안드로이드용 PKHeX. 이미지 에셋만 PKHeXMAUI에서 가져옴.

## 빌드
- .NET 10 SDK(공식 배포판) + `dotnet workload install maui-android`
- Android SDK(platform 36, build-tools 36.0.0), JDK 21
- 같은 상위 폴더에 ALM 소스: `git clone https://github.com/santacrab2/PKHeX-Plugins`
- `dotnet build PKHeXKR.csproj -f net10.0-android36.0 -c Release -p:AndroidPackageFormat=apk -p:RuntimeIdentifiers=android-arm64`

## 구성
- PKHeX.Core 26.8.26 (NuGet), PKHeX.Core.AutoMod / Injection (소스 빌드, 같은 버전 기준)
- `MainPage.cs` 단일 화면, `UI/` 박스·편집 섹션·시트, `AppState.cs` 전역 상태, `Theme.cs` 디자인 토큰

- HOME Live: 같은 상위 폴더에 `git clone https://github.com/Manu098vm/HOME-Live-Plugin HOMELive`

## 릴리스 서명
- 빌드 시 `-p:AndroidKeyStore=true -p:AndroidSigningKeyStore=<키파일> -p:AndroidSigningKeyAlias=pkhexkr -p:AndroidSigningStorePass=<비밀번호> -p:AndroidSigningKeyPass=<비밀번호>` 추가
- 같은 키로 서명해야 기존 설치 위에 업데이트됩니다

## 참고한 프로젝트
- SWSHSeedFinderPlugin (hexbyt3): 소드실드 야생 시드 계산 순서
- sv-research / RaidCalc (MewTracker): SV 레이드 시드의 포켓몬 선택 검사
