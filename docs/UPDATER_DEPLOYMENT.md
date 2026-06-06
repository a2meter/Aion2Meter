# A2Meter Updater Deployment

이 문서는 설치형 업데이터 기준의 릴리즈 배포 절차를 정리한다.

## 배포 구조

A2Meter는 GitHub Release의 최신 태그를 기준으로 업데이트한다.

- 저장소: `a2meter/Aion2Meter`
- 릴리즈 태그 형식: `v1.2.2`
- 필수 릴리즈 자산:
  - `A2Meter.exe`
  - `A2Meter_Installer.exe`
  - `uninstall.exe`

실행 중인 `A2Meter.exe`는 시작 시 최신 GitHub Release를 확인한다. 새 버전이 있으면 `%APPDATA%\A2Meter\A2Meter_Installer.exe`를 다운로드하고, 사용자가 업데이트를 누르면 설치형 업데이터를 실행한다.

`A2Meter_Installer.exe`는 다음 작업을 수행한다.

- `%APPDATA%\A2Meter` 폴더 생성
- 최신 `A2Meter.exe` 다운로드
- 최신 `uninstall.exe` 다운로드
- 게임 데이터 `game_db.sqlite` 다운로드 및 hash 검증
- 직업 아이콘 다운로드
- 바탕화면과 시작 메뉴에 `A2Meter` 바로가기 생성
- 제어판 프로그램 추가/제거용 레지스트리 등록
- 설치된 `A2Meter.exe` 실행

## 1. 버전 올리기

`src/A2Meter/A2Meter.csproj`의 버전을 릴리즈 버전으로 맞춘다.

```xml
<Version>1.2.2</Version>
<AssemblyVersion>1.2.2.0</AssemblyVersion>
<FileVersion>1.2.2.0</FileVersion>
```

## 2. PacketEngine 갱신

패킷 파서나 네이티브 콜백을 수정한 릴리즈라면 먼저 `PacketEngine.dll`을 다시 빌드해서 앱에 복사한다.

```powershell
cd E:\A2Viewer\A2Meter
$env:OS = 'Windows_NT'

dotnet publish src\PacketEngine\PacketEngine.csproj -c Release -r win-x64 --self-contained true
Copy-Item src\PacketEngine\bin\Release\net8.0\win-x64\native\PacketEngine.dll src\A2Meter\Native\PacketEngine.dll -Force
```

패킷 파서 변경이 없는 릴리즈라면 이 단계는 생략할 수 있다.

## 3. 릴리즈 산출물 빌드

```powershell
cd E:\A2Viewer\A2Meter
$env:OS = 'Windows_NT'
.\publish.ps1
```

성공하면 버전별 폴더에 산출물이 생성된다.

```text
E:\A2Viewer\A2Meter\publish\1.2.2\
  A2Meter.exe
  A2Meter.pdb
  A2Meter_Installer.exe
  uninstall.exe
```

릴리즈에 업로드해야 하는 파일은 `A2Meter.exe`, `A2Meter_Installer.exe`, `uninstall.exe` 세 개다.

## 4. 게임 데이터 배포

게임 데이터가 바뀐 릴리즈라면 `a2meter.github.io`의 다음 파일을 먼저 갱신하고 배포한다.

- `Assets/Game/game_db.sqlite`
- `Assets/Game/game_db.json`
- `Assets/Game/version.json`

`version.json`의 `hash`는 `game_db.sqlite`의 SHA-256 값이어야 한다. 설치형 업데이터와 실행 중 데이터 갱신 로직이 이 값을 사용해서 다운로드 파일을 검증한다.

## 5. GitHub Release 생성

`a2meter/Aion2Meter`에 새 릴리즈를 만든다.

- Tag: `v1.2.2`
- Title: `A2Meter v1.2.2`
- Assets:
  - `publish\1.2.2\A2Meter.exe`
  - `publish\1.2.2\A2Meter_Installer.exe`
  - `publish\1.2.2\uninstall.exe`

릴리즈를 공개하면 기존 A2Meter는 다음 실행 시 최신 릴리즈를 감지한다. 사용자가 업데이트를 수락하면 AppData에 설치형 업데이터가 내려받아지고, 업데이터가 최신 본체와 제거기를 설치한다.

## 6. 배포 후 확인

새 릴리즈 공개 후 다음을 확인한다.

```powershell
Invoke-RestMethod https://api.github.com/repos/a2meter/Aion2Meter/releases/latest |
    Select-Object tag_name
```

`tag_name`이 `v1.2.2`로 나오면 업데이트 감지 대상이 맞다.

설치 확인은 깨끗한 환경 또는 `%APPDATA%\A2Meter`를 백업 후 제거한 환경에서 `A2Meter_Installer.exe`를 직접 실행해 확인한다.

확인할 항목:

- `%APPDATA%\A2Meter\A2Meter.exe` 생성
- `%APPDATA%\A2Meter\A2Meter_Installer.exe` 생성
- `%APPDATA%\A2Meter\uninstall.exe` 생성
- `%APPDATA%\A2Meter\Data\game_db.sqlite` 생성
- 바탕화면 `A2Meter.lnk` 생성
- 시작 메뉴 `A2Meter\A2Meter.lnk` 생성
- 제어판 프로그램 추가/제거에 `A2Meter` 표시
