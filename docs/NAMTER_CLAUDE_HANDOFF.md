# Claude 작업지시서 — Namter Phase 1 마감

## 역할과 목표

당신은 이미 진행 중인 **Namter** 신규 제품의 마감 담당이다. 기존 A2Meter를 수정하는 일이 아니다.

목표는 Aion 2 실제 PCAP을 재현 가능한 방식으로 분석하는 Namter Phase 1을 **검증·리뷰·커밋 가능한 상태**로 끝내는 것이다. 불확실한 결과를 숨기거나 fixture에 맞추기 위한 actor/time/skill 하드코딩은 금지한다.

## 작업 위치

```text
worktree : E:\A2Viewer\A2Meter\.worktrees\namter-capture-engine
branch   : codex/namter-capture-engine
solution : Namter.slnx
pcap     : E:\A2Viewer\A2Meter\captures\aion2_part001.pcap   (read-only)
fixtures : E:\A2Viewer\A2Meter\captures\*_readable           (read-only)
```

현재 worktree는 의도적으로 **uncommitted** 상태다. Task 12 구현, 문서, 최종 리뷰 대응이 모두 들어 있다. `git reset --hard`, `git checkout --`, fixture 삭제를 하지 말고, 먼저 `git status`, `git diff`, 최신 테스트 결과를 확인하라.

## 절대 지켜야 할 계약

1. Namter는 독립 제품이다. Namter에서 A2Meter/PacketEngine 프로젝트·소스·네이티브 바이너리를 참조하지 않는다.
2. 네이티브 코어는 C++20, 호스트/리듀서는 .NET이다.
3. 사용자는 WinDivert/Npcap을 선택한다.
   - WinDivert는 sniff/read-only이다. reinject 금지.
   - Npcap은 제품에 동봉·다운로드·설치·브라우저 자동 실행하지 않는다. 외부 설치 상태만 진단한다.
4. 게임 가변 데이터는 로컬 SQLite `aion.db`에 둔다. 업데이트 실패가 active DB를 교체하면 안 되며 rollback이 가능해야 한다.
5. DB는 opcode identity/activation, boss·dungeon·job alias, entity/buff policy를 담당한다. 가변 길이 전투 payload는 **버전된 bounded C++ parser strategy**가 담당한다. 이를 일반 DB layout interpreter로 바꾸는 것은 현재 범위 밖 설계 변경이다.
6. 실제 증거가 우선이다. PCAP과 readable `.inglog` 결과가 다르면 원본 PCAP 시각을 바꾸지 말고 comparator 보고서에 provenance 차이로 남긴다.

## 현재 구현 상태

- capture ABI, WinDivert/Npcap 선택, TCP reassembly, framing, 압축 해제, DB snapshot, reducer, CLI replay/compare/artifact publish가 구현되어 있다.
- 실제 production profile: `aion2-production-2026-07-10`.
- 실제 parser가 self/party identity, named summon, clone/action, combat-state, boss HP, direct damage, DoT/heal, content progress, buff observation을 처리한다.
- category-4 multi-hit tail은 bounded backtracking으로 구현했다. `damage`와 `multiDamage`는 별도 값이며 서로 빼면 안 된다.
- named summon은 유일한 플레이어 이름 매칭으로만 owner를 추론한다.
- clone echo는 `clone entity + causal action-kind 3`일 때만 제외한다. clone 전체/특정 skill/actor를 제외하면 안 된다.
- production profile의 미지원이지만 유효한 variant는 bounded `Unknown` event로 보존한다. 실제 partial/lossy reset만 incomplete diagnostic으로 승격한다.
- full PCAP에는 3개의 실제 전투가 있다. **Turgen, Griosa 뒤 별도 런 Basilus(actor 17968)를 반드시 보존**한다.
- readable Basilus(actor 28353)는 reducer-only fixture다. PCAP에서 나왔다고 주장하면 안 된다.

## 실제 PCAP 기준값

| Encounter | actorId | mobCode | total damage | multi damage | participants | start ms |
|---|---:|---:|---:|---:|---:|---:|
| Turgen | 18804 | 2301721 | 230291779 | 49648275 | 5 | 1783689020004 |
| Griosa | 36737 | 2301722 | 229795893 | 48619527 | 5 | 1783689134002 |
| Basilus (separate run) | 17968 | 2301723 | 348510429 | 78276299 | 5 | 1783689289604 |

최신 replay 근거:

```text
events       107,937
encounters   3
diagnostics  0
metadata     complete
hash replay  7/7 identical
```

Turgen은 total/multi/5인/start/buff window `3382`/uptime `56`을 readable과 일치시켰다.

Griosa는 total/multi/5인/start/uptime `55`를 일치시켰다. buff window는 `3186` 대 readable `3187`이다. 이 1건은 임의 생성하거나 드롭하지 말고, 실제 packet-level 근거가 없는 한 PCAP/readable provenance discrepancy로 comparator report에 남겨라.

## 최근 검증 결과

최종 리뷰 수정 전 최신 결과이며, **아래 전체를 다시 실행해서 fresh evidence를 남겨야 한다.**

```text
Debug/Release solution build : PASS
Native tests                 : 186 total / 184 pass / 2 Npcap environment skip
Managed unit                 : 211 / 211 PASS
Managed integration          : 23 / 23 PASS
Replay determinism           : 7 / 7 identical artifact hashes
PCAP transport               : overlap 6 / duplicate bytes 66 / fabricated gap 0
```

