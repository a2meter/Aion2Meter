using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Npgsql;
using Renci.SshNet;

namespace A2DebugPacketTool;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

internal enum MockOpcode
{
    DungeonEnter,
    DungeonLeave,
    SelfInfo,
    CombatPower,
    PartyList,
    PartyUpdate,
    PartyRequest,
    PartyAccept,
    PartyLeft,
    PartyKick,
    MobSpawn,
    Damage,
    BossHp,
    EntityRemoved,
    Buff,
}

internal sealed class JobOption
{
    private static readonly Dictionary<int, string> KnownNames = new()
    {
        [5] = "검성",
        [9] = "수호성",
        [13] = "궁성",
        [17] = "살성",
        [21] = "정령성",
        [25] = "마도성",
        [29] = "치유성",
        [33] = "호법성",
    };

    public JobOption(int code, string? name = null)
    {
        Code = code;
        Name = ResolveName(code, name);
    }

    public int Code { get; }
    public string Name { get; }

    public override string ToString() => $"{Name} ({Code})";

    private static string ResolveName(int code, string? name)
    {
        if (KnownNames.TryGetValue(NormalizeBaseJobCode(code), out var known))
            return known;

        string trimmed = name?.Trim() ?? "";
        if (trimmed.Length > 0 && !trimmed.StartsWith("Job ", StringComparison.OrdinalIgnoreCase))
            return trimmed;

        return $"직업 {code}";
    }

    private static int NormalizeBaseJobCode(int code)
        => code is >= 5 and <= 36 ? ((code - 5) / 4) * 4 + 5 : code;

    public static List<JobOption> Defaults()
        => new()
        {
            new JobOption(5),
            new JobOption(9),
            new JobOption(13),
            new JobOption(17),
            new JobOption(21),
            new JobOption(25),
            new JobOption(29),
            new JobOption(33),
        };
}

internal sealed class ServerOption
{
    public ServerOption(int id, string name)
    {
        Id = id;
        Name = name.Trim();
    }

    public int Id { get; }
    public string Name { get; }

    public override string ToString() => $"{Name} ({Id})";

    public static List<ServerOption> Defaults()
        => new()
        {
            new ServerOption(1001, "시엘"),
            new ServerOption(1002, "네자칸"),
            new ServerOption(1003, "바이젤"),
            new ServerOption(1004, "카이시넬"),
            new ServerOption(1005, "유스티엘"),
            new ServerOption(1006, "아리엘"),
            new ServerOption(1007, "프레기온"),
            new ServerOption(1008, "메스람타에다"),
            new ServerOption(1009, "히타니에"),
            new ServerOption(1010, "나니아"),
            new ServerOption(1011, "타하바타"),
            new ServerOption(1012, "루터스"),
            new ServerOption(1013, "페르노스"),
            new ServerOption(1014, "다미누"),
            new ServerOption(1015, "카사카"),
            new ServerOption(1016, "바카르마"),
            new ServerOption(1017, "챈가룽"),
            new ServerOption(1018, "코치룽"),
            new ServerOption(1019, "이슈타르"),
            new ServerOption(1020, "티아마트"),
            new ServerOption(1021, "포에타"),
            new ServerOption(2001, "이스라펠"),
            new ServerOption(2002, "지켈"),
            new ServerOption(2003, "트리니엘"),
            new ServerOption(2004, "루미엘"),
            new ServerOption(2005, "마르쿠탄"),
            new ServerOption(2006, "아스펠"),
            new ServerOption(2007, "에레슈키갈"),
            new ServerOption(2008, "브리트라"),
            new ServerOption(2009, "네몬"),
            new ServerOption(2010, "하달"),
            new ServerOption(2011, "루드라"),
            new ServerOption(2012, "울고른"),
            new ServerOption(2013, "무닌"),
            new ServerOption(2014, "오다르"),
            new ServerOption(2015, "젠카카"),
            new ServerOption(2016, "크로메데"),
            new ServerOption(2017, "콰이링"),
            new ServerOption(2018, "바바룽"),
            new ServerOption(2019, "파프니르"),
            new ServerOption(2020, "인드나흐"),
            new ServerOption(2021, "이스할겐"),
        };
}

