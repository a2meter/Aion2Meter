[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Id,

    [string]$OutputPath,

    [string]$Aion2PowerBaseUrl = "https://www.aion2power.com",

    [string]$GameDataBaseUrl = "https://api.aion2meter.com",

    [int]$BossEntityId = 9001,

    [int]$BossCode = 0,

    [int]$BossHp = 0,

    [int]$Stage = 0,

    [int]$BossHpIntervalSec = 5
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $root = Resolve-Path (Join-Path $PSScriptRoot "..")
    $OutputPath = Join-Path $root ("samples\aion2power-{0}.jsonl" -f $Id)
}

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
Add-Type -AssemblyName System.Net.Http

$classToJobCode = @{
    0 = 5
    1 = 13
    2 = 25
    3 = 17
    4 = 9
    5 = 21
    6 = 29
    7 = 33
}

function U {
    param([string]$CodePoints)
    return -join ($CodePoints -split " " | Where-Object { $_ } | ForEach-Object { [char][Convert]::ToInt32($_, 16) })
}

$serverNameToId = @{}
$serverNameToId[(U "C2DC C5D8")] = 1001
$serverNameToId[(U "B124 C790 CE78")] = 1002
$serverNameToId[(U "BC14 C774 C824")] = 1003
$serverNameToId[(U "CE74 C774 C2DC B12C")] = 1004
$serverNameToId[(U "C720 C2A4 D2F0 C5D8")] = 1005
$serverNameToId[(U "C544 B9AC C5D8")] = 1006
$serverNameToId[(U "D504 B808 AE30 C628")] = 1007
$serverNameToId[(U "BA54 C2A4 B78C D0C0 C5D0 B2E4")] = 1008
$serverNameToId[(U "D788 D0C0 B2C8 C5D0")] = 1009
$serverNameToId[(U "B098 B2C8 C544")] = 1010
$serverNameToId[(U "D0C0 D558 BC14 D0C0")] = 1011
$serverNameToId[(U "B8E8 D130 C2A4")] = 1012
$serverNameToId[(U "D398 B974 B178 C2A4")] = 1013
$serverNameToId[(U "B2E4 BBF8 B204")] = 1014
$serverNameToId[(U "CE74 C0AC CE74")] = 1015
$serverNameToId[(U "BC14 CE74 B974 B9C8")] = 1016
$serverNameToId[(U "CC48 AC00 B8FD")] = 1017
$serverNameToId[(U "CF54 CE58 B8FD")] = 1018
$serverNameToId[(U "C774 C288 D0C0 B974")] = 1019
$serverNameToId[(U "D2F0 C544 B9C8 D2B8")] = 1020
$serverNameToId[(U "D3EC C5D0 D0C0")] = 1021
$serverNameToId[(U "C774 C2A4 B77C D3A0")] = 2001
$serverNameToId[(U "C9C0 CF08")] = 2002
$serverNameToId[(U "D2B8 B9AC B2C8 C5D8")] = 2003
$serverNameToId[(U "B8E8 BBF8 C5D8")] = 2004
$serverNameToId[(U "B9C8 B974 CFE0 D0C4")] = 2005
$serverNameToId[(U "C544 C2A4 D3A0")] = 2006
$serverNameToId[(U "C5D0 B808 C288 D0A4 AC08")] = 2007
$serverNameToId[(U "BE0C B9AC D2B8 B77C")] = 2008
$serverNameToId[(U "B124 BABD")] = 2009
$serverNameToId[(U "D558 B2EC")] = 2010
$serverNameToId[(U "B8E8 B4DC B77C")] = 2011
$serverNameToId[(U "C6B8 ACE0 B978")] = 2012
$serverNameToId[(U "BB34 B2CC")] = 2013
$serverNameToId[(U "C624 B2E4 B974")] = 2014
$serverNameToId[(U "C820 CE74 CE74")] = 2015
$serverNameToId[(U "D06C B85C BA54 B370")] = 2016
$serverNameToId[(U "CF70 C774 B9C1")] = 2017
$serverNameToId[(U "BC14 BC14 B8FD")] = 2018
$serverNameToId[(U "D30C D504 B2C8 B974")] = 2019
$serverNameToId[(U "C778 B4DC B098 D750")] = 2020
$serverNameToId[(U "C774 C2A4 D560 AC90")] = 2021

