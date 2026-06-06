# A2Meter

**아이온2(Aion 2) DPS 미터 오버레이**

A2Meter는 아이온2 게임 클라이언트가 서버와 주고받는 TCP 패킷을 NIC 단에서 스니핑하여 실시간으로 DPS(초당 데미지)를 계산해 화면 상단에 오버레이로 표시하는 도구입니다. 게임 프로세스에 일절 인젝션하지 않으며, 오로지 네트워크 패킷만 관찰합니다.

본 솔루션은 원본 A2Viewer/A2Power 프로젝트의 핵심 프로토콜 스택을 C#으로 포팅하고, 오버레이 UI를 WebView2 기반에서 **순수 네이티브(WinForms + Direct2D)** 로 재작성한 결과물입니다.

---

## 솔루션 구성

본 저장소는 다음 세 개의 .NET 8 프로젝트로 구성됩니다.

| 프로젝트 | 종류 | 역할 |
|---|---|---|
| [src/A2Meter/](src/A2Meter/) | WinExe (`A2Meter.exe`) | 실시간 DPS 오버레이 본체. Direct2D로 렌더링, Npcap으로 캡처. |
| [src/A2Capture/](src/A2Capture/) | Console (`a2cap.exe`) | 회귀 테스트용 패킷 캡처 도구. `.pcapng` + 매니페스트로 세션 저장. |
| [src/A2Inspect/](src/A2Inspect/) | Console (`a2inspect.exe`) | 캡처된 세션을 오프라인에서 파서에 통과시켜 진단/검증. |

세 프로젝트 모두 `x64` 플랫폼, `net8.0` 타깃입니다 (메인 앱만 `net8.0-windows`).
프로토콜 코드는 [src/A2Meter/Dps/Protocol/](src/A2Meter/Dps/Protocol/)에 한 벌만 존재하며, A2Inspect는 `<Compile Include="..\A2Meter\..." Link="..." />` 방식으로 동일한 소스를 빌드 시점에 공유합니다.

---

## 데이터 흐름

```
 [아이온2 클라이언트]
        │
        │  TCP (서버 ↔ 클라이언트, 기본 :13328)
        ▼
 ┌─────────────────────────────────────────────────────┐
 │            Npcap / WinPcap (커널 레벨)             │
 └─────────────────────────────────────────────────────┘
        │
        │  raw segments
        ▼
 ┌──────────────┐                ┌─────────────────┐
 │ PacketSniffer │  또는  ⇢  ⇢  │ PcapReplaySource │   (--replay 모드)
 │  (다중 NIC    │                │  (a2cap 세션 재생)│
 │   자동 락온)  │                └─────────────────┘
 └──────────────┘
        │
        ▼
   TcpReassembler  →  StreamProcessor  →  PacketDispatcher
   (시퀀스 정렬)      (varint 프레임/LZ4)    (태그 기반 디스패치)
        │
        │  Damage / MobSpawn / UserInfo / Buff / BossHp ...
        ▼
   DpsMeter  ──  PartyTracker  ──  CombatHistory
        │
        ▼
   DpsPipeline (10Hz 푸시)
        │
        ▼
   DpsCanvas (Direct2D)  +  OverlayHeaderPanel (WinForms)
```

---

## 빠른 시작

### 1. 사전 요구사항

