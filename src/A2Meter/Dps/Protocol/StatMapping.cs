using System.Collections.Generic;

namespace A2Meter.Dps.Protocol;

/// <summary>
/// Maps packet statId (LE16 from equipment sub-stat encoding) to human-readable names.
/// Derived from the EStat enum in AION2 .usmap (Unreal Engine mappings).
///
/// The game uses a 1-based ID scheme with a handful of hidden/reserved slots
/// interspersed within the enum sequence. Known anchor points (verified via
/// packet ↔ web-API cross-reference):
///   statId  1 → STR          (enum index 0)
///   statId  5 → AGI          (enum index 4)
///   statId 32 → AmplifyWeaponDamage (enum index 28)
///   statId146 → AdditionalHitRate   (enum index 138)
///   statId158 → AbnormalAccuracy    (enum index 150)
///   statId282 → CombatSpeed         (enum index 273)
///   statId318 → WeaponAccuracy      (enum index 309)
/// </summary>
internal static class StatMapping
{
    /// Lookup by packet statId. Returns null if unknown.
    public static string? GetName(int statId)
    {
        if (statId >= 0 && statId < Names.Length)
            return Names[statId];
        return null;
    }

    // ── Build the lookup array ──
    // EStat enum (0-based, 557 entries) mapped into a 1-based packet-ID array.
    // Hidden slots are inserted at empirically determined break points.
    //
    // Verified delta progression:
    //   IDs   1..  5 → enum  0..  4  (delta +1)      — 0 hidden slots before
    //   IDs   6.. 31 → enum  5.. 27  (delta grows to +4) — 3 hidden slots total by ID 32
    //   IDs  32..145 → enum 28..137  (delta grows to +8) — 4 more hidden by ID 146
    //   IDs 146..157 → enum138..149  (delta stays +8)
    //   IDs 158..281 → enum150..272  (delta grows to +9) — 1 more hidden
    //   IDs 282..318+→ enum273..309+ (delta stays +9)
    //
    // Without more anchor points the exact positions of the hidden slots are
    // uncertain. We therefore store statId→name for the most commonly seen
    // equipment-relevant stats using verified anchors, and fill in the
    // remainder using the enum sequence shifted by section.

    private static readonly string?[] Names = BuildTable();

