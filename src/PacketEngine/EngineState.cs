using System;
using System.Collections.Concurrent;
using System.Threading;

namespace PacketEngine;

/// Per-handle state for a PE (Packet Engine) instance.
/// Stores the registered callback function pointers and context pointer.
internal sealed unsafe class EngineState
{
    private static int _nextHandle;
    private static readonly ConcurrentDictionary<int, EngineState> _instances = new();

    public int Handle { get; }
    public nint Ctx;

    // ── Output callbacks ──────────────────────────────────────────────
    public delegate* unmanaged<nint, int, int, int, int, int, uint, int, int, int, int, void> OnDamage;
    public delegate* unmanaged<nint, int, int, int, int, void> OnMobSpawn;
    public delegate* unmanaged<nint, int, int, void> OnSummon;
    public delegate* unmanaged<nint, int, nint, int, int, int, int, void> OnUserInfo;
    public delegate* unmanaged<nint, int, int, void> OnCombatPower;
    public delegate* unmanaged<nint, nint, int, int, int, void> OnCombatPowerByName;
    public delegate* unmanaged<nint, int, void> OnEntityRemoved;
    public delegate* unmanaged<nint, int, int, void> OnBossHp;
    public delegate* unmanaged<nint, int, int, int, uint, long, int, void> OnBuff;
    public delegate* unmanaged<nint, int, int, uint, long, int, void> OnBuffRefresh;
    public delegate* unmanaged<nint, int, int, void> OnCombatState;
    public delegate* unmanaged<nint, int, uint, void> OnRemainHp;
    public delegate* unmanaged<nint, int, uint, uint, int, void> OnNpcGroggy;
    public delegate* unmanaged<nint, int, int, int, void> OnTargetOn;
    public delegate* unmanaged<nint, int, int, void> OnTargetOff;
    public delegate* unmanaged<nint, uint, void> OnZoneMove;
    public delegate* unmanaged<nint, int, int, int, uint, int, nint, int, int, int, int, int, void> OnPartyEvent;
    public delegate* unmanaged<nint, int, nint, int, void> OnLog;

    // ── Query callbacks (called back into managed code) ───────────────
    public delegate* unmanaged<nint, nint, int, int, int*, int*, int> ResolveSkill;
    public delegate* unmanaged<nint, int, int> IsMobBoss;
    public delegate* unmanaged<nint, int, nint, int, int> GetSkillName;
    public delegate* unmanaged<nint, int, int> IsKnownBuffCode;

    // ── Internal state ────────────────────────────────────────────────
    public bool DumpUnparsed;

    private EngineState(int handle) => Handle = handle;

    public static EngineState Create()
    {
        int h = Interlocked.Increment(ref _nextHandle);
        var state = new EngineState(h);
        _instances[h] = state;
        return state;
    }

    public static EngineState? Get(int handle)
        => _instances.TryGetValue(handle, out var s) ? s : null;

    public static void Remove(int handle)
        => _instances.TryRemove(handle, out _);

    // ── Callback invokers ─────────────────────────────────────────────

    public void FireDamage(int actorId, int targetId, int skillCode, int damageType,
        int damage, uint specialFlags, int multiHitCount, int multiHitDamage,
        int healAmount, int isDot)
    {
        if (OnDamage != null)
            OnDamage(Ctx, actorId, targetId, skillCode, damageType, damage,
                     specialFlags, multiHitCount, multiHitDamage, healAmount, isDot);
    }

    public void FireMobSpawn(int mobId, int mobCode, int hp, int isBoss)
    {
        if (OnMobSpawn != null)
            OnMobSpawn(Ctx, mobId, mobCode, hp, isBoss);
    }

    public void FireSummon(int actorId, int petId)
    {
        if (OnSummon != null)
            OnSummon(Ctx, actorId, petId);
    }

    public void FireUserInfo(int entityId, string nickname, int serverId, int jobCode, int isSelf)
    {
        if (OnUserInfo != null)
        {
            fixed (byte* ptr = System.Text.Encoding.UTF8.GetBytes(nickname))
            {
                int len = System.Text.Encoding.UTF8.GetByteCount(nickname);
                OnUserInfo(Ctx, entityId, (nint)ptr, len, serverId, jobCode, isSelf);
            }
        }
    }