internal class PacketMemberEditor
{
    private readonly NumericUpDown _entity;
    private readonly TextBox _name;
    private readonly ComboBox _job;
    private readonly ComboBox _server;
    private readonly NumericUpDown _level;
    private readonly NumericUpDown _cp;
    private int _fallbackJobCode;
    private int _fallbackServerId;

    public PacketMemberEditor(string title, int entityId, string name, int jobCode, int serverId, int level, int combatPower)
    {
        Title = title;
        _entity = MainFormNum(1, int.MaxValue, entityId, 90);
        _name = new TextBox { Text = name, Width = 120 };
        _fallbackJobCode = jobCode;
        _job = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
        _fallbackServerId = serverId;
        _server = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 135 };
        _level = MainFormNum(1, 99, level, 55);
        _cp = MainFormNum(0, int.MaxValue, combatPower, 95, 10000);
    }

    public string Title { get; }
    public int EntityId => (int)_entity.Value;
    public string NameText => _name.Text.Trim();
    public int JobCode => _job.SelectedItem is JobOption job ? job.Code : _fallbackJobCode;
    public int ServerId => _server.SelectedItem is ServerOption server ? server.Id : _fallbackServerId;
    public int Level => (int)_level.Value;
    public int CombatPower => (int)_cp.Value;

    public void SetJobs(IReadOnlyList<JobOption> jobs)
    {
        int current = JobCode;
        _job.BeginUpdate();
        try
        {
            _job.Items.Clear();
            foreach (var job in jobs)
                _job.Items.Add(job);

            int selected = -1;
            for (int i = 0; i < _job.Items.Count; i++)
            {
                if (_job.Items[i] is JobOption option && option.Code == current)
                {
                    selected = i;
                    break;
                }
            }
            if (selected < 0)
            {
                for (int i = 0; i < _job.Items.Count; i++)
                {
                    if (_job.Items[i] is JobOption option && option.Code == _fallbackJobCode)
                    {
                        selected = i;
                        break;
                    }
                }
            }
            if (selected < 0 && _job.Items.Count > 0) selected = 0;
            if (selected >= 0) _job.SelectedIndex = selected;
        }
        finally
        {
            _job.EndUpdate();
        }
    }

    public void SetServers(IReadOnlyList<ServerOption> servers)
    {
        int current = ServerId;
        _server.BeginUpdate();
        try
        {
            _server.Items.Clear();
            foreach (var server in servers)
                _server.Items.Add(server);

            int selected = -1;
            for (int i = 0; i < _server.Items.Count; i++)
            {
                if (_server.Items[i] is ServerOption option && option.Id == current)
                {
                    selected = i;
                    break;
                }
            }
            if (selected < 0)
            {
                for (int i = 0; i < _server.Items.Count; i++)
                {
                    if (_server.Items[i] is ServerOption option && option.Id == _fallbackServerId)
                    {
                        selected = i;
                        break;
                    }
                }
            }
            if (selected < 0 && _server.Items.Count > 0) selected = 0;
            if (selected >= 0) _server.SelectedIndex = selected;
        }
        finally
        {
            _server.EndUpdate();
        }
    }

    public virtual void AddControls(FlowLayoutPanel panel)
    {
        panel.Controls.Add(new Label { Text = "Entity", AutoSize = true, Margin = new Padding(8, 8, 2, 2) });
        panel.Controls.Add(_entity);
        panel.Controls.Add(new Label { Text = "Name", AutoSize = true, Margin = new Padding(8, 8, 2, 2) });
        panel.Controls.Add(_name);
        panel.Controls.Add(new Label { Text = "Job", AutoSize = true, Margin = new Padding(8, 8, 2, 2) });
        panel.Controls.Add(_job);
        panel.Controls.Add(new Label { Text = "Server", AutoSize = true, Margin = new Padding(8, 8, 2, 2) });
        panel.Controls.Add(_server);
        panel.Controls.Add(new Label { Text = "Lv", AutoSize = true, Margin = new Padding(8, 8, 2, 2) });
        panel.Controls.Add(_level);
        panel.Controls.Add(new Label { Text = "CP", AutoSize = true, Margin = new Padding(8, 8, 2, 2) });
        panel.Controls.Add(_cp);
    }

    protected static NumericUpDown MainFormNum(decimal min, decimal max, decimal value, int width, decimal increment = 1)
        => new() { Minimum = min, Maximum = max, Value = Math.Min(max, Math.Max(min, value)), Width = width, Increment = increment };
}