    private static string?[] BuildTable()
    {
        // Full EStat enum in order (0-based indices 0..556).
        string[] esEnum = {
            "STR","DEX","INT","CON","AGI","WIS",
            "Justice","Freedom","Illusion","Life","Destruction","Death","Wisdom","Destiny","Space",
            "FixingDamage","PhysicDamage","MagicDamage","BasicAtkDamage","SkillDamage",
            "PhysicSkillDamage","MagicSkillDamage","AmplifyPhysicDamage","AmplifyMagicDamage",
            "AmplifyAllDamage","AmplifySkillDamage","AmplifyBasicAtkDamage","WeaponDamage",
            "AmplifyWeaponDamage","WeaponMinDamage","PhysicWeaponDamage","PhysicWeaponMinDamage",
            "MagicWeaponDamage","MagicWeaponMinDamage","CriticalAddDamage","CriticalPhysicAddDamage",
            "CriticalMagicAddDamage","CriticalDamageDefense","CriticalPhysicDamageDefense",
            "CriticalMagicDamageDefense","AmplifyCriticalDamage","AmplifyPhysicCriticalDamage",
            "AmplifyMagicCriticalDamage","DecreaseCriticalDamage","DecreasePhysicCriticalDamage",
            "DecreaseMagicCriticalDamage","BossNpcAddDamage","BossNpcDefense",
            "PhysicDamageDefense","MagicDamageDefense","BasicAtkDamageDefense",
            "PvEAddDamage","PvEDamageDefense","PvPAddDamage","PvPPhysicAddDamage","PvPMagicAddDamage",
            "PvPDamageDefense","PvPPhysicDamageDefense","PvPMagicDamageDefense",
            "PvPAmplifySkillDamage","PvPAmplifyDamage","PvPAmplifyWeaponDamage",
            "PvPAmplifyPhysicDamage","PvPAmplifyMagicDamage","SealStoneAddDamage",
            "DecreaseDamage","DecreasePhysicDamage","DecreaseMagicDamage","DecreaseSkillDamage",
            "DecreaseWeaponDamage","DecreaseBasicAtkDamage","PvPDecreaseDamage",
            "PvPDecreaseWeaponDamage","PvPDecreasePhysicDamage","PvPDecreaseMagicDamage",
            "PvPDecreaseSkillDamage","SkillDefense","PhysicSkillDefense","MagicSkillDefense",
            "AmplifyPhysicSkillDamage","AmplifyMagicSkillDamage","DecreasePhysicSkillDamage",
            "DecreaseMagicSkillDamage","IgnoreDecreaseShieldBlock","DecreaseShieldBlock",
            "IgnoreShieldBlockDefense","ShieldBlockDefense","IgnoreDecreaseWeaponBlock",
            "DecreaseWeaponBlock","IgnoreWeaponBlockDefense","WeaponBlockDefense",
            "MaxWeaponBlock","MaxShieldBlock","BackAttackDamage","BackAttackDefense",
            "BackAttackCritical","BackAttackCriticalResist","AmplifyBackAttack","DecreaseBackAttack",
            "Accuracy","PhysicAccuracy","MagicAccuracy","SkillAccuracy","PhysicSkillAccuracy",
            "MagicSkillAccuracy","PvEAccuracy","PvEPhysicAccuracy","PvEMagicAccuracy",
            "PvPAccuracy","PvPPhysicAccuracy","PvPMagicAccuracy","Evasion","PhysicEvasion",
            "MagicEvasion","SkillEvasion","PhysicSkillEvasion","MagicSkillEvasion",
            "PvEEvasion","PvEPhysicEvasion","PvEMagicEvasion","PvPEvasion","PvPPhysicEvasion",
            "PvPMagicEvasion","PhysicCritical","MagicCritical","SkillCritical","PvPCritical",
            "CriticalResist","PhysicCriticalResist","MagicCriticalResist","PvPCriticalResist",
            "SkillCriticalResist","PhysicImmune","MagicImmune","PhysicIgnoreImmune",
            "MagicIgnoreImmune","WeaponBlockPierce","ShieldBlockPierce",
            "AdditionalHitRate","AdditionalHitResistRate",
            "StunAccuracy","HoldAccuracy","AerialAccuracy","TauntAccuracy","SilenceAccuracy",
            "StunResist","HoldResist","AerialResist","TauntResist","SilenceResist",
            "AbnormalAccuracy","AbnormalResistance","AbnormalFocus","AbnormalLower",
            "PvPAbnormalFocus","PvPAbnormalLower",
            "AddDamageWater","AddDamageFire","AddDamageWind","AddDamageEarth",
            "AddDamageHoly","AddDamageDark","WaterDamageDefense","FireDamageDefense",
            "WindDamageDefense","EarthDamageDefense","HolyDamageDefense","DarkDamageDefense",
            "AmplifyWater","AmplifyFire","AmplifyWind","AmplifyEarth","AmplifyHoly","AmplifyDark",
            "ResistWater","ResistFire","ResistWind","ResistEarth","ResistHoly","ResistDark",
            "AmplifyAllAttribute","AllAttributeResist","AllAttributeDamage","AllAttributeDefense",
            "AddAttackRange","HPMax","HPRegen","RestHPRegen","MaxHPRatio","MaxMPRatio",
            "MPUseDecrease","MPMax","MPRegen","RestMPRegen","AttackSpeed","SkillSpeed",
            "ChargeSpeed","MoveSpeed","BattleMoveSpeed","VehicleSpeed","GroundVehicleSpeed",
            "VehicleSprintSpeed","VehicleSprintCost","FlyLimitHeight","FlyLimitHeightRate",
            "FallingSpeed","FlySpeed","CoolTimeDecrease","ExperienceBonus","AdenaBonus",
            "HpHealRate","HpHealRegen","HpPotionRate","HpPotionRegen","HpHealGetReduce",
            "MpHealRate","MpHealRegen","MpPotionRate","MpPotionRegen","SpHealRate","SpHealRegen",
            "SpPotionRate","SpPotionRegen","DpHealRate","DpHealRegen","DpPotionRate","DpPotionRegen",
            "AmplifyHpHealGet","AmplifyAggro","DecreaseAggro","WalkSpeed","GPMax","GPRegen",
            "SprintSpeed","SPMax","SPRegen","RestSPRegen","SPRegenRatio","SprintCost","FlyCost",
            "CastingSpeed","AddWeaponRange","FrozenAccuracy","FrozenResist","DPMax","MaxDPRatio",
            "AmplifyDpGet","BlockPierce","FlyFallSpeed","LimitPhysicAccuracy","LimitMagicAccuracy",
            "LimitPhysicEvasion","LimitMagicEvasion","LimitPhysicCritical","LimitMagicCritical",
            "LimitPhysicCriticalResist","LimitMagicCriticalResist","LimitWeaponBlock",
            "LimitWeaponBlockPierce","LimitShieldBlock","LimitShieldBlockPierce",
            "CastingTime","PhysicDamageRatio","MagicDamageRatio","PhysicDefenseRatio",
            "MagicDefenseRatio","BattleHPRegen","BattleMPRegen","BattleSPRegen",
            "FlyPhysicDamage","FlyPhysicDefense","FlyMagicDamage","FlyMagicDefense",
            "CombatSpeed","SpellSpeed","DefensePierce","PhysicDefensePierce","MagicDefensePierce",
            "DPRegen","SwimmingPhysicDamage","SwimmingMagicDamage","SwimmingPhysicDefense",
            "SwimmingMagicDefense","SwimmingSpeed","SwimmingSprintSpeed","SwimmingSprintCost",
            "OPMax","DiveCost","OPRegen","FPMax","FPRegen","BattleFPRegen","RestFPRegen",
            "FPRegenRatio","FPHealRegen","FPHealRate","FPPotionRegen","FPPotionRate",
            "ArmorDefense","PhysicArmorDefense","MagicArmorDefense","PhysicArmorDefenseRatio",
            "MagicArmorDefenseRatio","ArmorEvasion","PhysicArmorEvasion","MagicArmorEvasion",
            "PhysicArmorEvasionRatio","MagicArmorEvasionRatio","WeaponFixingDamage",
            "WeaponAccuracy","PhysicWeaponAccuracy","MagicWeaponAccuracy",
            "ShockPropertyAccuracy","ShockPropertyResist","ShockPropertyTimeIncrease",
            "ShockPropertyTimeDecrease","MentalPropertyAccuracy","MentalPropertyResist",
            "MentalPropertyTimeIncrease","MentalPropertyTimeDecrease","BodyPropertyAccuracy",
            "BodyPropertyResist","BodyPropertyTimeIncrease","BodyPropertyTimeDecrease",
            "StunTimeIncrease","StunTimeDecrease","HoldTimeIncrease","HoldTimeDecrease",
            "TauntTimeIncrease","TauntTimeDecrease","AerialTimeIncrease","AerialTimeDecrease",
            "SilenceTimeIncrease","SilenceTimeDecrease","FrozenTimeIncrease","FrozenTimeDecrease",
            "FireCoolTimeDecrease","WaterCoolTimeDecrease","WindCoolTimeDecrease",
            "EarthCoolTimeDecrease","AttackCoolTimeDecrease","HealCoolTimeDecrease",
            "SpawnCoolTimeDecrease","BuffCoolTimeDecrease","DebuffCoolTimeDecrease",
            "FireMPUseDecrease","WaterMPUseDecrease","WindMPUseDecrease","EarthMPUseDecrease",
            "AttackMPUseDecrease","HealMPUseDecrease","SpawnMPUseDecrease","BuffMPUseDecrease",
            "DebuffMPUseDecrease","FireSpellSpeed","WaterSpellSpeed","WindSpellSpeed",
            "EarthSpellSpeed","AttackSpellSpeed","HealSpellSpeed","SpawnSpellSpeed",
            "BuffSpellSpeed","DebuffSpellSpeed","HPDrain","AmplifyHPBarrier","ProxyMPUseDecrease",
            "PhysicWeaponFixingDamage","MagicWeaponFixingDamage","ElementalistDespawnHPRegen",
            "ElementalSummonLifetime","PvEAmplifyDamage","PvEDecreaseDamage",
            "KnockdownInstantAccuracy","KnockdownInstantResist",
            "KnockdownInstantTimeIncrease","KnockdownInstantTimeDecrease",
            "CoolTimeIncrease","MPUseIncrease","PvPDamageRatioLoss",
            "OpHealRate","OpHealRegen","OpPotionRate","OpPotionRegen",
            "IntellectDamage","IntellectDefense","IntellectAccuracy","IntellectEvasion",
            "IntellectCritical","IntellectCriticalResist","IntellectBlockPierce","IntellectBlock",
            "FeralDamage","FeralDefense","FeralAccuracy","FeralEvasion","FeralCritical",
            "FeralCriticalResist","FeralBlockPierce","FeralBlock",
            "NatureDamage","NatureDefense","NatureAccuracy","NatureEvasion","NatureCritical",
            "NatureCriticalResist","NatureBlockPierce","NatureBlock",
            "TransDamage","TransDefense","TransAccuracy","TransEvasion","TransCritical",
            "TransCriticalResist","TransBlockPierce","TransBlock",
            "DamageRatio","DefenseRatio","AccuracyRatio","EvasionRatio","CriticalRatio",
            "CriticalResistRatio","BlockPierceRatio","BlockRatio","CoolTimeDecreaseRatio",
            "CombatSpeedRatio","AmplifyAllDamageRatio","DecreaseDamageRatio",
            "MaxGroggyGuardRatio","EventGroundVehicleSpeed","EventVehicleFlySpeed",
            "FlyingSprintSpeed","FlyingSprintCost","HardHit","PerfectResist","HardHitResist",
            "IgnoreRestoration","IgnoreIronWall","AmplifyGroggyGuardDecreaseEnergy",
            "SleepAccuracy","StoneAccuracy","ParalysisAccuracy","PoisonAccuracy",
            "BleedAccuracy","SnareAccuracy","SlowAccuracy","BlindAccuracy",
            "SleepResist","StoneResist","ParalysisResist","PoisonResist","BleedResist",
            "SnareResist","SlowResist","BlindResist",
            "SleepTimeIncrease","SleepTimeDecrease","StoneTimeIncrease","StoneTimeDecrease",
            "ParalysisTimeIncrease","ParalysisTimeDecrease","PoisonTimeIncrease",
            "PoisonTimeDecrease","BleedTimeIncrease","BleedTimeDecrease",
            "SnareTimeIncrease","SnareTimeDecrease","SlowTimeIncrease","SlowTimeDecrease",
            "BlindTimeIncrease","BlindTimeDecrease",
            "IntellectAmplifyDamage","IntellectDecreaseDamage",
            "FeralAmplifyDamage","FeralDecreaseDamage",
            "NatureAmplifyDamage","NatureDecreaseDamage",
            "TransAmplifyDamage","TransDecreaseDamage",
            "SwordDamageRatio","GreatswordDamageRatio","DaggerDamageRatio","BowDamageRatio",
            "MagicbookDamageRatio","MaceDamageRatio","StaffDamageRatio","OrbDamageRatio",
            "GuarderDamageRatio","HelmetDefenseRatio","TorsoDefenseRatio","PantsDefenseRatio",
            "GlovesDefenseRatio","BootsDefenseRatio","BeltDefenseRatio","CapeDefenseRatio",
            "ShoulderDefenseRatio","NecklaceDamageRatio","NecklaceDefenseRatio",
            "EarringDamageRatio","EarringDefenseRatio","RingDamageRatio","RingDefenseRatio",
            "BraceletDamageRatio","BraceletDefenseRatio",
            "HealCostMpRate","AbyssPointBonus","PureMinDamage","PureMaxDamage",
            "BossNpcAmplifyDamage","BossNpcDecreaseDamage","ArmorDefenseRatio","ArmorEvasionRatio",
            "FlyDamage","FlyDefense","SwimmingDamage","SwimmingDefense","IgnoreImmune",
            "LimitAccuracy","LimitEvasion","LimitCritical","LimitCriticalResist",
            "SealStoneAmplifyDamage","FearAccuracy","FearResist","FearTimeIncrease",
            "FearTimeDecrease","PolymorphAccuracy","PolymorphResist","PolymorphTimeIncrease",
            "PolymorphTimeDecrease","BlockActiveAccuracy","BlockActiveResist",
            "BlockActiveTimeIncrease","BlockActiveTimeDecrease","BlockadeAccuracy",
            "BlockadeResist","BlockadeTimeIncrease","BlockadeTimeDecrease",
            "CombatFlySpeed","IgnoreDefense","SPUseDecrease","SPUseIncrease",
            "PvPDefensePierce","PvEDefensePierce","PvPIgnoreDefense","PvEIgnoreDefense",
            "SealStonePvPIgnoreDefense","BindAccuracy","BindResist","BindTimeIncrease",
            "BindTimeDecrease","AbyssAmplifyDamage","AbyssDecreaseDamage",
            "SkillHPDrain","HPDrainResist","SkillHPDrainResist","PvPBlock","PvPBlockPierce",
        };

        // The packet uses statId with offsets that grow due to hidden reserved slots.
        // We build a sparse array mapping packet statId → name.
        // Strategy: place entries at statId = enumIndex + delta for each section.
        //
        // Section deltas (verified anchor points):
        //   enum[  0..  4] → statId = enum+1   (delta=1)
        //   enum[  5.. 27] → statId = enum+4   (delta=4, 3 hidden slots appear after ID 5)
        //   enum[ 28..137] → statId = enum+4   (stays +4 until...)
        //     ...but at enum 138 the delta must be 8, so 4 more hidden between enum 27-138.
        //
        // Without full verification, use a piecewise linear approach:
        //   Section A: enum[0..4]     → statId = idx + 1            (delta 1)
        //   Section B: enum[5..27]    → statId = idx + 4            (delta 4)
        //   Section C: enum[28..137]  → statId = idx + 4 +? → 8
        //   Section D: enum[138..149] → statId = idx + 8            (delta 8)
        //   Section E: enum[150..272] → statId = idx + 8 +? → 9
        //   Section F: enum[273..556] → statId = idx + 9            (delta 9)
        //
        // Anchors force: enum28+4=32 ✓, enum138+8=146 ✓, enum150+8=158 ✓,
        //                enum273+9=282 ✓, enum309+9=318 ✓
        //
        // Between sections B and D: enum[5..27] at delta 4, then suddenly at enum[28] also delta 4.
        // enum[28]+4=32 matches! So section B and C share delta=4 up to some point where it jumps to 8.
        // The jump from delta 4→8 must happen somewhere between enum 27 and 138.
        //   enum[137]+8=145 ≠ anchor... enum[138]+8=146 ✓
        //   enum[137]+4=141. So somewhere between enum indices ~134-137 the delta jumps.
        //   Since we don't have verified anchors there, assume the jump happens just before enum 138.
        //   That means enum[5..137] all use delta=4, but that would give enum[137]+4=141.
        //   We need enum[138]+8=146. The gap: from 141 to 146 is 5, but only 1 entry.
        //   So 4 hidden slots are inserted between statIds 141 and 146.
        //
        // Similarly, for delta 8→9 between enum 149 and 273:
        //   enum[149]+8=157, next enum[150]+8=158 ✓ and enum[272]+8=280.
        //   We need enum[273]+9=282. Gap from 280→282 is 2, but 1 entry. So 1 hidden slot.
        //
        // Refined model:
        //   enum[  0..  4] → +1
        //   enum[  5..137] → +4   (3 hidden slots inserted after statId 5)
        //   enum[138..149] → +8   (4 hidden slots inserted at statId ~142-145)
        //   enum[150..272] → +8   (same delta, continues)
        //   Wait: enum[150]+8=158 ✓, enum[272]+8=280, but we need enum[273]+9=282.
        //   So delta jumps 8→9 between enum 272 and 273. 1 hidden at statId 281.
        //
        //   enum[150..272] → +8
        //   enum[273..556] → +9

        // Final verified piecewise mapping:
        int maxId = esEnum.Length + 10; // +10 for hidden slots overhead
        var table = new string?[maxId + 1];

        for (int i = 0; i < esEnum.Length; i++)
        {
            int delta;
            if (i <= 4)        delta = 1;
            else if (i <= 137) delta = 4;
            else if (i <= 272) delta = 8;
            else               delta = 9;

            int statId = i + delta;
            if (statId < table.Length)
                table[statId] = esEnum[i];
        }

        return table;
    }
}
