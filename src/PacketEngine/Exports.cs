using System;
using System.Runtime.InteropServices;

namespace PacketEngine;

/// All PE_* and PP_* exports as [UnmanagedCallersOnly] entry points.
/// API surface matches A2Power's PacketEngineInterop.cs exactly.
internal static unsafe class Exports
{
    // ═══════════════════════════════════════════════════════════════════
    //  PE (Packet Engine) — combat parsing
    // ═══════════════════════════════════════════════════════════════════

    [UnmanagedCallersOnly(EntryPoint = "PE_Init")]
    public static int PE_Init()
    {
        var state = EngineState.Create();
        return state.Handle;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_Shutdown")]
    public static void PE_Shutdown(int handle)
    {
        EngineState.Remove(handle);
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_Dispatch")]
    public static void PE_Dispatch(int handle, byte* data, int length)
        => DispatchInternal(handle, data, length);

    [UnmanagedCallersOnly(EntryPoint = "PE_ScanRawCombatPower")]
    public static void PE_ScanRawCombatPower(int handle, byte* data, int length)
        => DispatchInternal(handle, data, length);

    private static void DispatchInternal(int handle, byte* data, int length)
    {
        var state = EngineState.Get(handle);
        if (state == null || length <= 0) return;

        byte[] buf = new byte[length];
        new ReadOnlySpan<byte>(data, length).CopyTo(buf);
        var dispatcher = new Dispatcher(state);
        dispatcher.Dispatch(buf, 0, length);
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetDumpUnparsed")]
    public static void PE_SetDumpUnparsed(int handle, int enabled)
    {
        var state = EngineState.Get(handle);
        if (state != null) state.DumpUnparsed = enabled != 0;
    }

    // ── Output callback setters ─────────────────────────────────────

    [UnmanagedCallersOnly(EntryPoint = "PE_SetOnDamage")]
    public static void PE_SetOnDamage(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnDamage = (delegate* unmanaged<nint, int, int, int, int, int, uint, int, int, int, int, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetOnMobSpawn")]
    public static void PE_SetOnMobSpawn(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnMobSpawn = (delegate* unmanaged<nint, int, int, int, int, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetOnSummon")]
    public static void PE_SetOnSummon(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnSummon = (delegate* unmanaged<nint, int, int, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetOnUserInfo")]
    public static void PE_SetOnUserInfo(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnUserInfo = (delegate* unmanaged<nint, int, nint, int, int, int, int, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetOnCombatPower")]
    public static void PE_SetOnCombatPower(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnCombatPower = (delegate* unmanaged<nint, int, int, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetOnCombatPowerByName")]
    public static void PE_SetOnCombatPowerByName(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnCombatPowerByName = (delegate* unmanaged<nint, nint, int, int, int, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetOnEntityRemoved")]
    public static void PE_SetOnEntityRemoved(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnEntityRemoved = (delegate* unmanaged<nint, int, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetOnBossHp")]
    public static void PE_SetOnBossHp(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnBossHp = (delegate* unmanaged<nint, int, int, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetOnBuff")]
    public static void PE_SetOnBuff(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnBuff = (delegate* unmanaged<nint, int, int, int, uint, long, int, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetOnBuffRefresh")]
    public static void PE_SetOnBuffRefresh(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnBuffRefresh = (delegate* unmanaged<nint, int, int, uint, long, int, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetOnCombatState")]
    public static void PE_SetOnCombatState(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnCombatState = (delegate* unmanaged<nint, int, int, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetOnRemainHP")]
    public static void PE_SetOnRemainHP(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnRemainHp = (delegate* unmanaged<nint, int, uint, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetOnNpcGroggy")]
    public static void PE_SetOnNpcGroggy(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnNpcGroggy = (delegate* unmanaged<nint, int, uint, uint, int, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetOnTargetOn")]
    public static void PE_SetOnTargetOn(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnTargetOn = (delegate* unmanaged<nint, int, int, int, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetOnTargetOff")]
    public static void PE_SetOnTargetOff(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnTargetOff = (delegate* unmanaged<nint, int, int, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetOnZoneMove")]
    public static void PE_SetOnZoneMove(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnZoneMove = (delegate* unmanaged<nint, uint, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetOnPartyEvent")]
    public static void PE_SetOnPartyEvent(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnPartyEvent = (delegate* unmanaged<nint, int, int, int, uint, int, nint, int, int, int, int, int, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetOnLog")]
    public static void PE_SetOnLog(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnLog = (delegate* unmanaged<nint, int, nint, int, void>)cb;
    }

    // ── Query callback setters ──────────────────────────────────────

    [UnmanagedCallersOnly(EntryPoint = "PE_SetResolveSkill")]
    public static void PE_SetResolveSkill(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.ResolveSkill = (delegate* unmanaged<nint, nint, int, int, int*, int*, int>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetIsMobBoss")]
    public static void PE_SetIsMobBoss(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.IsMobBoss = (delegate* unmanaged<nint, int, int>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetGetSkillName")]
    public static void PE_SetGetSkillName(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.GetSkillName = (delegate* unmanaged<nint, int, nint, int, int>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PE_SetIsKnownBuffCode")]
    public static void PE_SetIsKnownBuffCode(int handle, nint ctx, nint cb)
    {
        var s = EngineState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.IsKnownBuffCode = (delegate* unmanaged<nint, int, int>)cb;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PP (Party Parser)
    // ═══════════════════════════════════════════════════════════════════

    [UnmanagedCallersOnly(EntryPoint = "PP_Init")]
    public static int PP_Init()
    {
        var state = PartyState.Create();
        state.Parser = new PartyParser(state);
        return state.Handle;
    }

    [UnmanagedCallersOnly(EntryPoint = "PP_Shutdown")]
    public static void PP_Shutdown(int handle)
    {
        PartyState.Remove(handle);
    }

    [UnmanagedCallersOnly(EntryPoint = "PP_Feed")]
    public static void PP_Feed(int handle, byte* data, int length)
    {
        var state = PartyState.Get(handle);
        if (state?.Parser == null || length <= 0) return;
        byte[] buf = new byte[length];
        new ReadOnlySpan<byte>(data, length).CopyTo(buf);
        state.Parser.Feed(buf, 0, length);
    }

    [UnmanagedCallersOnly(EntryPoint = "PP_Reset")]
    public static void PP_Reset(int handle)
    {
        var state = PartyState.Get(handle);
        state?.Parser?.Reset();
    }

    // ── PP callback setters ─────────────────────────────────────────

    [UnmanagedCallersOnly(EntryPoint = "PP_SetOnMember")]
    public static void PP_SetOnMember(int handle, nint ctx, nint cb)
    {
        var s = PartyState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnMember = (delegate* unmanaged<nint, int, uint, int, nint, int, int, nint, int, int, int, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PP_SetOnBatch")]
    public static void PP_SetOnBatch(int handle, nint ctx, nint startCb, nint endCb)
    {
        var s = PartyState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnBatchStart = (delegate* unmanaged<nint, int, int, void>)startCb;
        s.OnBatchEnd   = (delegate* unmanaged<nint, int, void>)endCb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PP_SetOnPartyLeft")]
    public static void PP_SetOnPartyLeft(int handle, nint ctx, nint cb)
    {
        var s = PartyState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnPartyLeft = (delegate* unmanaged<nint, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PP_SetOnPartyEjected")]
    public static void PP_SetOnPartyEjected(int handle, nint ctx, nint cb)
    {
        var s = PartyState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnPartyEjected = (delegate* unmanaged<nint, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PP_SetOnDungeonDetected")]
    public static void PP_SetOnDungeonDetected(int handle, nint ctx, nint cb)
    {
        var s = PartyState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnDungeonDetected = (delegate* unmanaged<nint, int, int, void>)cb;
    }

    [UnmanagedCallersOnly(EntryPoint = "PP_SetOnCombatPowerDetected")]
    public static void PP_SetOnCombatPowerDetected(int handle, nint ctx, nint cb)
    {
        var s = PartyState.Get(handle); if (s == null) return;
        s.Ctx = ctx;
        s.OnCombatPowerDetected = (delegate* unmanaged<nint, nint, int, int, int, void>)cb;
    }
}