internal sealed class PartyRow : PacketMemberEditor
{
    public PartyRow(int index, int entityId, string name, int jobCode, int serverId, int level, int combatPower)
        : base($"파티원 {index}", entityId, name, jobCode, serverId, level, combatPower)
    {
        Panel = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Dock = DockStyle.Top, Margin = new Padding(4) };
        Panel.Controls.Add(new Label { Text = $"파티원 {index}", Width = 60, Margin = new Padding(4, 8, 2, 2) });
        AddControls(Panel);
        var left = new Button { Text = "Left", AutoSize = true, Height = 30, Margin = new Padding(12, 4, 4, 4) };
        var kick = new Button { Text = "Kick", AutoSize = true, Height = 30, Margin = new Padding(4) };
        left.Click += (s, e) => LeftClicked?.Invoke(s, e);
        kick.Click += (s, e) => KickClicked?.Invoke(s, e);
        Panel.Controls.Add(left);
        Panel.Controls.Add(kick);
    }

    public FlowLayoutPanel Panel { get; }
    public event EventHandler? LeftClicked;
    public event EventHandler? KickClicked;
}

internal sealed class MockMember
{
    public int EntityId { get; set; }
    public string Name { get; set; } = "";
    public int JobCode { get; set; }
    public int ServerId { get; set; }
    public int Level { get; set; }
    public int CombatPower { get; set; }
}

internal sealed class MockCommand
{
    public int DeltaTime { get; set; }
    public string Opcode { get; set; } = "";
    public int EntityId { get; set; }
    public int ActorId { get; set; }
    public int TargetId { get; set; }
    public string Name { get; set; } = "";
    public int JobCode { get; set; }
    public int ServerId { get; set; }
    public int Level { get; set; }
    public int CombatPower { get; set; }
    public int DungeonId { get; set; }
    public int Stage { get; set; }
    public int BossCode { get; set; }
    public int Hp { get; set; }
    public int SkillCode { get; set; }
    public int Damage { get; set; }
    public bool Crit { get; set; }
    public int BuffId { get; set; }
    public int DurationMs { get; set; }
    public int CasterId { get; set; }
    public List<MockMember>? Members { get; set; }
}

internal static class PacketBuilder
{
    private static readonly byte[] Magic = { 0x06, 0x00, 0x36 };