- **Windows 10/11 x64**
- **[Npcap](https://npcap.com)** (설치 시 *"Install Npcap in WinPcap API-compatible Mode"* 체크 권장)
- **.NET 8 SDK** (빌드 시) — 단일 파일 self-contained 게시본은 .NET 런타임 불필요
- Visual Studio 2022 17.8 이상 또는 `dotnet` CLI

### 2. 빌드

```powershell
# 솔루션 전체 빌드
dotnet build A2Meter.slnx -c Release

# 단일 파일 게시 (메인 앱)
dotnet publish src/A2Meter/A2Meter.csproj -c Release -r win-x64
```

게시 결과물은 `bin/Release/net8.0-windows/win-x64/publish/A2Meter.exe` 단일 파일이며,
[src/A2Meter/A2Meter.csproj](src/A2Meter/A2Meter.csproj)의 다음 설정으로 자체 추출됩니다.

```xml
<PublishSingleFile>true</PublishSingleFile>
<SelfContained>true</SelfContained>
<EnableCompressionInSingleFile>true</EnableCompressionInSingleFile>
```

### 3. 실행

```powershell
A2Meter.exe                              # 라이브 캡처 모드 (Npcap 필요)
A2Meter.exe --replay <세션디렉토리>      # 오프라인 재생 (실시간 페이스)
A2Meter.exe --replay <디렉토리> --speed 4  # 4배속 재생
A2Meter.exe --replay <디렉토리> --fast    # 최대 속도 재생
A2Meter.exe --demo                        # 더미 데이터 미리보기
```

오버레이는 기본적으로 **단일 인스턴스 뮤텍스**로 보호되며 (`A2Meter.SingleInstance.Mutex`),
`--replay` 모드는 이 제한이 풀려 라이브 인스턴스와 함께 띄울 수 있습니다.

---

## 주요 기능

| 기능 | 위치 |
|---|---|
| 다중 NIC 자동 락온(첫 게임 패킷 수신 어댑터 선택) | [PacketSniffer.cs](src/A2Meter/Dps/PacketSniffer.cs) |
| LZ4 디컴프레션 + varint 프레임 디코딩 | [Protocol/StreamProcessor.cs](src/A2Meter/Dps/Protocol/StreamProcessor.cs) |
| 태그 기반 패킷 디스패치 (Damage/MobSpawn/UserInfo 등) | [Protocol/PacketDispatcher.cs](src/A2Meter/Dps/Protocol/PacketDispatcher.cs) |
| 보스 단위 세션 분리 (원본 A2Power의 "1풀 = 1세션" 의미론) | [DpsPipeline.cs](src/A2Meter/Dps/DpsPipeline.cs) |
| 파티 멤버 추적 / 본인 식별 | [PartyTracker.cs](src/A2Meter/Dps/PartyTracker.cs), [Protocol/PartyStreamParser.cs](src/A2Meter/Dps/Protocol/PartyStreamParser.cs) |
| 글로벌 핫키 (RegisterHotKey) | [Core/HotkeyManager.cs](src/A2Meter/Core/HotkeyManager.cs) |
| 시스템 트레이 메뉴 | [Core/TrayManager.cs](src/A2Meter/Core/TrayManager.cs) |
| 클릭 통과(Lock) / 토픽모스트 / 레이어드 윈도우 | [Forms/OverlayForm.cs](src/A2Meter/Forms/OverlayForm.cs) |
| 전투 이력 보존/조회 | [Dps/CombatHistory.cs](src/A2Meter/Dps/CombatHistory.cs), [Forms/CombatHistoryForm.cs](src/A2Meter/Forms/CombatHistoryForm.cs) |
| 네이티브 PacketEngine.dll 폴백 (선택사항) | [Protocol/NativePacketEngine.cs](src/A2Meter/Dps/Protocol/NativePacketEngine.cs) |

---

## 회귀 테스트 워크플로

라이브 게임 없이 파서 변경을 검증하려면 다음 흐름을 사용합니다.

```powershell
# 1) 인게임에서 한 번만 캡처
a2cap.exe                                 # Ctrl-C로 중단
# → ./captures/<세션ID>/capture-*.pcapng + manifest.json

# 2) 캡처 진단 (포트/플로우/이벤트 카운트 검증)
a2inspect.exe ./captures/<세션ID>

# 3) 동일 세션을 오버레이에서 재생
A2Meter.exe --replay ./captures/<세션ID> --fast
```

자세한 사용법은 각 프로젝트의 README를 참조하세요.

- [src/A2Meter/README.md](src/A2Meter/README.md)
- [src/A2Capture/README.md](src/A2Capture/README.md)
- [src/A2Inspect/README.md](src/A2Inspect/README.md)

---

## 설정 파일 위치

`%APPDATA%\A2Meter\` 아래에 다음 파일들이 자동 생성됩니다.

| 파일 | 내용 |
|---|---|
| `app_settings.json` | 투명도, 테마, 단축키, 패널 크기, 동의 버전 등 |
| `window_state.json` | 오버레이 창의 좌표/크기 (해상도/모니터 변경 시 자동 보정) |
| `gamedata\meter-bootstrap.json` | A2Web 장애 시에도 DPS 인식에 쓰는 opcode/스킬/던전 bootstrap 캐시 |
| `*.bak` | 원자적 쓰기 실패 시 복구용 백업본 |

쓰기는 [AppSettings.SaveDebounced](src/A2Meter/Core/AppSettings.cs)로 약 400ms 디바운스되며,
크래시 안전을 위해 `tmp → File.Replace → bak` 순서로 원자적 교체됩니다.

---

## 라이선스 / 책임

본 도구는 게임 클라이언트나 서버에 어떠한 변조도 가하지 않으며,
오로지 사용자 PC의 NIC를 통과하는 패킷만을 관찰합니다.
사용으로 인한 약관 위반/계정 제재 등 모든 책임은 사용자에게 있습니다.