function Get-JsonUtf8 {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Uri,

        [hashtable]$Headers = @{}
    )

    $client = [System.Net.Http.HttpClient]::new()
    try {
        foreach ($key in $Headers.Keys) {
            [void]$client.DefaultRequestHeaders.TryAddWithoutValidation($key, [string]$Headers[$key])
        }

        $bytes = $client.GetByteArrayAsync($Uri).GetAwaiter().GetResult()
        $text = [System.Text.Encoding]::UTF8.GetString($bytes)
        try {
            return $text | ConvertFrom-Json -Depth 100
        }
        catch [System.Management.Automation.ParameterBindingException] {
            return $text | ConvertFrom-Json
        }
    }
    finally {
        $client.Dispose()
    }
}

function Get-Long {
    param($Value, [long]$Default = 0)
    if ($null -eq $Value) { return $Default }
    $text = [string]$Value
    $parsed = 0L
    if ([long]::TryParse($text, [ref]$parsed)) { return $parsed }
    return $Default
}

function Get-Int {
    param($Value, [int]$Default = 0)
    if ($null -eq $Value) { return $Default }
    $text = [string]$Value
    $parsed = 0
    if ([int]::TryParse($text, [ref]$parsed)) { return $parsed }
    return $Default
}

function Get-Double {
    param($Value, [double]$Default = 0)
    if ($null -eq $Value) { return $Default }
    $text = [string]$Value
    $parsed = 0.0
    if ([double]::TryParse($text, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
        return $parsed
    }
    return $Default
}

function As-JsonArray {
    param($Value)
    if ($null -eq $Value) { return @() }
    if ($Value -is [System.Array]) { return @($Value) }

    $valueProperty = $Value.PSObject.Properties["value"]
    if ($null -ne $valueProperty -and $valueProperty.Value -is [System.Array]) {
        return @($valueProperty.Value)
    }

    return @($Value)
}

function Get-BaseJobCode {
    param($ClassId)
    $class = Get-Int $ClassId 0
    if ($classToJobCode.ContainsKey($class)) { return [int]$classToJobCode[$class] }
    return 5
}

function Get-ServerId {
    param([string]$Name)
    if ($serverNameToId.ContainsKey($Name)) { return [int]$serverNameToId[$Name] }
    return 1001
}

function Get-SkillScore {
    param($Skill)
    $score = 0
    $code = Get-Int $Skill.code 0
    if (($code % 10000) -eq 3450) { $score += 100 }
    if (-not [string]::IsNullOrWhiteSpace([string]$Skill.iconUrl)) { $score += 70 }
    if ($Skill.skillType -eq "Active") { $score += 50 }
    if ($Skill.skillType -eq "System") { $score += 20 }
    if ((Get-Int $Skill.isBasic 0) -ne 0) { $score += 5 }
    if ($Skill.skillType -eq "Passive") { $score -= 200 }
    return $score
}

function Select-BestSkill {
    param($Skills)
    $best = $null
    $bestScore = [int]::MinValue
    $bestCode = [int]::MinValue

    foreach ($skill in $Skills) {
        $score = Get-SkillScore $skill
        $code = Get-Int $skill.code 0
        if ($null -eq $best -or $score -gt $bestScore -or ($score -eq $bestScore -and $code -gt $bestCode)) {
            $best = $skill
            $bestScore = $score
            $bestCode = $code
        }
    }

    return $best
}

function Get-StableFallbackSkillCode {
    param([string]$Name, [int]$JobCode)
    $md5 = [System.Security.Cryptography.MD5]::Create()
    try {
        $bytes = [System.Text.Encoding]::UTF8.GetBytes($Name)
        $hashBytes = $md5.ComputeHash($bytes)
        $hash = [BitConverter]::ToUInt32($hashBytes, 0)
        return [int](90000000 + (($JobCode * 100000 + ($hash % 99999)) % 9000000))
    }
    finally {
        $md5.Dispose()
    }
}

function ConvertTo-JsonLine {
    param($Object)
    return ($Object | ConvertTo-Json -Compress -Depth 50)
}

function New-OrderedRow {
    param([hashtable]$Values)
    $row = [ordered]@{}
    foreach ($key in $Values.Keys) { $row[$key] = $Values[$key] }
    return [pscustomobject]$row
}

function Get-MemberEntityId {
    param($Member, [int]$Fallback)

    $ids = @()
    foreach ($buff in @($Member.buff_uptime)) {
        $id = Get-Int $buff.casterId 0
        if ($id -gt 0) { $ids += $id }
    }

    if ($ids.Count -gt 0) {
        return [int](($ids | Group-Object | Sort-Object Count -Descending | Select-Object -First 1).Name)
    }

    return $Fallback
}

function New-SyntheticTimes {
    param([int]$Count, [double]$DurationSec)
    $times = New-Object System.Collections.Generic.List[double]
    if ($Count -le 0) { return $times }
    $start = [Math]::Min(3.0, [Math]::Max(0.0, $DurationSec * 0.05))
    $span = [Math]::Max(1.0, $DurationSec - $start - 0.5)
    for ($i = 0; $i -lt $Count; $i++) {
        $times.Add($start + (($i + 0.5) * $span / $Count))
    }
    return $times
}

function New-DamageRows {
    param(
        [object]$Member,
        [hashtable]$MemberInfo,
        [hashtable]$SkillCodeByName,
        [double]$DurationSec,
        [int]$BossEntityId
    )

    $rows = New-Object System.Collections.Generic.List[object]
    $timelineByName = @{}
    foreach ($skillTimeline in @($Member.skill_timeline.skills)) {
        $name = [string]$skillTimeline.name
        if (-not [string]::IsNullOrWhiteSpace($name)) {
            $timelineByName[$name] = @($skillTimeline.times)
        }
    }

    foreach ($skill in @($Member.skills)) {
        $name = [string]$skill.skill_name
        if ([string]::IsNullOrWhiteSpace($name)) { continue }

        $totalDamage = Get-Long $skill.total_damage 0
        if ($totalDamage -le 0) { continue }

        if ($timelineByName.ContainsKey($name)) {
            $times = @($timelineByName[$name])
        }
        else {
            $times = @(New-SyntheticTimes -Count (Get-Int $skill.hit_count 0) -DurationSec $DurationSec)
        }

        if ($times.Count -eq 0) { continue }
        if ($totalDamage -lt $times.Count) {
            $times = @($times | Select-Object -First ([int]$totalDamage))
        }

        $skillCode = if ($SkillCodeByName.ContainsKey($name)) { [int]$SkillCodeByName[$name] } else { Get-StableFallbackSkillCode $name ([int]$MemberInfo.JobCode) }
        $count = $times.Count
        $weights = New-Object System.Collections.Generic.List[double]
        $weightSum = 0.0
        for ($i = 0; $i -lt $count; $i++) {
            $wave = [Math]::Sin(($i + 1) * 12.9898 + $skillCode * 0.0001)
            $weight = 0.75 + ([Math]::Abs($wave) * 0.6)
            $weights.Add($weight)
            $weightSum += $weight
        }

        $emitted = 0L
        $critCount = [Math]::Min($count, [Math]::Max(0, (Get-Int $skill.crit_count 0)))
        for ($i = 0; $i -lt $count; $i++) {
            $remaining = $totalDamage - $emitted
            $remainingEvents = $count - $i
            if ($i -eq ($count - 1)) {
                $damage = [Math]::Max(1L, $remaining)
            }
            elseif ($remaining -le $remainingEvents) {
                $damage = 1L
            }
            else {
                $damage = [Math]::Max(1L, [long][Math]::Round($totalDamage * $weights[$i] / $weightSum))
                $maxAllowed = $remaining - ($remainingEvents - 1)
                if ($damage -gt $maxAllowed) { $damage = $maxAllowed }
            }

            $emitted += $damage
            $timeMs = [int][Math]::Round((Get-Double $times[$i] 0) * 1000)
            if ($timeMs -lt 0) { $timeMs = 0 }

            $crit = $false
            if ($critCount -gt 0) {
                $crit = ($i -lt $critCount)
            }

            $rows.Add((New-OrderedRow @{
                deltaTime = $timeMs
                Opcode = "Damage"
                ActorId = [int]$MemberInfo.EntityId
                TargetId = $BossEntityId
                SkillCode = $skillCode
                SkillName = $name
                Damage = [int][Math]::Min([int]::MaxValue, $damage)
                Crit = $crit
            }))
        }
    }

    return $rows
}

function Scale-DamageRows {
    param($DamageRows, [long]$TargetTotal)
    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($row in $DamageRows) { $rows.Add($row) }
    $current = [long](($rows | Measure-Object -Property Damage -Sum).Sum)
    if ($rows.Count -eq 0 -or $current -le 0 -or $TargetTotal -le 0 -or $current -eq $TargetTotal) { return $rows }

    $emitted = 0L
    for ($i = 0; $i -lt $rows.Count; $i++) {
        if ($i -eq ($rows.Count - 1)) {
            $damage = [Math]::Max(1L, $TargetTotal - $emitted)
        }
        else {
            $damage = [Math]::Max(1L, [long][Math]::Round(([long]$rows[$i].Damage) * $TargetTotal / $current))
            $maxAllowed = $TargetTotal - $emitted - ($rows.Count - $i - 1)
            if ($damage -gt $maxAllowed) { $damage = $maxAllowed }
        }
        $rows[$i].Damage = [int][Math]::Min([int]::MaxValue, $damage)
        $emitted += $damage
    }

    return $rows
}

function Get-BossCodeFromGameData {
    param([int]$DungeonId, [string]$BossName, [string]$BaseUrl)
    if ($DungeonId -le 0) { return 0 }
    try {
        $bossRows = As-JsonArray (Get-JsonUtf8 -Uri ("{0}/api/gamedata/bosses?dungeonId={1}" -f $BaseUrl.TrimEnd("/"), $DungeonId))
        $match = @($bossRows | Where-Object { $_.bossName -eq $BossName } | Select-Object -First 1)
        if ($match.Count -gt 0) { return Get-Int $match[0].mobId 0 }
        $first = @($bossRows | Select-Object -First 1)
        if ($first.Count -gt 0) { return Get-Int $first[0].mobId 0 }
    }
    catch {
        Write-Warning ("boss code lookup failed: {0}" -f $_.Exception.Message)
    }
    return 0
}

$detailUrl = "{0}/api/meter/detail?id={1}" -f $Aion2PowerBaseUrl.TrimEnd("/"), [Uri]::EscapeDataString($Id)
$headers = @{
    accept = "*/*"
    "accept-language" = "ko,en-US;q=0.9,en;q=0.8"
    "cache-control" = "no-cache"
    pragma = "no-cache"
    referer = ("{0}/combat/{1}" -f $Aion2PowerBaseUrl.TrimEnd("/"), $Id)
    "user-agent" = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36"
}

Write-Host ("fetch: {0}" -f $detailUrl)
$response = Get-JsonUtf8 -Uri $detailUrl -Headers $headers
$record = if ($null -ne $response.data) { $response.data } else { $response }

$dungeonId = Get-Int $record.dungeon_id 0
$durationSec = Get-Double $record.battle_duration_sec 0
if ($durationSec -le 0) { throw "battle_duration_sec is empty" }

if ($BossCode -le 0) {
    $BossCode = Get-BossCodeFromGameData -DungeonId $dungeonId -BossName ([string]$record.dungeon_name) -BaseUrl $GameDataBaseUrl
}
if ($BossCode -le 0) { $BossCode = 2301059 }

$members = @($record.party_members | Sort-Object { Get-Int $_.rank_in_party 999 })
if ($members.Count -eq 0) { throw "party_members is empty" }

$serverId = Get-ServerId ([string]$record.uploader_server)
$usedEntityIds = @{}
$memberInfos = @{}
$fallbackEntityId = 1001
foreach ($member in $members) {
    $entityId = Get-MemberEntityId -Member $member -Fallback $fallbackEntityId
    while ($usedEntityIds.ContainsKey($entityId)) {
        $fallbackEntityId++
        $entityId = $fallbackEntityId
    }
    $usedEntityIds[$entityId] = $true
    $fallbackEntityId++

    $memberInfos[[string]$member.id] = @{
        EntityId = $entityId
        Name = [string]$member.character_name
        JobCode = Get-BaseJobCode $member.class_id
        ServerId = $serverId
        Level = 55
        CombatPower = Get-Int $member.combat_power 0
    }
}

$self = @($members | Where-Object { $_.character_name -eq $record.uploader_name } | Select-Object -First 1)
if ($self.Count -eq 0) { $self = @($members | Select-Object -First 1) }
$selfInfo = $memberInfos[[string]$self[0].id]

Write-Host "fetch game skills..."
$allSkills = As-JsonArray (Get-JsonUtf8 -Uri ("{0}/api/gamedata/skills" -f $GameDataBaseUrl.TrimEnd("/")))
$skillsByJobAndName = @{}
$skillsByName = @{}
foreach ($skill in $allSkills) {
    $name = [string]$skill.name
    if ([string]::IsNullOrWhiteSpace($name)) { continue }
    $jobCode = Get-Int $skill.jobCode 0
    $jobKey = "{0}|{1}" -f $jobCode, $name
    if (-not $skillsByJobAndName.ContainsKey($jobKey)) { $skillsByJobAndName[$jobKey] = New-Object System.Collections.Generic.List[object] }
    $skillsByJobAndName[$jobKey].Add($skill)
    if (-not $skillsByName.ContainsKey($name)) { $skillsByName[$name] = New-Object System.Collections.Generic.List[object] }
    $skillsByName[$name].Add($skill)
}

$skillCodeByMemberSkillName = @{}
foreach ($member in $members) {
    $info = $memberInfos[[string]$member.id]
    $map = @{}
    foreach ($skill in @($member.skills)) {
        $name = [string]$skill.skill_name
        if ([string]::IsNullOrWhiteSpace($name) -or $map.ContainsKey($name)) { continue }

        $jobKey = "{0}|{1}" -f $info.JobCode, $name
        $best = $null
        if ($skillsByJobAndName.ContainsKey($jobKey)) { $best = Select-BestSkill $skillsByJobAndName[$jobKey] }
        if ($null -eq $best -and $skillsByName.ContainsKey($name)) { $best = Select-BestSkill $skillsByName[$name] }

        $map[$name] = if ($null -ne $best) { Get-Int $best.code 0 } else { Get-StableFallbackSkillCode $name ([int]$info.JobCode) }
    }
    $skillCodeByMemberSkillName[[string]$member.id] = $map
}

$damageRows = New-Object System.Collections.Generic.List[object]
foreach ($member in $members) {
    $rows = New-DamageRows -Member $member -MemberInfo $memberInfos[[string]$member.id] -SkillCodeByName $skillCodeByMemberSkillName[[string]$member.id] -DurationSec $durationSec -BossEntityId $BossEntityId
    foreach ($row in $rows) { $damageRows.Add($row) }
}

$generatedDamage = [long](($damageRows | Measure-Object -Property Damage -Sum).Sum)
if ($generatedDamage -le 0) { throw "no damage rows generated" }

$targetBossHp = if ($BossHp -gt 0) { [long]$BossHp } else { $generatedDamage }
if ($targetBossHp -gt [int]::MaxValue) {
    Write-Warning ("boss hp {0} exceeds Int32. Scaling to Int32.MaxValue for mock packet compatibility." -f $targetBossHp)
    $targetBossHp = [int]::MaxValue
}

$damageRows = @(Scale-DamageRows -DamageRows $damageRows -TargetTotal $targetBossHp)
$totalDamage = [long](($damageRows | Measure-Object -Property Damage -Sum).Sum)

$events = New-Object System.Collections.Generic.List[object]
$order = 0
function Add-Event {
    param($Row)
    $Row | Add-Member -NotePropertyName "_order" -NotePropertyValue $script:order -Force
    $script:order++
    $script:events.Add($Row)
}

function New-MemberPayload {
    param([hashtable]$Info)
    return [ordered]@{
        EntityId = [int]$Info.EntityId
        Name = [string]$Info.Name
        JobCode = [int]$Info.JobCode
        ServerId = [int]$Info.ServerId
        Level = [int]$Info.Level
        CombatPower = [int]$Info.CombatPower
    }
}

function New-MemberCommandRow {
    param([int]$DeltaTime, [string]$Opcode, $Payload)
    $row = @{
        deltaTime = $DeltaTime
        Opcode = $Opcode
    }
    foreach ($key in $Payload.Keys) { $row[$key] = $Payload[$key] }
    return New-OrderedRow $row
}

Add-Event (New-OrderedRow @{
    deltaTime = 0; Opcode = "SelfInfo"; EntityId = [int]$selfInfo.EntityId; Name = [string]$selfInfo.Name
    JobCode = [int]$selfInfo.JobCode; ServerId = [int]$selfInfo.ServerId; Level = [int]$selfInfo.Level; CombatPower = [int]$selfInfo.CombatPower
})
Add-Event (New-OrderedRow @{ deltaTime = 0; Opcode = "CombatPower"; EntityId = [int]$selfInfo.EntityId; CombatPower = [int]$selfInfo.CombatPower })

$partyPayloads = New-Object System.Collections.Generic.List[object]
$partyPayloads.Add((New-MemberPayload $selfInfo))
$joinIndex = 0
foreach ($member in $members) {
    $info = $memberInfos[[string]$member.id]
    if ([int]$info.EntityId -eq [int]$selfInfo.EntityId) { continue }

    $requestTime = 250 + ($joinIndex * 200)
    $acceptTime = $requestTime + 120
    $updateTime = $acceptTime + 30
    $payload = New-MemberPayload $info
    Add-Event (New-MemberCommandRow -DeltaTime $requestTime -Opcode "PartyRequest" -Payload $payload)
    Add-Event (New-MemberCommandRow -DeltaTime $acceptTime -Opcode "PartyAccept" -Payload $payload)
    $partyPayloads.Add($payload)
    Add-Event (New-OrderedRow @{ deltaTime = $updateTime; Opcode = "PartyUpdate"; DungeonId = $dungeonId; Stage = $Stage; Members = $partyPayloads.ToArray() })
    $joinIndex++
}

Add-Event (New-OrderedRow @{ deltaTime = 1200; Opcode = "DungeonEnter"; DungeonId = $dungeonId; Stage = $Stage })
$allMemberPayloads = New-Object System.Collections.Generic.List[object]
foreach ($member in $members) { $allMemberPayloads.Add((New-MemberPayload $memberInfos[[string]$member.id])) }
Add-Event (New-OrderedRow @{ deltaTime = 1300; Opcode = "PartyUpdate"; DungeonId = $dungeonId; Stage = $Stage; Members = $allMemberPayloads.ToArray() })
Add-Event (New-OrderedRow @{ deltaTime = 1500; Opcode = "MobSpawn"; EntityId = $BossEntityId; BossCode = $BossCode; Hp = [int]$targetBossHp })
Add-Event (New-OrderedRow @{ deltaTime = 1510; Opcode = "BossHp"; EntityId = $BossEntityId; Hp = [int]$targetBossHp })

foreach ($member in $members) {
    $info = $memberInfos[[string]$member.id]
    $buffIndex = 0
    foreach ($buff in @($member.buff_uptime)) {
        $buffId = Get-Int $buff.buffId 0
        if ($buffId -le 0) { continue }
        $durationMs = [int][Math]::Max(1000, [Math]::Round((Get-Double $buff.uptimeSeconds 0) * 1000))
        $casterId = Get-Int $buff.casterId ([int]$info.EntityId)
        Add-Event (New-OrderedRow @{
            deltaTime = 1600 + ($buffIndex * 35)
            Opcode = "Buff"
            EntityId = [int]$info.EntityId
            BuffId = $buffId
            BuffName = [string]$buff.name
            DurationMs = $durationMs
            CasterId = $casterId
        })
        $buffIndex++
    }
}

$debuffIndex = 0
foreach ($buff in @($record.boss_debuffs)) {
    $buffId = Get-Int $buff.buffId 0
    if ($buffId -le 0) { continue }
    $durationMs = [int][Math]::Max(1000, [Math]::Round((Get-Double $buff.uptimeSeconds 0) * 1000))
    Add-Event (New-OrderedRow @{
        deltaTime = 1700 + ($debuffIndex * 35)
        Opcode = "Buff"
        EntityId = $BossEntityId
        BuffId = $buffId
        BuffName = [string]$buff.name
        DurationMs = $durationMs
        CasterId = (Get-Int $buff.casterId 0)
    })
    $debuffIndex++
}

foreach ($row in $damageRows) { Add-Event $row }

$sortedDamage = @($damageRows | Sort-Object deltaTime, _order)
$damageCursor = 0
$cumulative = 0L
$interval = [Math]::Max(1, $BossHpIntervalSec)
for ($sec = $interval; $sec -lt [Math]::Ceiling($durationSec); $sec += $interval) {
    $t = [int]($sec * 1000)
    while ($damageCursor -lt $sortedDamage.Count -and [int]$sortedDamage[$damageCursor].deltaTime -le $t) {
        $cumulative += [long]$sortedDamage[$damageCursor].Damage
        $damageCursor++
    }
    Add-Event (New-OrderedRow @{ deltaTime = $t + 20; Opcode = "BossHp"; EntityId = $BossEntityId; Hp = [int][Math]::Max(0, $targetBossHp - $cumulative) })
}

$endMs = [int][Math]::Round($durationSec * 1000)
Add-Event (New-OrderedRow @{ deltaTime = $endMs; Opcode = "BossHp"; EntityId = $BossEntityId; Hp = 0 })
Add-Event (New-OrderedRow @{ deltaTime = $endMs + 50; Opcode = "EntityRemoved"; EntityId = $BossEntityId })

$finalEvents = @($events | Sort-Object deltaTime, _order)
foreach ($event in $finalEvents) { $event.PSObject.Properties.Remove("_order") }

$outDir = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrWhiteSpace($outDir)) {
    [System.IO.Directory]::CreateDirectory($outDir) | Out-Null
}

$lines = @($finalEvents | ForEach-Object { ConvertTo-JsonLine $_ })
[System.IO.File]::WriteAllLines($OutputPath, $lines, $utf8NoBom)

$damageByActor = @{}
foreach ($row in $damageRows) {
    $actor = [string]$row.ActorId
    if (-not $damageByActor.ContainsKey($actor)) { $damageByActor[$actor] = 0L }
    $damageByActor[$actor] += [long]$row.Damage
}

Write-Host ("wrote: {0}" -f $OutputPath)
Write-Host ("lines={0} damageEvents={1} buffEvents={2} totalDamage={3} durationSec={4} bossCode={5}" -f $lines.Count, $damageRows.Count, (@($finalEvents | Where-Object { $_.Opcode -eq "Buff" }).Count), $totalDamage, $durationSec, $BossCode)
Write-Host "damageByActor:"
$damageByActor.GetEnumerator() | Sort-Object Name | ForEach-Object {
    Write-Host ("  {0}: {1}" -f $_.Name, $_.Value)
}