    public static IEnumerable<byte[]> Build(MockCommand c, List<MockMember> fallbackMembers)
    {
        string op = (c.Opcode ?? "").Trim();
        if (op.Equals("SelfInfo", StringComparison.OrdinalIgnoreCase))
        {
            yield return Frame(BuildSelfInfo(c));
        }
        else if (op.Equals("CombatPower", StringComparison.OrdinalIgnoreCase))
        {
            yield return Frame(BuildCombatPower(c));
        }
        else if (op.Equals("PartyList", StringComparison.OrdinalIgnoreCase))
        {
            yield return Frame(BuildPartyList(0x01, c.Members ?? fallbackMembers));
        }
        else if (op.Equals("PartyUpdate", StringComparison.OrdinalIgnoreCase))
        {
            yield return Frame(BuildPartyUpdate(c.DungeonId, c.Stage, c.Members ?? fallbackMembers));
        }
        else if (op.Equals("PartyRequest", StringComparison.OrdinalIgnoreCase))
        {
            yield return Frame(BuildPartyRequest(ToMember(c)));
        }
        else if (op.Equals("PartyAccept", StringComparison.OrdinalIgnoreCase))
        {
            yield return Frame(BuildPartyAccept(ToMember(c)));
        }
        else if (op.Equals("PartyLeft", StringComparison.OrdinalIgnoreCase))
        {
            yield return Frame(new byte[] { 0x1D, 0x97, 0x00, 0x00 });
            yield return Frame(new byte[] { 0x01, 0x97, 0x00, 0x00 });
        }
        else if (op.Equals("PartyKick", StringComparison.OrdinalIgnoreCase))
        {
            yield return Frame(new byte[] { 0x01, 0x97, 0x00, 0x00 });
        }
        else if (op.Equals("DungeonEnter", StringComparison.OrdinalIgnoreCase))
        {
            yield return Frame(BuildDungeon(c.DungeonId, c.Stage));
        }
        else if (op.Equals("DungeonLeave", StringComparison.OrdinalIgnoreCase))
        {
            yield return Frame(new byte[] { 0x04, 0x97, 0x00, 0x00 });
        }
        else if (op.Equals("MobSpawn", StringComparison.OrdinalIgnoreCase))
        {
            yield return Frame(BuildMobSpawn(c.EntityId, c.BossCode, c.Hp));
        }
        else if (op.Equals("Damage", StringComparison.OrdinalIgnoreCase))
        {
            yield return Frame(BuildDamage(c.ActorId, c.TargetId, c.SkillCode, c.Damage, c.Crit));
        }
        else if (op.Equals("BossHp", StringComparison.OrdinalIgnoreCase))
        {
            yield return Frame(BuildBossHp(c.EntityId, c.Hp));
        }
        else if (op.Equals("EntityRemoved", StringComparison.OrdinalIgnoreCase))
        {
            yield return Frame(BuildEntityRemoved(c.EntityId));
        }
        else if (op.Equals("Buff", StringComparison.OrdinalIgnoreCase))
        {
            yield return Frame(BuildBuff(c.EntityId, c.BuffId, c.DurationMs, c.CasterId));
        }
    }

    private static MockMember ToMember(MockCommand c)
        => new()
        {
            EntityId = c.EntityId != 0 ? c.EntityId : c.ActorId,
            Name = string.IsNullOrWhiteSpace(c.Name) ? "MockMember" : c.Name,
            JobCode = c.JobCode != 0 ? c.JobCode : 5,
            ServerId = c.ServerId != 0 ? c.ServerId : 1001,
            Level = c.Level != 0 ? c.Level : 55,
            CombatPower = c.CombatPower != 0 ? c.CombatPower : 500000,
        };

    private static byte[] Frame(byte[] payloadWithoutMagic)
    {
        var payload = new List<byte>(payloadWithoutMagic.Length + Magic.Length);
        payload.AddRange(payloadWithoutMagic);
        payload.AddRange(Magic);
        var frame = new List<byte>(payload.Count + 5);
        WriteFrameVarint(frame, (uint)(payload.Count + 4));
        frame.AddRange(payload);
        return frame.ToArray();
    }

    private static byte[] BuildSelfInfo(MockCommand c)
    {
        var bytes = new List<byte> { 0x33, 0x36 };
        WriteVarint(bytes, (uint)c.EntityId);
        WriteName(bytes, c.Name);
        WriteUInt16(bytes, (ushort)c.ServerId);
        bytes.Add((byte)c.JobCode);
        return bytes.ToArray();
    }

    private static byte[] BuildCombatPower(MockCommand c)
    {
        var bytes = new List<byte> { 47, 42, 56 };
        WriteVarint(bytes, (uint)c.EntityId);
        bytes.AddRange(new byte[] { 14, 85, 54 });
        WriteInt32(bytes, c.CombatPower);
        return bytes.ToArray();
    }

