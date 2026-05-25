using System;
using System.Collections.Generic;

namespace A2Meter.Dps;

/// Authoritative roster of players currently in the party / nearby.
/// Keyed by characterId when available. Packets such as party requests can
/// arrive without a characterId, so those rows are assigned a synthetic key
/// based on server + nickname until a real id is observed.
internal sealed class PartyTracker
{
    private readonly object _sync = new();
    private readonly Dictionary<uint, PartyMember> _members = new();
    private readonly Dictionary<string, uint> _namedKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> _partyNames = new(StringComparer.Ordinal);
    private uint _nextSyntheticId = 0x80000000;
    private long _nextPartyRequestOrder;

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

    /// Race-safe snapshot of the current member set. Returns a fresh array.
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
            bool hasProtocolId = member.CharacterId != 0;
            uint key = ResolveMemberKey(member, out var existing);

            if (member.IsSelf && hasProtocolId)
                SelfEntityId = (int)member.CharacterId;

            if (member.IsPartyMember && !string.IsNullOrEmpty(member.Nickname))
            {
                _partyNames.Add(member.Nickname);
                foreach (var m in _members.Values)
                    if (m.Nickname == member.Nickname)
                        m.IsPartyMember = true;
            }

            if (!member.IsPartyMember && !string.IsNullOrEmpty(member.Nickname) && _partyNames.Contains(member.Nickname))
                member.IsPartyMember = true;

            if (existing != null)
                MergeExisting(member, existing);

            if (member.IsPartyMember)
            {
                member.IsPartyRequest = false;
                member.PartyRequestOrder = 0;
            }
            else if (member.IsPartyRequest && member.PartyRequestOrder == 0)
            {
                member.PartyRequestOrder = ++_nextPartyRequestOrder;
            }

            _members[key] = member;
            changed = true;
        }

        if (changed) Changed?.Invoke();
    }

    private uint ResolveMemberKey(PartyMember member, out PartyMember? existing)
    {
        existing = null;
        string? nameKey = GetNameKey(member);

        if (member.CharacterId != 0)
        {
            if (nameKey != null &&
                _namedKeys.TryGetValue(nameKey, out var oldKey) &&
                oldKey != member.CharacterId &&
                _members.TryGetValue(oldKey, out var oldMember))
            {
                existing = oldMember;
                _members.Remove(oldKey);
            }
            else
            {
                _members.TryGetValue(member.CharacterId, out existing);
            }

            if (nameKey != null) _namedKeys[nameKey] = member.CharacterId;
            return member.CharacterId;
        }

        if (nameKey == null)
        {
            _members.TryGetValue(0, out existing);
            return 0;
        }

        if (!_namedKeys.TryGetValue(nameKey, out var key))
        {
            key = _nextSyntheticId++;
            _namedKeys[nameKey] = key;
        }

        member.CharacterId = key;
        _members.TryGetValue(key, out existing);
        return key;
    }

    private static void MergeExisting(PartyMember member, PartyMember existing)
    {
        if (!member.IsPartyMember) member.IsPartyMember = existing.IsPartyMember;
        if (!member.IsLookup) member.IsLookup = existing.IsLookup;
        if (!member.IsPartyRequest) member.IsPartyRequest = existing.IsPartyRequest;
        if (member.PartyRequestOrder == 0) member.PartyRequestOrder = existing.PartyRequestOrder;
        if (member.CombatPower == 0 && existing.CombatPower > 0) member.CombatPower = existing.CombatPower;
        if (member.ServerId == 0 && existing.ServerId > 0)
        {
            member.ServerId = existing.ServerId;
            member.ServerName = existing.ServerName;
        }
        if (member.JobCode == 0 && existing.JobCode > 0) member.JobCode = existing.JobCode;
        if (member.Level == 0 && existing.Level > 0) member.Level = existing.Level;
    }

    private static string? GetNameKey(PartyMember member)
        => string.IsNullOrEmpty(member.Nickname)
            ? null
            : $"{member.ServerId}\u001f{member.Nickname}";

    private void ForgetNamedKey(uint memberKey)
    {
        string? remove = null;
        foreach (var kvp in _namedKeys)
        {
            if (kvp.Value == memberKey)
            {
                remove = kvp.Key;
                break;
            }
        }
        if (remove != null) _namedKeys.Remove(remove);
    }

    public void Remove(uint characterId)
    {
        bool removed;
        lock (_sync)
        {
            removed = _members.Remove(characterId);
            if (removed) ForgetNamedKey(characterId);
        }
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

    /// Remove members that are neither self, confirmed party members, nor
    /// pending party-request rows.
    public void PurgeNonParty()
    {
        bool changed = false;
        lock (_sync)
        {
            List<uint>? toRemove = null;
            foreach (var kvp in _members)
                if (!kvp.Value.IsSelf && !kvp.Value.IsPartyMember && !kvp.Value.IsPartyRequest)
                    (toRemove ??= new List<uint>()).Add(kvp.Key);
            if (toRemove != null)
            {
                foreach (var id in toRemove)
                {
                    _members.Remove(id);
                    ForgetNamedKey(id);
                }
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
            _namedKeys.Clear();
            _members.Clear();
            _nextPartyRequestOrder = 0;
            changed = true;
        }
        if (changed) Changed?.Invoke();
    }
}