    public void FireCombatPower(int entityId, int cp)
    {
        if (OnCombatPower != null)
            OnCombatPower(Ctx, entityId, cp);
    }

    public void FireCombatPowerByName(string nickname, int serverId, int cp)
    {
        if (OnCombatPowerByName != null)
        {
            fixed (byte* ptr = System.Text.Encoding.UTF8.GetBytes(nickname))
            {
                int len = System.Text.Encoding.UTF8.GetByteCount(nickname);
                OnCombatPowerByName(Ctx, (nint)ptr, len, serverId, cp);
            }
        }
    }

    public void FireEntityRemoved(int entityId)
    {
        if (OnEntityRemoved != null)
            OnEntityRemoved(Ctx, entityId);
        FireTargetOff(entityId, 0);
        FireCombatState(entityId, 0);
    }

    public void FireBossHp(int entityId, int hp)
    {
        if (OnBossHp != null)
            OnBossHp(Ctx, entityId, hp);
        if (hp >= 0)
            FireRemainHp(entityId, (uint)hp);
    }

    public void FireBuff(int entityId, int buffId, int type, uint durationMs, long timestamp, int casterId)
    {
        if (OnBuff != null)
            OnBuff(Ctx, entityId, buffId, type, durationMs, timestamp, casterId);
    }

    public void FireBuffRefresh(int entityId, int buffId, uint durationMs, long timestamp, int casterId)
    {
        if (OnBuffRefresh != null)
            OnBuffRefresh(Ctx, entityId, buffId, durationMs, timestamp, casterId);
    }

    public void FireCombatState(int entityId, int state)
    {
        if (OnCombatState != null)
            OnCombatState(Ctx, entityId, state);
    }

    public void FireRemainHp(int targetId, uint remainHp)
    {
        if (OnRemainHp != null)
            OnRemainHp(Ctx, targetId, remainHp);
    }

    public void FireNpcGroggy(int targetId, uint maxGroggy, uint currentGroggy, int groggyStatus)
    {
        if (OnNpcGroggy != null)
            OnNpcGroggy(Ctx, targetId, maxGroggy, currentGroggy, groggyStatus);
    }

    public void FireTargetOn(int targetId, int aggroId, int targetingMode)
    {
        if (OnTargetOn != null)
            OnTargetOn(Ctx, targetId, aggroId, targetingMode);
    }

    public void FireTargetOff(int targetId, int offMode)
    {
        if (OnTargetOff != null)
            OnTargetOff(Ctx, targetId, offMode);
    }

    public void FireZoneMove(uint zoneId)
    {
        if (OnZoneMove != null)
            OnZoneMove(Ctx, zoneId);
    }

    public void FirePartyEvent(int eventType, int memberIndex, int totalMembers, uint characterId,
        int serverId, string nickname, int jobCode, int level, int combatPower, int itemLevel)
    {
        if (OnPartyEvent == null) return;
        byte[] nickBytes = System.Text.Encoding.UTF8.GetBytes(nickname);
        fixed (byte* nickPtr = nickBytes)
        {
            OnPartyEvent(Ctx, eventType, memberIndex, totalMembers, characterId, serverId,
                         (nint)nickPtr, nickBytes.Length, jobCode, level, combatPower, itemLevel);
        }
    }

    public void FireLog(int level, string message)
    {
        if (OnLog != null)
        {
            fixed (byte* ptr = System.Text.Encoding.UTF8.GetBytes(message))
            {
                int len = System.Text.Encoding.UTF8.GetByteCount(message);
                OnLog(Ctx, level, (nint)ptr, len);
            }
        }
    }

    // ── Query callback invokers ───────────────────────────────────────

    public int QueryResolveSkill(byte[] data, ref int pos, int end, out int rawCode)
    {
        rawCode = 0;
        if (ResolveSkill == null) return 0;
        int outPos = pos;
        int outRaw = 0;
        int result;
        fixed (byte* ptr = data)
        {
            result = ResolveSkill(Ctx, (nint)ptr, pos, end, &outPos, &outRaw);
        }
        pos = outPos;
        rawCode = outRaw;
        return result;
    }

