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
    private readonly Dictionary<uint, uint> _aliases = new();
    private readonly HashSet<string> _partyNames = new(StringComparer.Ordinal);
    private uint _nextSyntheticId = 0x80000000;
    private long _nextPartyRequestOrder;
    private PartyMember? _selfIdentity;

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

    public bool TryGetSelfIdentity(out PartyMember member)
    {
        lock (_sync)
        {
            if (_selfIdentity != null)
            {
                member = Clone(_selfIdentity);
                return true;
            }

            foreach (var candidate in _members.Values)
            {
                if (!candidate.IsSelf) continue;
                member = Clone(candidate);
                return true;
            }
        }

        member = null!;
        return false;
    }

    /// Check if a given entityId belongs to a confirmed party member (or self).
    public bool IsInParty(uint entityId)
    {
        lock (_sync)
        {
            return TryGetByIdOrAlias(entityId, out var m) && (m.IsPartyMember || m.IsSelf);
        }
    }

    /// Check if a nickname belongs to a confirmed party member.
    public bool IsPartyName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        lock (_sync) { return _partyNames.Contains(CleanName(name)); }
    }

    public event Action? Changed;

    public void Upsert(PartyMember member)
    {
        bool changed;
        lock (_sync)
        {
            bool hasProtocolId = member.CharacterId != 0;
            uint key = ResolveMemberKey(member, out var existing);

            if (member.IsPartyMember && !string.IsNullOrEmpty(member.Nickname))
            {
                string partyName = CleanName(member.Nickname);
                _partyNames.Add(partyName);
                foreach (var m in _members.Values)
                    if (CleanName(m.Nickname) == partyName)
                        m.IsPartyMember = true;
            }

            if (!member.IsPartyMember && !string.IsNullOrEmpty(member.Nickname) && _partyNames.Contains(CleanName(member.Nickname)))
                member.IsPartyMember = true;

            if (existing != null)
                MergeExisting(member, existing);

            if (member.IsSelf)
                ApplySelfIdentityFallback(member);

            if (member.IsPartyMember)
            {
                member.IsPartyRequest = false;
                member.PartyRequestOrder = 0;
            }
            else if (member.IsPartyRequest && member.PartyRequestOrder == 0)
            {
                member.PartyRequestOrder = ++_nextPartyRequestOrder;
            }

            if (member.IsSelf && hasProtocolId)
            {
                SelfEntityId = (int)member.CharacterId;
                ClearSelfFlagsExcept(member.CharacterId);
            }

            _members[key] = member;
            if (member.IsSelf)
                RememberSelfIdentity(member);
            changed = true;
        }

        if (changed) Changed?.Invoke();
    }

    private void ClearSelfFlagsExcept(uint characterId)
    {
        foreach (var m in _members.Values)
        {
            if (m.CharacterId != characterId)
                m.IsSelf = false;
        }
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
                _aliases[oldKey] = member.CharacterId;
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
        if (!member.IsSelf) member.IsSelf = existing.IsSelf;
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

    private void ApplySelfIdentityFallback(PartyMember member)
    {
        if (_selfIdentity == null) return;

        if (IsUnknownSelfName(member.Nickname) && !IsUnknownSelfName(_selfIdentity.Nickname))
            member.Nickname = _selfIdentity.Nickname;
        if (member.ServerId == 0 && _selfIdentity.ServerId > 0)
        {
            member.ServerId = _selfIdentity.ServerId;
            member.ServerName = _selfIdentity.ServerName;
        }
        if (string.IsNullOrWhiteSpace(member.ServerName) && !string.IsNullOrWhiteSpace(_selfIdentity.ServerName))
            member.ServerName = _selfIdentity.ServerName;
        if (member.JobCode <= 0 && _selfIdentity.JobCode > 0)
            member.JobCode = _selfIdentity.JobCode;
        if (member.Level == 0 && _selfIdentity.Level > 0)
            member.Level = _selfIdentity.Level;
        if (member.CombatPower == 0 && _selfIdentity.CombatPower > 0)
            member.CombatPower = _selfIdentity.CombatPower;
    }

    private void RememberSelfIdentity(PartyMember member)
    {
        var copy = Clone(member);
        copy.IsSelf = true;
        _selfIdentity = copy;
    }

    private static PartyMember Clone(PartyMember member)
        => new()
        {
            CharacterId = member.CharacterId,
            ServerId = member.ServerId,
            ServerName = member.ServerName,
            Nickname = member.Nickname,
            JobCode = member.JobCode,
            JobName = member.JobName,
            Level = member.Level,
            CombatPower = member.CombatPower,
            IsSelf = member.IsSelf,
            IsPartyMember = member.IsPartyMember,
            IsLookup = member.IsLookup,
            IsPartyRequest = member.IsPartyRequest,
            PartyRequestOrder = member.PartyRequestOrder,
        };

    private static string? GetNameKey(PartyMember member)
        => string.IsNullOrEmpty(CleanName(member.Nickname))
            ? null
            : $"{member.ServerId}\u001f{CleanName(member.Nickname)}";

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

    private void ForgetAliases(uint memberKey)
    {
        _aliases.Remove(memberKey);
        List<uint>? remove = null;
        foreach (var kvp in _aliases)
        {
            if (kvp.Value == memberKey)
                (remove ??= new List<uint>()).Add(kvp.Key);
        }
        if (remove != null)
            foreach (var key in remove)
                _aliases.Remove(key);
    }

    private bool TryGetByIdOrAlias(uint id, out PartyMember member)
    {
        if (_members.TryGetValue(id, out member!))
            return true;
        if (TryResolveAlias(id, out var canonical) && _members.TryGetValue(canonical, out member!))
            return true;
        member = null!;
        return false;
    }

    private bool TryResolveAlias(uint id, out uint canonical)
    {
        canonical = id;
        for (int i = 0; i < 8; i++)
        {
            if (!_aliases.TryGetValue(canonical, out var next) || next == canonical)
                return canonical != id;
            canonical = next;
        }
        return canonical != id;
    }

    public void Remove(uint characterId)
    {
        bool removed;
        lock (_sync)
        {
            removed = _members.Remove(characterId);
            if (removed)
            {
                ForgetNamedKey(characterId);
                ForgetAliases(characterId);
            }
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

    /// Clear party/session identity while preserving lookup rows shown in the
    /// lookup tab. New party membership must come from fresh party packets.
    public void ClearPartyForDungeonEnter()
    {
        bool changed = false;
        lock (_sync)
        {
            var preservedMembers = new List<PartyMember>();
            int? preservedSelfEntityId = null;
            foreach (var member in _members.Values)
            {
                bool preserveSelf = member.IsSelf
                    && member.CharacterId != 0
                    && member.CharacterId <= int.MaxValue;
                if (!preserveSelf && !member.IsLookup && !IsIdentityHint(member))
                {
                    changed = true;
                    continue;
                }

                if ((!preserveSelf && member.IsSelf) || member.IsPartyMember || member.IsPartyRequest || member.PartyRequestOrder != 0)
                    changed = true;

                member.IsSelf = preserveSelf;
                member.IsPartyMember = false;
                member.IsPartyRequest = false;
                member.PartyRequestOrder = 0;
                if (preserveSelf)
                    preservedSelfEntityId = (int)member.CharacterId;
                preservedMembers.Add(member);
            }

            if (SelfEntityId != preservedSelfEntityId)
                changed = true;

            _partyNames.Clear();
            _namedKeys.Clear();
            _aliases.Clear();
            _members.Clear();
            SelfEntityId = preservedSelfEntityId;
            _nextPartyRequestOrder = 0;

            foreach (var member in preservedMembers)
            {
                uint key = member.CharacterId != 0 ? member.CharacterId : _nextSyntheticId++;
                member.CharacterId = key;
                _members[key] = member;
                var nameKey = GetNameKey(member);
                if (nameKey != null)
                    _namedKeys[nameKey] = key;
            }
        }

        if (changed) Changed?.Invoke();
    }

    public bool TryGetLookupForCombatActor(int entityId, string? nickname, out PartyMember member)
    {
        lock (_sync)
        {
            if (entityId > 0
                && TryGetByIdOrAlias((uint)entityId, out var byId)
                && IsCombatRelevant(byId))
            {
                member = byId;
                return true;
            }

            string cleanName = CleanName(nickname);
            if (cleanName.Length > 0)
            {
                foreach (var candidate in _members.Values)
                {
                    if (!IsCombatRelevant(candidate)) continue;
                    if (string.Equals(CleanName(candidate.Nickname), cleanName, StringComparison.OrdinalIgnoreCase))
                    {
                        member = candidate;
                        return true;
                    }
                }
            }
        }

        member = null!;
        return false;
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
                if (!kvp.Value.IsSelf
                    && !kvp.Value.IsPartyMember
                    && !kvp.Value.IsPartyRequest
                    && !kvp.Value.IsLookup
                    && !IsIdentityHint(kvp.Value))
                    (toRemove ??= new List<uint>()).Add(kvp.Key);
            if (toRemove != null)
            {
                foreach (var id in toRemove)
                {
                    _members.Remove(id);
                    ForgetNamedKey(id);
                    ForgetAliases(id);
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
            if (_members.Count == 0 && SelfEntityId is null && _selfIdentity == null) return;
            _partyNames.Clear();
            _namedKeys.Clear();
            _aliases.Clear();
            _members.Clear();
            SelfEntityId = null;
            _selfIdentity = null;
            _nextPartyRequestOrder = 0;
            changed = true;
        }
        if (changed) Changed?.Invoke();
    }

    private static string CleanName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        int idx = name.IndexOf('[');
        return (idx > 0 ? name[..idx] : name).Trim();
    }

    private static bool IsCombatRelevant(PartyMember member)
        => member.IsLookup || member.IsPartyMember || member.IsSelf || member.IsPartyRequest;

    private static bool IsIdentityHint(PartyMember member)
        => member.CharacterId != 0
           && !string.IsNullOrWhiteSpace(member.Nickname)
           && member.ServerId > 0
           && member.JobCode > 0;

    private static bool IsUnknownSelfName(string? name)
    {
        string clean = CleanName(name);
        return clean.Length == 0 || clean.StartsWith('#') || clean == "\uB098";
    }
}
