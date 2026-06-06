using System;
using System.Runtime.InteropServices;
using System.Text;

namespace A2Meter.Dps.Protocol;

/// Wraps the native PacketEngine.dll for high-fidelity packet parsing.
/// Falls back to the C# PacketDispatcher when the DLL is not available.
/// The native engine handles more packet format variants, resulting in
/// fewer missed damage events (~9% improvement over C#-only parsing).
internal sealed unsafe class NativePacketEngine : IDisposable
{
    private const string DLL = "PacketEngine";

    // ─── P/Invoke declarations ──────────────────────────────────────────

    [DllImport(DLL)] private static extern int PE_Init();
    [DllImport(DLL)] private static extern void PE_Shutdown(int handle);
    [DllImport(DLL)] private static extern void PE_Dispatch(int handle, byte* data, int length);
    [DllImport(DLL)] private static extern void PE_SetDumpUnparsed(int handle, int enabled);
    [DllImport(DLL)] private static extern void PE_SetOnDamage(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetOnMobSpawn(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetOnSummon(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetOnUserInfo(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetOnCombatPower(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetOnCombatPowerByName(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetOnEntityRemoved(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetOnBossHp(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetOnBuff(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetOnBuffRefresh(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetOnCombatState(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetOnRemainHP(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetOnNpcGroggy(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetOnTargetOn(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetOnTargetOff(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetOnZoneMove(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetOnPartyEvent(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetOnLog(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetResolveSkill(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetIsMobBoss(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetGetSkillName(int handle, nint ctx, nint cb);
    [DllImport(DLL)] private static extern void PE_SetIsKnownBuffCode(int handle, nint ctx, nint cb);

    // ─── Events (same signature as PacketDispatcher) ────────────────────

    public event Action<int, int, int, byte, int, uint, int, int, int, int>? Damage;
    public event Action<int, int, int, int>? MobSpawn;
    public event Action<int, int>? Summon;
    public event Action<int, string, int, int, int>? UserInfo;
    public event Action<int, int>? CombatPower;
    public event Action<string, int, int>? CombatPowerByName;
    public event Action<int>? EntityRemoved;
    public event Action<int, int>? BossHp;
    public event Action<int, int, int, uint, long, int>? Buff;
    public event Action<int, int, uint, long, int>? BuffRefresh;
    public event Action<int, int>? CombatState;
    public event Action<int, uint>? RemainHp;
    public event Action<int, uint, uint, int>? NpcGroggy;
    public event Action<int, int, int>? TargetOn;
    public event Action<int, int>? TargetOff;
    public event Action<uint>? ZoneMove;
    public event Action<int, int, int, uint, int, string, int, int, int, int>? PartyEvent;

    // ─── State ──────────────────────────────────────────────────────────

    private int _handle;
    private GCHandle _gcHandle;
    private readonly SkillDatabase _skillDb;
    private readonly Action<string>? _logSink;
    private bool _disposed;

    public bool IsAvailable => _handle > 0;

    private NativePacketEngine(SkillDatabase skillDb, Action<string>? logSink)
    {
        _skillDb = skillDb;
        _logSink = logSink;
    }

    /// Attempts to create and initialize the native engine.
    /// Returns null if the DLL is not found or initialization fails.
    public static NativePacketEngine? TryCreate(SkillDatabase skillDb, Action<string>? logSink = null)
    {
        try
        {
            var engine = new NativePacketEngine(skillDb, logSink);
            engine._handle = PE_Init();
            if (engine._handle <= 0)
            {
                logSink?.Invoke("[NativeEngine] PE_Init returned invalid handle");
                return null;
            }

            engine._gcHandle = GCHandle.Alloc(engine);
            nint ctx = GCHandle.ToIntPtr(engine._gcHandle);

            PE_SetOnDamage(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, int, int, int, int, int, uint, int, int, int, int, void>)(&OnDamage));
            PE_SetOnMobSpawn(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, int, int, int, int, void>)(&OnMobSpawn));
            PE_SetOnSummon(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, int, int, void>)(&OnSummon));
            PE_SetOnUserInfo(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, int, nint, int, int, int, int, void>)(&OnUserInfo));
            PE_SetOnCombatPower(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, int, int, void>)(&OnCombatPower));
            PE_SetOnCombatPowerByName(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, nint, int, int, int, void>)(&OnCombatPowerByName));
            PE_SetOnEntityRemoved(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, int, void>)(&OnEntityRemoved));
            PE_SetOnBossHp(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, int, int, void>)(&OnBossHp));
            PE_SetOnBuff(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, int, int, int, uint, long, int, void>)(&OnBuff));
            PE_SetOnBuffRefresh(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, int, int, uint, long, int, void>)(&OnBuffRefresh));
            PE_SetOnCombatState(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, int, int, void>)(&OnCombatState));
            PE_SetOnRemainHP(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, int, uint, void>)(&OnRemainHp));
            PE_SetOnNpcGroggy(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, int, uint, uint, int, void>)(&OnNpcGroggy));
            PE_SetOnTargetOn(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, int, int, int, void>)(&OnTargetOn));
            PE_SetOnTargetOff(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, int, int, void>)(&OnTargetOff));
            PE_SetOnZoneMove(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, uint, void>)(&OnZoneMove));
            PE_SetOnPartyEvent(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, int, int, int, uint, int, nint, int, int, int, int, int, void>)(&OnPartyEvent));
            PE_SetOnLog(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, int, nint, int, void>)(&OnLog));
            PE_SetResolveSkill(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, nint, int, int, int*, int*, int>)(&OnResolveSkill));
            PE_SetIsMobBoss(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, int, int>)(&OnIsMobBoss));
            PE_SetGetSkillName(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, int, nint, int, int>)(&OnGetSkillName));
            PE_SetIsKnownBuffCode(engine._handle, ctx,
                (nint)(delegate* unmanaged<nint, int, int>)(&OnIsKnownBuffCode));

            logSink?.Invoke("[NativeEngine] initialized successfully");
            return engine;
        }
        catch (DllNotFoundException)
        {
            logSink?.Invoke("[NativeEngine] PacketEngine.dll not found, using C# fallback");
            return null;
        }
        catch (Exception ex)
        {
            logSink?.Invoke($"[NativeEngine] init failed: {ex.Message}");
            return null;
        }
    }

    public void Dispatch(byte[] data, int offset, int length)
    {
        if (_handle <= 0 || _disposed) return;
        int len = Math.Min(length, data.Length - offset);
        if (len <= 0) return;
        fixed (byte* ptr = &data[offset])
        {
            PE_Dispatch(_handle, ptr, len);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_handle > 0)
        {
            PE_Shutdown(_handle);
            _handle = 0;
        }
        if (_gcHandle.IsAllocated) _gcHandle.Free();
    }

    // ─── Helpers ────────────────────────────────────────────────────────

    private static NativePacketEngine? Resolve(nint ctx)
    {
        try { return (NativePacketEngine)GCHandle.FromIntPtr(ctx).Target!; }
        catch { return null; }
    }

    // ─── Unmanaged callbacks ────────────────────────────────────────────

    [UnmanagedCallersOnly]
    private static void OnDamage(nint ctx, int actorId, int targetId, int skillCode,
        int damageType, int damage, uint specialFlags, int multiHitCount,
        int multiHitDamage, int healAmount, int isDot)
    {
        Resolve(ctx)?.Damage?.Invoke(actorId, targetId, skillCode, (byte)damageType,
            damage, specialFlags, multiHitCount, multiHitDamage, healAmount, isDot);
    }

    [UnmanagedCallersOnly]
    private static void OnMobSpawn(nint ctx, int mobId, int mobCode, int hp, int isBoss)
    {
        Resolve(ctx)?.MobSpawn?.Invoke(mobId, mobCode, hp, isBoss);
    }

    [UnmanagedCallersOnly]
    private static void OnSummon(nint ctx, int actorId, int petId)
    {
        Resolve(ctx)?.Summon?.Invoke(actorId, petId);
    }

    [UnmanagedCallersOnly]
    private static void OnUserInfo(nint ctx, int entityId, nint nickPtr, int nickLen,
        int serverId, int jobCode, int isSelf)
    {
        var engine = Resolve(ctx);
        if (engine == null) return;
        string nick = Encoding.UTF8.GetString((byte*)nickPtr, nickLen);
        engine.UserInfo?.Invoke(entityId, nick, serverId, jobCode, isSelf);
    }

    [UnmanagedCallersOnly]
    private static void OnCombatPower(nint ctx, int entityId, int cp)
    {
        Resolve(ctx)?.CombatPower?.Invoke(entityId, cp);
    }

    [UnmanagedCallersOnly]
    private static void OnCombatPowerByName(nint ctx, nint nickPtr, int nickLen, int serverId, int cp)
    {
        var engine = Resolve(ctx);
        if (engine == null) return;
        string nick = Encoding.UTF8.GetString((byte*)nickPtr, nickLen);
        engine.CombatPowerByName?.Invoke(nick, serverId, cp);
    }

    [UnmanagedCallersOnly]
    private static void OnEntityRemoved(nint ctx, int entityId)
    {
        Resolve(ctx)?.EntityRemoved?.Invoke(entityId);
    }

    [UnmanagedCallersOnly]
    private static void OnBossHp(nint ctx, int entityId, int hp)
    {
        Resolve(ctx)?.BossHp?.Invoke(entityId, hp);
    }

    [UnmanagedCallersOnly]
    private static void OnBuff(nint ctx, int entityId, int buffId, int type,
        uint durationMs, long timestamp, int casterId)
    {
        Resolve(ctx)?.Buff?.Invoke(entityId, buffId, type, durationMs, timestamp, casterId);
    }

    [UnmanagedCallersOnly]
    private static void OnBuffRefresh(nint ctx, int entityId, int buffId,
        uint durationMs, long timestamp, int casterId)
    {
        Resolve(ctx)?.BuffRefresh?.Invoke(entityId, buffId, durationMs, timestamp, casterId);
    }

    [UnmanagedCallersOnly]
    private static void OnCombatState(nint ctx, int entityId, int state)
    {
        Resolve(ctx)?.CombatState?.Invoke(entityId, state);
    }

    [UnmanagedCallersOnly]
    private static void OnRemainHp(nint ctx, int targetId, uint remainHp)
    {
        Resolve(ctx)?.RemainHp?.Invoke(targetId, remainHp);
    }

    [UnmanagedCallersOnly]
    private static void OnNpcGroggy(nint ctx, int targetId, uint maxGroggy,
        uint currentGroggy, int groggyStatus)
    {
        Resolve(ctx)?.NpcGroggy?.Invoke(targetId, maxGroggy, currentGroggy, groggyStatus);
    }

    [UnmanagedCallersOnly]
    private static void OnTargetOn(nint ctx, int targetId, int aggroId, int targetingMode)
    {
        Resolve(ctx)?.TargetOn?.Invoke(targetId, aggroId, targetingMode);
    }

    [UnmanagedCallersOnly]
    private static void OnTargetOff(nint ctx, int targetId, int offMode)
    {
        Resolve(ctx)?.TargetOff?.Invoke(targetId, offMode);
    }

    [UnmanagedCallersOnly]
    private static void OnZoneMove(nint ctx, uint zoneId)
    {
        Resolve(ctx)?.ZoneMove?.Invoke(zoneId);
    }

    [UnmanagedCallersOnly]
    private static void OnPartyEvent(nint ctx, int eventType, int memberIndex, int totalMembers,
        uint characterId, int serverId, nint nickPtr, int nickLen, int jobCode, int level,
        int combatPower, int itemLevel)
    {
        var engine = Resolve(ctx);
        if (engine == null) return;
        string nick = nickLen > 0 ? Encoding.UTF8.GetString((byte*)nickPtr, nickLen) : "";
        engine.PartyEvent?.Invoke(eventType, memberIndex, totalMembers, characterId,
            serverId, nick, jobCode, level, combatPower, itemLevel);
    }

    [UnmanagedCallersOnly]
    private static void OnLog(nint ctx, int level, nint msgPtr, int msgLen)
    {
        var engine = Resolve(ctx);
        if (engine?._logSink == null) return;
        string msg = Encoding.UTF8.GetString((byte*)msgPtr, msgLen);
        engine._logSink($"[PE:{level}] {msg}");
    }

    [UnmanagedCallersOnly]
    private static int OnResolveSkill(nint ctx, nint dataPtr, int pos, int end,
        int* outPos, int* outRawCode)
    {
        var engine = Resolve(ctx);
        if (engine == null)
        {
            *outPos = pos;
            *outRawCode = 0;
            return 0;
        }
        byte[] buf = new byte[end];
        new ReadOnlySpan<byte>((void*)dataPtr, end).CopyTo(buf);
        int p = pos;
        int result = engine._skillDb.ResolveFromPacketBytes(buf, ref p, end);
        *outPos = p;
        *outRawCode = engine._skillDb.LastRawSkillCode;
        return result;
    }

    [UnmanagedCallersOnly]
    private static int OnIsMobBoss(nint ctx, int mobCode)
    {
        var engine = Resolve(ctx);
        return (engine != null && engine._skillDb.IsMobBoss(mobCode)) ? 1 : 0;
    }

    [UnmanagedCallersOnly]
    private static int OnGetSkillName(nint ctx, int skillCode, nint bufPtr, int bufSize)
    {
        var engine = Resolve(ctx);
        string? name = engine?._skillDb.GetSkillName(skillCode);
        if (string.IsNullOrEmpty(name)) return 0;
        byte[] bytes = Encoding.UTF8.GetBytes(name);
        int n = Math.Min(bytes.Length, bufSize);
        new ReadOnlySpan<byte>(bytes, 0, n).CopyTo(new Span<byte>((void*)bufPtr, n));
        return n;
    }

    [UnmanagedCallersOnly]
    private static int OnIsKnownBuffCode(nint ctx, int buffId)
    {
        var engine = Resolve(ctx);
        if (engine == null) return 1;
        if (engine._skillDb.IsKnownBuffCode(buffId)) return 1;
        if (engine._skillDb.GetSkillName(buffId) != null) return 1;
        if ((uint)buffId >= 100000000u && (uint)buffId <= 999999999u)
        {
            int half = (int)((uint)buffId / 10u);
            if (engine._skillDb.GetSkillName(half) != null) return 1;
            int baseCode = half / 10000 * 10000;
            if (engine._skillDb.GetSkillName(baseCode) != null) return 1;
        }
        return 0;
    }
}
