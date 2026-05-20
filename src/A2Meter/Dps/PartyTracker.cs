using System;
using System.Collections.Generic;

namespace A2Meter.Dps;

/// Authoritative roster of players currently in the party / nearby.
/// Keyed by characterId (the protocol's stable identity), not by EntityId
/// (which is per-zone and can shift when re-entering a map).
///
/// Thread safety:
/// PacketEngine threads call Upsert/Remove/Purge*/Clear concurrently with
/// timer-driven readers (DpsPipeline.Push runs on the ThreadPool). All
/// internal access to <c>_members</c> and <c>_partyNames</c> is serialized
/// through <c>_sync</c>; external callers must enumerate via
/// <see cref="SnapshotMembers"/> rather than touching the dictionary directly.
internal sealed class PartyTracker
{
    private readonly object _sync = new();
    private readonly Dictionary<uint, PartyMember> _members = new();

    /// Confirmed party member nicknames — bridges characterId (party protocol)
    /// with entityId (UserInfo/combat) since they use different ID spaces.
    private readonly HashSet<string> _partyNames = new(StringComparer.Ordinal);

    /// EntityId of the local player (set when UserInfo isSelf=1 arrives).
    public int? SelfEntityId { get; private set; }

    /// True when an actual party exists (at least one non-self member confirmed
    /// via a party protocol packet, not just seen nearby).
    public bool HasParty
    {
        get
        {
            lock (_sync)
            {
                foreach (var m in _members.Values)
                    if (m.IsPartyMember && !m.IsSelf) return true;
                return false;
            }
        }
    }

    /// Race-safe snapshot of the current member set. Returns a fresh array;
    /// the caller may iterate freely without worrying about concurrent
    /// Upsert/Remove. Each <see cref="PartyMember"/> is shared by reference
    /// — treat its fields as read-only outside PartyTracker.
    public PartyMember[] SnapshotMembers()
    {
        lock (_sync)
        {
            var arr = new PartyMember[_members.Count];
            int i = 0;
            foreach (var m in _members.Values) arr[i++] = m;
            return arr;
        }
    }

    /// Check if a given entityId belongs to a confirmed party member (or self).
    public bool IsInParty(uint entityId)
    {
        lock (_sync)
        {
            return _members.TryGetValue(entityId, out var m) && (m.IsPartyMember || m.IsSelf);
        }
    }

    /// Check if a nickname belongs to a confirmed party member.
    public bool IsPartyName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        lock (_sync) { return _partyNames.Contains(name); }
    }

    public event Action? Changed;

    public void Upsert(PartyMember member)
    {
        bool changed;
        lock (_sync)
        {
            if (member.IsSelf && member.CharacterId != 0)
                SelfEntityId = (int)member.CharacterId;

            // When a party protocol confirms a member, record their nickname
            // and retroactively mark any existing entry with the same name.
            if (member.IsPartyMember && !string.IsNullOrEmpty(member.Nickname))
            {
                _partyNames.Add(member.Nickname);
                foreach (var m in _members.Values)
                    if (m.Nickname == member.Nickname)
                        m.IsPartyMember = true;
            }

            // Bridge: if this member's nickname matches a confirmed party member, mark them.
            if (!member.IsPartyMember && !string.IsNullOrEmpty(member.Nickname) && _partyNames.Contains(member.Nickname))
                member.IsPartyMember = true;

            // When CharacterId is 0 (e.g. CombatPowerByName event), merge into
            // an existing entry found by nickname instead of creating a ghost at key 0.
            if (member.CharacterId == 0 && !string.IsNullOrEmpty(member.Nickname))
            {
                foreach (var kvp in _members)
                {
                    if (kvp.Value.Nickname == member.Nickname)
                    {
                        var exist = kvp.Value;
                        if (member.CombatPower > exist.CombatPower) exist.CombatPower = member.CombatPower;
                        if (member.ServerId > 0 && exist.ServerId == 0) { exist.ServerId = member.ServerId; exist.ServerName = member.ServerName; }
                        if (member.JobCode > 0 && exist.JobCode == 0) exist.JobCode = member.JobCode;
                        if (member.IsPartyMember) exist.IsPartyMember = true;
                        changed = true;
                        goto raise;
                    }
                }
                // No existing entry — fall through and store at key 0.
            }

            // Preserve existing flags/values when upserting identity-only data.
            if (_members.TryGetValue(member.CharacterId, out var existing))
            {
                if (!member.IsPartyMember) member.IsPartyMember = existing.IsPartyMember;
                if (!member.IsLookup)      member.IsLookup      = existing.IsLookup;
                if (member.CombatPower == 0 && existing.CombatPower > 0)
                    member.CombatPower = existing.CombatPower;
            }

            _members[member.CharacterId] = member;
            changed = true;
        }
    raise:
        if (changed) Changed?.Invoke();
    }

    public void Remove(uint characterId)
    {
        bool removed;
        lock (_sync) { removed = _members.Remove(characterId); }
        if (removed) Changed?.Invoke();
    }

    /// Clear all party membership flags (called on party disband/leave).
    public void ClearPartyFlags()
    {
        lock (_sync)
        {
            _partyNames.Clear();
            foreach (var m in _members.Values)
                m.IsPartyMember = false;
        }
        Changed?.Invoke();
    }

    /// Remove members that are neither self nor confirmed party members.
    public void PurgeNonParty()
    {
        bool changed = false;
        lock (_sync)
        {
            List<uint>? toRemove = null;
            foreach (var kvp in _members)
                if (!kvp.Value.IsSelf && !kvp.Value.IsPartyMember)
                    (toRemove ??= new List<uint>()).Add(kvp.Key);
            if (toRemove != null)
            {
                foreach (var id in toRemove) _members.Remove(id);
                changed = toRemove.Count > 0;
            }
        }
        if (changed) Changed?.Invoke();
    }

    public void Clear()
    {
        bool changed;
        lock (_sync)
        {
            if (_members.Count == 0) return;
            _partyNames.Clear();
            _members.Clear();
            changed = true;
        }
        if (changed) Changed?.Invoke();
    }
}