추가 hostile/determinism coverage:

- native deterministic mutation corpus 1,024건
- 4,096 flow spray, framer cap 32
- replay speed 0/1/10 semantic equivalence
- real BossHp 순서 `18804 -> 36737 -> 17968`
- Task 11 후속: recursive basename provenance+hash, comparator 64MiB cap, direct `.stage`/`.backup` crash recovery 및 reparse refusal regression

## 지금 남은 일 (우선순위 순)

### 1. 최종 리뷰 수정사항 검증

구현자는 다음을 방금 수정했다고 보고했다. diff를 읽고 focused test부터 전체 test까지 재검증하라.

- `EncounterReducer`의 **모든** bounded collection이 capacity overflow 시 diagnostic + incomplete를 일관되게 만든다.
- entity removal이 clone/action 상태를 정리해 stale echo suppression을 만들지 않는다.
- real-PCAP integration test가 Turgen/Griosa exact totals/content와 Basilus third encounter 보존을 명시한다.

### 2. 전체 검증 매트릭스

repo의 Visual Studio/MSBuild wrapper를 우선 사용한다. 이 환경에서는 `NamterDisableFileTracking=true`와 중복 `Path/PATH` 정규화가 필요할 수 있다.

최소 실행 항목:

```powershell
msbuild Namter.slnx -m -p:Configuration=Debug -p:Platform=x64 -p:NamterDisableFileTracking=true
msbuild Namter.slnx -m -p:Configuration=Release -p:Platform=x64 -p:NamterDisableFileTracking=true

# native Debug/Release test executable
# managed unit/integration Debug/Release dotnet test --no-build
```

환경 의존 Npcap skip은 허용하되, skip 사유와 수를 보고서에 기록한다. 새 failure는 고치거나 실제 blocker로 명시한다.

### 3. 실PCAP replay와 comparison

production `aion.db`로 PCAP을 replay한다.

확인할 것:

- 3 encounters, 0 diagnostics, complete metadata
- 위 Turgen/Griosa 수치
- overlap 6 / duplicate bytes 66 / gap 0
- Turgen/Griosa readable compare report 생성
- report에는 PCAP timestamp와 separate readable-log clock 차이, healing/buff window/uptime 차이를 숨기지 않고 남긴다.

Comparator는 boss-target combat projection과 evidence-based timestamp tolerance를 쓴다. 실제 시간값을 readable 값으로 덮어쓰지 않는다.

### 4. 보안·배포 감사

다음을 검색/확인하고 증거를 Task 12 report에 남긴다.

- Namter에 A2Meter/PacketEngine dependency 없음
- Npcap installer/download/browser/action API 없음
- Npcap/WinDivert binary, private key, generated `aion.db`, generated artifact가 Git에 tracked되지 않음
- `THIRD-PARTY-NOTICES.txt`와 `NAMTER_CAPTURE_OPERATIONS.md`의 version/license/운영 안내가 현재 구현과 일치

### 5. 독립 재리뷰 및 커밋

- Critical/Important review finding은 커밋 전에 0개여야 한다.
- Minor는 `.superpowers/sdd/` report에 기록한다.
- `.superpowers/sdd/progress.md`와 Task 12 report에 fresh command/results, known discrepancy, review 결론을 기록한다.
- 검증이 끝나면 모든 의도된 Task 12 파일을 stage하고 Lore protocol로 커밋한다.

커밋 intent line:

```text
Prove Namter phase one against real and hostile traffic
```

트레일러에는 real PCAP evidence, full test matrix, 3rd Basilus truth, 비교 provenance 차이, Npcap skip 여부를 포함한다.

## 핵심 파일

```text
docs/superpowers/specs/2026-07-11-namter-capture-engine-design.md
docs/superpowers/plans/2026-07-11-namter-capture-engine.md
docs/NAMTER_CAPTURE_OPERATIONS.md
src/Namter/Namter.Cli/THIRD-PARTY-NOTICES.txt

src/Namter/Namter.Core.Native/src/protocol.cpp
src/Namter/Namter.Core.Native/src/capture_pipeline.cpp
src/Namter/Namter.Encounter/EncounterReducer.cs
src/Namter/Namter.Cli/Commands/PipelineRunner.cs
src/Namter/Namter.Cli/Comparison/GoldenComparator.cs

src/Namter/Namter.Tests.Integration/Golden/GoldenComparisonTests.cs
src/Namter/Namter.Tests.Integration/Golden/AionPart001Tests.cs
src/Namter/Namter.Tests.Integration/Golden/ReplayDeterminismTests.cs
src/Namter/Namter.Core.Native.Tests/src/protocol_tests.cpp
src/Namter/Namter.Core.Native.Tests/src/stress_tests.cpp
src/Namter/Namter.Core.Native.Tests/src/fuzz_corpus_tests.cpp

.superpowers/sdd/   # ignored scratch replay/comparison/test evidence
```

## 금지 사항

- legacy A2Meter main worktree의 기존 변경을 수정하지 말 것
- captures fixture를 수정/삭제하지 말 것
- `git reset --hard`, `git checkout --` 사용 금지
- WinDivert/Npcap binary 다운로드·번들·설치 금지
- PCAP 결과에 맞추기 위한 actorId/time/skill ID 하드코딩 금지
- third Basilus를 필터링하여 전체 PCAP이 두 encounter라고 주장하는 행위 금지