    public bool QueryIsMobBoss(int mobCode)
    {
        if (IsMobBoss == null) return false;
        return IsMobBoss(Ctx, mobCode) != 0;
    }

    public string? QueryGetSkillName(int skillCode)
    {
        if (GetSkillName == null) return null;
        byte* buf = stackalloc byte[512];
        int len = GetSkillName(Ctx, skillCode, (nint)buf, 512);
        if (len <= 0) return null;
        return System.Text.Encoding.UTF8.GetString(buf, len);
    }

    public bool QueryIsKnownBuffCode(int buffId)
    {
        if (IsKnownBuffCode == null) return true;
        return IsKnownBuffCode(Ctx, buffId) != 0;
    }
}

/// Per-handle state for a PP (Party Parser) instance.
internal sealed unsafe class PartyState
{
    private static int _nextHandle;
    private static readonly ConcurrentDictionary<int, PartyState> _instances = new();

    public int Handle { get; }
    public nint Ctx;

    // ── Output callbacks ──────────────────────────────────────────────
    public delegate* unmanaged<nint, int, uint, int, nint, int, int, nint, int, int, int, void> OnMember;
    public delegate* unmanaged<nint, int, int, void> OnBatchStart;
    public delegate* unmanaged<nint, int, void> OnBatchEnd;
    public delegate* unmanaged<nint, void> OnPartyLeft;
    public delegate* unmanaged<nint, void> OnPartyEjected;
    public delegate* unmanaged<nint, int, int, void> OnDungeonDetected;
    public delegate* unmanaged<nint, nint, int, int, int, void> OnCombatPowerDetected;

    // ── Internal state: the parser ────────────────────────────────────
    public PartyParser? Parser;

    private PartyState(int handle) => Handle = handle;

    public static PartyState Create()
    {
        int h = Interlocked.Increment(ref _nextHandle);
        var state = new PartyState(h);
        _instances[h] = state;
        return state;
    }

    public static PartyState? Get(int handle)
        => _instances.TryGetValue(handle, out var s) ? s : null;

    public static void Remove(int handle)
        => _instances.TryRemove(handle, out _);

    // ── Callback invokers ─────────────────────────────────────────────

    public void FireMember(int eventType, uint charId, int serverId, string nickname,
        int jobCode, string jobName, int level, int cp)
    {
        if (OnMember == null) return;
        byte[] nickBytes = System.Text.Encoding.UTF8.GetBytes(nickname);
        byte[] jobBytes  = System.Text.Encoding.UTF8.GetBytes(jobName);
        fixed (byte* nickPtr = nickBytes)
        fixed (byte* jobPtr  = jobBytes)
        {
            OnMember(Ctx, eventType, charId, serverId,
                     (nint)nickPtr, nickBytes.Length,
                     jobCode,
                     (nint)jobPtr, jobBytes.Length,
                     level, cp);
        }
    }

    public void FireBatchStart(int eventType, int count)
    {
        if (OnBatchStart != null)
            OnBatchStart(Ctx, eventType, count);
    }

    public void FireBatchEnd(int eventType)
    {
        if (OnBatchEnd != null)
            OnBatchEnd(Ctx, eventType);
    }

    public void FirePartyLeft()
    {
        if (OnPartyLeft != null) OnPartyLeft(Ctx);
    }

    public void FirePartyEjected()
    {
        if (OnPartyEjected != null) OnPartyEjected(Ctx);
    }

    public void FireDungeonDetected(int dungeonId, int stage)
    {
        if (OnDungeonDetected != null)
            OnDungeonDetected(Ctx, dungeonId, stage);
    }

    public void FireCombatPowerDetected(string nickname, int serverId, int cp)
    {
        if (OnCombatPowerDetected == null) return;
        byte[] nickBytes = System.Text.Encoding.UTF8.GetBytes(nickname);
        fixed (byte* nickPtr = nickBytes)
        {
            OnCombatPowerDetected(Ctx, (nint)nickPtr, nickBytes.Length, serverId, cp);
        }
    }
}
