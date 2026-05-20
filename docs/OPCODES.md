# A2 (Aion 2) 게임 프로토콜 Opcode 문서

본 문서는 A2Meter 프로젝트에서 사용되는 A2(아이온 2) 게임 프로토콜의 opcode 및 패킷 구조를 정리한 것입니다.

---

## 프로토콜 기본 구조

| 항목 | 설명 |
|------|------|
| 프레임 구분자 | `06 00 36` (3바이트) — TCP 스트림 내 모든 프레임 메시지를 구분 |
| 길이 접두사 | varint (protobuf 스타일 가변 길이 정수) |
| 압축 | LZ4 컨테이너 포맷 (배치 메시지용) |

---

## 전투 스트림 (PacketDispatcher) — 태그 기반 식별

전투 스트림은 초기 varint 이후 2바이트 "태그" 패턴으로 메시지 유형을 식별합니다.

### 태그 목록

| 태그 바이트 | 상수 | 설명 |
|-------------|------|------|
| `04 38` | TAG_DAMAGE_1=4, TAG_DAMAGE_2=56 | 데미지 (피격) |
| `05 38` | TAG_DOT_1=5, TAG_DOT_2=56 | DoT (지속 피해) / 힐 |
| `2A 38` | TAG_BATTLE_STATS_1=42, TAG_BATTLE_STATS_2=56 | 전투 통계 / 버프 적용 |
| `2B 38` | TAG_BATTLE_STATS_ALT_1=43, TAG_BATTLE_STATS_2=56 | 전투 통계 (대체) |
| `33 36` | TAG_SELF_INFO_1=51, TAG_SELF_INFO_2=54 | 자기 유저 정보 |
| `44 36` | TAG_OTHER_INFO_1=68, TAG_OTHER_INFO_2=54 | 타 플레이어 정보 |
| `40 36` | TAG_MOB_SPAWN_1=64, TAG_MOB_SPAWN_2=54 | 몬스터/NPC 스폰 |
| `03 36` | TAG_GUARD_1=3, TAG_GUARD_2=54 | 가드 프레임 (무시, 전투 데이터 없음) |
| `21 8D` | TAG_ENTITY_REMOVED_1=33, TAG_ENTITY_REMOVED_2=141 | 엔티티 제거 |
| `4F 36` | TAG_CHAR_LOOKUP_1=79, TAG_CHAR_LOOKUP_2=54 | 캐릭터 조회 응답 |

### 전투력 마커

| 마커 바이트 | 상수 | 설명 |
|-------------|------|------|
| `00 92` | CP_PACKET_MARKER | 전투력 패킷 표시자 |
| `2F 2A 38` | EntityCpPacketMarker | 엔티티 전투력 |
| `0E 55 36` | EntityCpTailMarker | 엔티티 전투력 후미 |
| `06 00 36` | sync marker | CP 스캔용 위치 기준점 |

---

### 데미지 패킷 구조

```
[varint: 프레임 길이]
[TAG_DAMAGE: 04 38]
[varint: targetId]
[varint: flags1] → category = flags1 & 0xF (유효값: 4-7)
[varint: skip]
[varint: actorId]
[skill code bytes]
[varint: damageType]
[flagByte + 0x00 패딩]
[카테고리별 후행 바이트: {4:8, 5:12, 6:10, 7:14}]
[varint: skip]
[varint: damage]
[선택: 다단히트 (varint count + N개 varint damages)]
[선택: 힐 (0x03 0x00 + varint healAmount)]
```

---

### DoT 패킷 구조

```
[varint: 프레임 길이]
[TAG_DOT: 05 38]
[varint: targetId]
[byte: flags — bit 1 = hasExtraDamage]
[varint: actorId]
[varint: healAmount]
[LE32: rawMobCode → skillCode = mobCode/1000 또는 /100]
[선택 (hasExtra인 경우): varint damage]
```

---

### 버프/전투 통계 구조

```
[TAG_BATTLE_STATS: 2A 38 또는 2B 38]
[varint: entityId]
[1 byte skip]
[byte: type]
[varint: skip]
[LE32: buffId]
[LE32: durationMs]
[4 bytes skip]
[LE64: timestamp]
[선택: varint casterId]
```

---

### 자기 유저 정보 구조

```
[TAG_SELF_INFO: 33 36]
[varint: entityId]
[0x07 마커 스캔 → varint nameLen + UTF-8 name]
[LE16: serverId]
[byte: jobCode]
```