    private static byte[] BuildDungeon(int dungeonId, int stage)
    {
        var bytes = new List<byte> { 0x02, 0x97, 0, 0, 0, 0 };
        WriteVarint(bytes, 0);
        bytes.Add(4);
        WriteInt32(bytes, dungeonId);
        bytes.Add((byte)stage);
        return bytes.ToArray();
    }

    private static byte[] BuildPartyUpdate(int dungeonId, int stage, List<MockMember> members)
    {
        var bytes = new List<byte>();
        bytes.AddRange(BuildDungeon(dungeonId, stage));
        foreach (var member in members) bytes.AddRange(BuildMemberBlock(member));
        return bytes.ToArray();
    }

    private static byte[] BuildPartyList(byte op, List<MockMember> members)
    {
        var bytes = new List<byte> { op, 0x97 };
        foreach (var member in members) bytes.AddRange(BuildMemberBlock(member));
        return bytes.ToArray();
    }

    private static byte[] BuildMemberBlock(MockMember member)
    {
        var bytes = new List<byte>();
        WriteInt32(bytes, member.EntityId);
        bytes.Add(0);
        bytes.Add(0);
        WriteUInt16(bytes, (ushort)member.ServerId);
        var name = Encoding.UTF8.GetBytes(member.Name);
        bytes.Add((byte)Math.Min(48, name.Length));
        bytes.AddRange(name.AsSpan(0, Math.Min(48, name.Length)).ToArray());
        WriteInt32(bytes, member.JobCode);
        WriteInt32(bytes, member.Level);
        WriteInt32(bytes, Math.Max(500, member.CombatPower));
        WriteUInt16(bytes, (ushort)member.ServerId);
        WriteUInt16(bytes, (ushort)member.ServerId);
        bytes.Add(4);
        WriteInt32(bytes, member.CombatPower);
        return bytes.ToArray();
    }

    private static byte[] BuildPartyRequest(MockMember member)
    {
        var bytes = new List<byte> { 0x07, 0x97 };
        var data = new byte[24];
        data[10] = (byte)(member.ServerId & 0xFF);
        data[11] = (byte)((member.ServerId >> 8) & 0xFF);
        WriteInt32(data, 12, member.JobCode);
        WriteInt32(data, 16, member.Level);
        bytes.AddRange(data);
        var name = Encoding.UTF8.GetBytes(member.Name);
        bytes.Add((byte)Math.Min(48, name.Length));
        bytes.AddRange(name.AsSpan(0, Math.Min(48, name.Length)).ToArray());
        bytes.AddRange(new byte[6]);
        WriteInt32(bytes, member.CombatPower);
        return bytes.ToArray();
    }

    private static byte[] BuildPartyAccept(MockMember member)
    {
        var bytes = new List<byte> { 0x0B, 0x97, 26, 0 };
        WriteInt32(bytes, member.EntityId);
        bytes.Add(0);
        bytes.Add(0);
        WriteUInt16(bytes, (ushort)member.ServerId);
        var name = Encoding.UTF8.GetBytes(member.Name);
        bytes.Add((byte)Math.Min(48, name.Length));
        bytes.AddRange(name.AsSpan(0, Math.Min(48, name.Length)).ToArray());
        WriteInt32(bytes, member.JobCode);
        WriteInt32(bytes, member.Level);
        WriteInt32(bytes, member.CombatPower);
        return bytes.ToArray();
    }

    private static byte[] BuildMobSpawn(int entityId, int mobCode, int hp)
    {
        var bytes = new List<byte> { 0x40, 0x36 };
        WriteVarint(bytes, (uint)entityId);
        bytes.Add((byte)(mobCode & 0xFF));
        bytes.Add((byte)((mobCode >> 8) & 0xFF));
        bytes.Add((byte)((mobCode >> 16) & 0xFF));
        bytes.Add(0);
        bytes.Add(0);
        bytes.Add(2);
        bytes.Add(1);
        WriteVarint(bytes, (uint)Math.Max(1, hp));
        WriteVarint(bytes, (uint)Math.Max(0, hp));
        return bytes.ToArray();
    }

    private static byte[] BuildDamage(int actorId, int targetId, int skillCode, int damage, bool crit)
    {
        var bytes = new List<byte> { 0x04, 0x38 };
        WriteVarint(bytes, (uint)targetId);
        WriteVarint(bytes, 4);
        WriteVarint(bytes, 0);
        WriteVarint(bytes, (uint)actorId);
        WriteInt32(bytes, skillCode);
        bytes.Add(0);
        WriteVarint(bytes, crit ? 3u : 1u);
        bytes.Add(crit ? (byte)0x80 : (byte)0);
        bytes.Add(0);
        bytes.AddRange(new byte[6]);
        WriteVarint(bytes, 0);
        WriteVarint(bytes, (uint)Math.Max(1, damage));
        return bytes.ToArray();
    }

    private static byte[] BuildBossHp(int entityId, int hp)
    {
        var bytes = new List<byte> { 0x01, 0x8D };
        WriteVarint(bytes, (uint)entityId);
        bytes.AddRange(new byte[] { 2, 1, 0 });
        WriteInt32(bytes, Math.Max(0, hp));
        WriteInt32(bytes, 0);
        return bytes.ToArray();
    }

    private static byte[] BuildEntityRemoved(int entityId)
    {
        var bytes = new List<byte> { 0x21, 0x8D };
        WriteVarint(bytes, (uint)entityId);
        return bytes.ToArray();
    }

    private static byte[] BuildBuff(int entityId, int buffId, int durationMs, int casterId)
    {
        var bytes = new List<byte> { 0x2A, 0x38 };
        WriteVarint(bytes, (uint)entityId);
        bytes.Add(0);
        bytes.Add(0);
        WriteVarint(bytes, 0);
        WriteInt32(bytes, buffId);
        WriteInt32(bytes, durationMs);
        WriteInt32(bytes, 0);
        WriteInt64(bytes, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        WriteVarint(bytes, (uint)Math.Max(0, casterId));
        return bytes.ToArray();
    }

    private static void WriteName(List<byte> bytes, string name)
    {
        var raw = Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(name) ? "Mock" : name.Trim());
        int len = Math.Min(48, raw.Length);
        bytes.Add(7);
        WriteVarint(bytes, (uint)len);
        bytes.AddRange(raw.AsSpan(0, len).ToArray());
    }

    private static void WriteVarint(List<byte> bytes, uint value)
    {
        while (value >= 0x80)
        {
            bytes.Add((byte)(value | 0x80));
            value >>= 7;
        }
        bytes.Add((byte)value);
    }

    private static void WriteFrameVarint(List<byte> bytes, uint value)
    {
        bytes.Add((byte)((value & 0x7F) | 0x80));
        bytes.Add((byte)(((value >> 7) & 0x7F) | 0x80));
        bytes.Add((byte)(((value >> 14) & 0x7F) | 0x80));
        bytes.Add((byte)((value >> 21) & 0x7F));
    }

    private static void WriteUInt16(List<byte> bytes, ushort value)
    {
        bytes.Add((byte)(value & 0xFF));
        bytes.Add((byte)((value >> 8) & 0xFF));
    }

    private static void WriteInt32(List<byte> bytes, int value)
    {
        bytes.Add((byte)(value & 0xFF));
        bytes.Add((byte)((value >> 8) & 0xFF));
        bytes.Add((byte)((value >> 16) & 0xFF));
        bytes.Add((byte)((value >> 24) & 0xFF));
    }

    private static void WriteInt64(List<byte> bytes, long value)
    {
        for (int i = 0; i < 8; i++)
            bytes.Add((byte)((value >> (8 * i)) & 0xFF));
    }

    private static void WriteInt32(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value & 0xFF);
        bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
        bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
        bytes[offset + 3] = (byte)((value >> 24) & 0xFF);
    }
}