---

### 타 플레이어 정보 구조

```
[TAG_OTHER_INFO: 44 36]
[varint: entityId]
[varint: skip] x2
[0x07 마커 → varint nameLen + UTF-8 name]
[varint: jobCode]
[서버 ID 스캔: 오프셋 +75..+108 범위]
```

---

### 몬스터 스폰 구조

```
[TAG_MOB_SPAWN: 40 36]
[varint: mobEntityId]
[몬스터 코드 마커 스캔: 00 [&0xBF==0] 02]
[LE24: mobCode (마커 3바이트 전)]
[HP 스캔: byte 0x01 → varint hpMax → varint hpCur]
```

---

### 엔티티 제거 구조

```
[varint: 프레임 길이]
[TAG_ENTITY_REMOVED: 21 8D]
[varint: entityId]
```

---

### 보스 HP 구조 (raw 스캔)

```
[byte: 141 (0x8D)]
[varint: entityId]
[02 01 00]
[LE32: currentHp]
[LE32: 0이어야 유효 (검증)]
```

---

### 캐릭터 조회 구조

```
[TAG_CHAR_LOOKUP: 4F 36]
[2 bytes 패딩]
[0x07 마커]
[varint: nameLen]
[UTF-8: name]
[byte: jobCode]
[3 bytes 0x00]
[1 byte: 0x01]
[1 byte: isSelf (1=본인, 2=타인)]
[1 byte: level]
[7 bytes 0x00]
[LE32: entityId]
[LE16: serverId]
...
[후미-9 위치: 0x14 + LE32 combatPower + 5 bytes 0x00]
```

---

## 파티 스트림 (PartyStreamParser) — Opcode 기반

프레이밍: `06 00 36` 매직으로 구분됩니다. 내부 프레임은 varint 길이 접두사를 사용합니다.
Opcode의 두 번째 바이트는 항상 `0x97` (151)입니다.

### Opcode 목록

| Opcode | Hex | 설명 |
|--------|-----|------|
| 1 | `01 97` | 파티 목록 (전체 인원) |
| 2 | `02 97` | 파티 업데이트 (멤버 변경) |
| 4 | `04 97` | 던전 퇴장 |
| 7 | `07 97` | 파티 요청 (초대 수신) |
| 11 | `0B 97` | 파티 수락 (멤버 합류) |
| 19 | `13 97` | 게시판 갱신 / 제어 |
| 29 | `1D 97` | 파티 탈퇴 / 해산 |
| 42 | `2A 97` | 게시판 갱신 트리거 |

---

### 파티 멤버 블록 구조

```
[byte: nameLen]
[UTF-8: nickname]
[LE32: jobCode]
[LE32: level]
[LE32: combatPower]
[LE16: serverId] (x2 반복)
[0x04 마커 + LE32 combatPower (최종)]
```

---

### 던전 감지

태그 `02 97` 내부 중첩 구조:

```
[3 bytes + 0x00]
[varint: skip length + skip bytes]
[byte: marker (4 또는 8)]
[LE32: dungeonId] (유효 범위: 600000-699999)
[byte: stage]
```

---

## 프로토콜 상수

| 항목 | 값 | 비고 |
|------|-----|------|
| 서버 포트 | 13328 | 기본값, 휴리스틱으로 자동 감지 |
| 유효 서버 ID | 1001-2021 | |
| 스킬 코드 센티널 | 12,250,030 | |
| 최대 이름 길이 | 72 바이트 | |
| 전투력 범위 | 10,000-999,999 | |
| 레벨 범위 | 1-55 | |
| 직업 코드 범위 | 1-40 | |
| 던전 ID 범위 | 600,000-699,999 | |

---

## 데미지 플래그

```
specialFlags = (flagByte & 0x7F)
             | (flagByte & 0x80 ? 0x80 : 0)
             | (damageType == 3 ? 0x100 : 0)   // 크리티컬
```

카테고리별 후행 바이트 크기: `[0, 0, 0, 0, 8, 12, 10, 14]` (카테고리 0-7 인덱스)

---

## 소환수 감지

몬스터 스폰 프레임 (`40 36`) 내부:

| 항목 | 값 | 설명 |
|------|-----|------|
| 경계 마커 | `FF FF FF FF FF FF FF FF` | 8바이트 |
| 액터 헤더 | `07 02 06` | |
| 액터 ID | LE16 (헤더+3 위치) | 99 초과일 때 유효 |
