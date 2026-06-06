using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Npgsql;
using Renci.SshNet;

namespace A2DebugPacketTool;

public partial class MainForm : Form
{
    private const int DefaultPort = 40133;
    private const int DefaultSelfId = 1001;
    private const int DefaultBossEntityId = 9001;
    private const int DefaultDungeonId = 620021;
    private const int DefaultBossCode = 2301059;
    private const int DefaultSkillCode = 11000000;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly record struct MemberSeed(int EntityId, string Name, int JobCode, int ServerId, int Level, int CombatPower);

    private static readonly MemberSeed[] DefaultPartyMembers =
    {
        new(1001, "남힐", 29, 1002, 55, 464812),
        new(2001, "김찍", 9, 1002, 55, 552137),
        new(2002, "자피", 33, 1003, 55, 465174),
        new(2003, "김살성", 17, 2001, 55, 537321),
    };

    private readonly PacketMemberEditor _self = new("내 캐릭터", DefaultSelfId, "남힐", 29, 1002, 55, 464812);
    private readonly List<PartyRow> _partyRows = new();
    private List<JobOption> _jobs = JobOption.Defaults();
    private readonly List<ServerOption> _servers = ServerOption.Defaults();
    private SshClient? _sshClient;
    private ForwardedPortLocal? _sshTunnel;
    private int _sshForwardedLocalPort;
    private string _sshTunnelSignature = "";
    private readonly Button _appendJsonButton;
    private readonly NumericUpDown _deltaTime;

    private sealed record CommandFrameBatch(MockCommand Command, List<byte[]> Frames, int Order);

    public MainForm()
    {
        InitializeComponent();

        _deltaTime = new NumericUpDown
        {
            Minimum = 0,
            Maximum = 600000,
            Increment = 100,
            Width = 100,
        };
        _appendJsonButton = new Button
        {
            AutoSize = true,
            Text = "JSON 추가",
            Margin = new Padding(8, 4, 4, 4),
        };
        _opcodeLayout.RowCount = Math.Max(_opcodeLayout.RowCount, 3);
        _opcodeLayout.RowStyles.Add(new RowStyle());
        _opcodeLayout.Controls.Add(_appendJsonButton, 0, 2);
        _jsonGroup.Text = "JSONL 구조";
        _refreshJsonButton.Text = "JSONL 갱신";
        _sendJsonButton.Text = "JSONL 보내기";
        _sendJsonFileButton.Text = "JSONL 파일 보내기";

        _dungeonId.Value = DefaultDungeonId;
        _bossEntityId.Value = DefaultBossEntityId;
        _bossCode.Value = DefaultBossCode;
        _skillCode.Value = DefaultSkillCode;
        _meterPath.Text = FindDefaultMeterExePath();
        _self.AddControls(_selfPanel);
        _selfPanel.Controls.Add(_selfInfoButton);
        _selfPanel.Controls.Add(_combatPowerButton);
        _useSshTunnel.CheckedChanged += (_, _) => UpdateSshControlState();
        _launchMeterButton.Click += async (_, _) => await LaunchMeterAsync();
        _browseMeterButton.Click += (_, _) => BrowseMeter();
        _loadJobsButton.Click += async (_, _) => await LoadJobsAsync();
        _browseSshKeyButton.Click += (_, _) => BrowseSshKey();
        _useSshKey.CheckedChanged += (_, _) => UpdateSshControlState();
        _selfInfoButton.Click += async (_, _) => await SendCommandsAsync(new[] { BuildMemberCommand(_self, "SelfInfo") });
        _combatPowerButton.Click += async (_, _) => await SendCommandsAsync(new[] { BuildMemberCommand(_self, "CombatPower") });
        _sendPartyListButton.Click += async (_, _) => await SendCommandsAsync(new[] { BuildPartyCommand("PartyList") });
        _sendPartyUpdateButton.Click += async (_, _) => await SendCommandsAsync(new[] { BuildPartyCommand("PartyUpdate") });
        _refreshJsonButton.Click += (_, _) => RenderSelectedOpcodeJson();
        _appendJsonButton.Click += (_, _) => AppendSelectedOpcodeJson();
        _sendSelectedOpcodeButton.Click += async (_, _) => await SendSelectedOpcodeAsync();
        _combatSetupButton.Click += async (_, _) => await SendCombatSetupAsync();
        _hitButton.Click += async (_, _) => await SendCommandsAsync(new[] { BuildOpcodeCommand(MockOpcode.Damage) });
        _killButton.Click += async (_, _) => await SendCommandsAsync(new[] { BuildOpcodeCommand(MockOpcode.BossHp, hp: 0), BuildOpcodeCommand(MockOpcode.EntityRemoved) });
        _sendJsonButton.Click += async (_, _) => await SendJsonAsync();
        _sendJsonFileButton.Click += async (_, _) => await SendJsonFileAsync();
        _searchDungeonButton.Click += async (_, _) => await SearchDungeonAsync();
        _searchBossButton.Click += async (_, _) => await SearchBossAsync();
        _searchSkillButton.Click += async (_, _) => await SearchSkillAsync();
        _clearLogButton.Click += (_, _) => _log.Clear();
        UpdateSshControlState();
        ApplyMemberOptionsToEditors();
        _addPartyButton.Click += (_, _) => AddPartyRow();
        foreach (var name in Enum.GetNames<MockOpcode>()) _opcode.Items.Add(name);
        _opcode.SelectedItem = MockOpcode.Damage.ToString();
        _opcode.SelectedIndexChanged += (_, _) =>
        {
            RefreshOpcodeDetails();
            RenderSelectedOpcodeJson();
        };

        AddDefaultPartyRows();
        RefreshOpcodeDetails();
        RenderSelectedOpcodeJson();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        StopSshTunnel();
        base.OnFormClosed(e);
    }

    private void AddDefaultPartyRows()
    {
        foreach (var member in DefaultPartyMembers)
            AddPartyRow(member);
    }

    private void AddPartyRow()
        => AddPartyRow(null);

    private void AddPartyRow(MemberSeed? seed)
    {
        if (_partyRows.Count >= 7)
        {
            Log("파티원은 7명까지 추가할 수 있습니다.");
            return;
        }
        int index = _partyRows.Count + 1;
        var member = seed ?? new MemberSeed(2000 + index, $"Party{index}", 5 + (index % 8) * 4, 1001, 55, 500000 + index * 10000);
        var row = new PartyRow(index, member.EntityId, member.Name, member.JobCode, member.ServerId, member.Level, member.CombatPower);
        row.SetJobs(_jobs);
        row.SetServers(_servers);
        row.LeftClicked += async (_, _) => await SendPartyMemberLeftAsync(row);
        row.KickClicked += async (_, _) => await SendPartyMemberKickAsync(row);
        _partyRows.Add(row);
        _partyList.Controls.Add(row.Panel);
    }

    private async Task LoadJobsAsync()
    {
        try
        {
            var loaded = new List<JobOption>();
            await using var conn = new NpgsqlConnection(await BuildDbConnectionStringAsync());
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                SELECT job_code, MIN(job) AS job
                  FROM game_skills
                 WHERE job_code BETWEEN 1 AND 40
                   AND job IS NOT NULL
                   AND job <> ''
                 GROUP BY job_code
                 ORDER BY job_code
                """, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int code = reader.GetInt32(0);
                string name = reader.IsDBNull(1) ? $"Job {code}" : reader.GetString(1);
                loaded.Add(new JobOption(code, name));
            }

            if (loaded.Count == 0)
            {
                Log("DB job rows are empty.");
                return;
            }

            _jobs = loaded;
            ApplyMemberOptionsToEditors();
            Log($"jobs loaded from DB: {_jobs.Count}");
        }
        catch (Exception ex)
        {
            Log("job load failed: " + ex.Message);
        }
    }

    private async Task SearchDungeonAsync()
    {
        try
        {
            var items = new List<GameDataSearchItem>();
            await using var conn = new NpgsqlConnection(await BuildDbConnectionStringAsync());
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                SELECT id, asset_name, base_name, tier_label
                  FROM game_dungeons
                 ORDER BY base_name, tier_label, id
                """, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int id = reader.GetInt32(0);
                string assetName = reader.IsDBNull(1) ? "" : reader.GetString(1);
                string baseName = reader.IsDBNull(2) ? "" : reader.GetString(2);
                string tier = reader.IsDBNull(3) ? "" : reader.GetString(3);
                string name = DisplayDungeonName(baseName, tier);
                items.Add(new GameDataSearchItem(id, name, assetName));
            }

            ApplySearchResult("던전 검색", items, item =>
            {
                SetNumericValue(_dungeonId, item.Id);
                RenderSelectedOpcodeJson();
            });
        }
        catch (Exception ex)
        {
            Log("dungeon search failed: " + ex.Message);
        }
    }

    private async Task SearchBossAsync()
    {
        try
        {
            var items = new List<GameDataSearchItem>();
            await using var conn = new NpgsqlConnection(await BuildDbConnectionStringAsync());
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                SELECT gm.id, gm.name, gm.level, gm.dungeon_id, gd.base_name, gd.tier_label
                  FROM game_mobs gm
                  LEFT JOIN game_dungeons gd ON gd.id = gm.dungeon_id
                 WHERE gm.is_boss = 1
                 ORDER BY COALESCE(gd.base_name, ''), gm.name, gm.id
                """, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int id = reader.GetInt32(0);
                string name = reader.IsDBNull(1) ? "" : reader.GetString(1);
                int level = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);
                int? dungeonId = reader.IsDBNull(3) ? null : reader.GetInt32(3);
                string dungeonName = reader.IsDBNull(4) ? "" : reader.GetString(4);
                string tier = reader.IsDBNull(5) ? "" : reader.GetString(5);
                string detail = $"Lv {level}";
                if (dungeonId.HasValue)
                    detail += $" / {DisplayDungeonName(dungeonName, tier)} ({dungeonId.Value})";
                items.Add(new GameDataSearchItem(id, name, detail, dungeonId));
            }

            ApplySearchResult("보스 검색", items, item =>
            {
                SetNumericValue(_bossCode, item.Id);
                if (item.DungeonId.HasValue)
                    SetNumericValue(_dungeonId, item.DungeonId.Value);
                RenderSelectedOpcodeJson();
            });
        }
        catch (Exception ex)
        {
            Log("boss search failed: " + ex.Message);
        }
    }

    private async Task SearchSkillAsync()
    {
        try
        {
            var items = new List<GameDataSearchItem>();
            await using var conn = new NpgsqlConnection(await BuildDbConnectionStringAsync());
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand("""
                SELECT code, job, name, job_code, skill_type, attack_type, grade
                  FROM game_skills
                 ORDER BY job_code, name, code
                """, conn);
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                int code = reader.GetInt32(0);
                string job = reader.IsDBNull(1) ? "" : reader.GetString(1);
                string name = reader.IsDBNull(2) ? "" : reader.GetString(2);
                int jobCode = reader.IsDBNull(3) ? 0 : reader.GetInt32(3);
                string skillType = reader.IsDBNull(4) ? "" : reader.GetString(4);
                string attackType = reader.IsDBNull(5) ? "" : reader.GetString(5);
                string grade = reader.IsDBNull(6) ? "" : reader.GetString(6);
                string detail = $"{job} ({jobCode}) / {skillType} / {attackType} / grade {grade}";
                items.Add(new GameDataSearchItem(code, name, detail));
            }

            ApplySearchResult("스킬 검색", items, item =>
            {
                SetNumericValue(_skillCode, item.Id);
                RenderSelectedOpcodeJson();
            });
        }
        catch (Exception ex)
        {
            Log("skill search failed: " + ex.Message);
        }
    }

    private void ApplySearchResult(string title, List<GameDataSearchItem> items, Action<GameDataSearchItem> apply)
    {
        if (items.Count == 0)
        {
            Log($"{title}: rows are empty.");
            return;
        }

        using var form = new GameDataSearchForm(title, items);
        if (form.ShowDialog(this) != DialogResult.OK || form.SelectedItem is null)
            return;

        apply(form.SelectedItem);
        Log($"{title} applied: {form.SelectedItem.Id} {form.SelectedItem.Name}");
    }

    private async Task<string> BuildDbConnectionStringAsync()
    {
        string host = _dbHost.Text.Trim();
        int port = (int)_dbPort.Value;
        if (_useSshTunnel.Checked)
        {
            await EnsureSshTunnelAsync();
            host = "127.0.0.1";
            port = _sshForwardedLocalPort;
        }

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = _dbName.Text.Trim(),
            Username = _dbUser.Text.Trim(),
            Password = _dbPassword.Text,
            Timeout = 5,
            CommandTimeout = 15,
        };
        return builder.ConnectionString;
    }

    private void ApplyMemberOptionsToEditors()
    {
        _self.SetJobs(_jobs);
        _self.SetServers(_servers);
        foreach (var row in _partyRows)
        {
            row.SetJobs(_jobs);
            row.SetServers(_servers);
        }
    }

    private async Task LaunchMeterAsync()
    {
        string path = _meterPath.Text.Trim();
        if (!File.Exists(path))
        {
            Log("A2Meter.exe path not found.");
            return;
        }
        string arguments = $"--port {(int)_port.Value}";
        if (_enableUpload.Checked)
            arguments += " --enable-upload";

        var start = new ProcessStartInfo(path, arguments)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? AppContext.BaseDirectory,
        };
        Process.Start(start);
        Log($"launched: {path} {arguments}");
        await Task.Delay(500);
    }

    private async Task SendSelectedOpcodeAsync()
    {
        var opcode = Enum.Parse<MockOpcode>(_opcode.SelectedItem?.ToString() ?? nameof(MockOpcode.Damage));
        await SendCommandsAsync(new[] { BuildOpcodeCommand(opcode) });
    }

    private async Task SendCombatSetupAsync()
    {
        var commands = new List<MockCommand>
        {
            BuildOpcodeCommand(MockOpcode.DungeonEnter),
            BuildMemberCommand(_self, "SelfInfo"),
            BuildMemberCommand(_self, "CombatPower"),
            BuildPartyCommand("PartyList"),
            BuildOpcodeCommand(MockOpcode.MobSpawn),
            BuildOpcodeCommand(MockOpcode.BossHp),
        };
        await SendCommandsAsync(commands);
    }

    private async Task SendPartyMemberLeftAsync(PartyRow row)
    {
        _partyRows.Remove(row);
        _partyList.Controls.Remove(row.Panel);
        row.Panel.Dispose();
        await SendCommandsAsync(new[] { BuildPartyCommand("PartyUpdate") });
    }

    private async Task SendPartyMemberKickAsync(PartyRow row)
    {
        _partyRows.Remove(row);
        _partyList.Controls.Remove(row.Panel);
        row.Panel.Dispose();
        await SendCommandsAsync(new[] { new MockCommand { Opcode = "PartyKick" }, BuildPartyCommand("PartyUpdate") });
    }

    private async Task SendJsonAsync()
    {
        try
        {
            var commands = ParseJsonLines(_json.Text);
            await SendCommandsAsync(commands, honorDeltaTime: true);
        }
        catch (Exception ex)
        {
            Log("JSONL parse/send failed: " + ex.Message);
        }
    }

    private async Task SendJsonFileAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "JSONL 파일 선택",
            Filter = "JSONL files (*.jsonl;*.json)|*.jsonl;*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            string path = dialog.FileName;
            var commands = ParseJsonLines(File.ReadLines(path));
            Log($"JSONL file parsed: {Path.GetFileName(path)} rows={commands.Count}");
            await SendCommandsAsync(commands, honorDeltaTime: true);
        }
        catch (Exception ex)
        {
            Log("JSONL file parse/send failed: " + ex.Message);
        }
    }

    private async Task SendCommandsAsync(IEnumerable<MockCommand> commands, bool honorDeltaTime = false)
    {
        var batches = new List<CommandFrameBatch>();
        int frameCount = 0;
        int order = 0;
        foreach (var command in commands)
        {
            var built = PacketBuilder.Build(command, GetPartyMembers()).ToList();
            Log($"send {FormatCommandForLog(command)} -> frames={built.Count}"
                + (built.Count > 0 ? $" bytes={string.Join(",", built.Select(f => f.Length))}" : ""));
            if (built.Count == 0)
                continue;

            frameCount += built.Count;
            batches.Add(new CommandFrameBatch(command, built, order++));
        }
        if (frameCount == 0)
        {
            Log("no frames built.");
            return;
        }
        await SendFramesOverTcpAsync(batches, (int)_port.Value, honorDeltaTime);
        Log($"sent tcp commands={batches.Count} frames={frameCount}");
    }

    private static string FormatCommandForLog(MockCommand c)
    {
        var parts = new List<string> { c.Opcode };
        Add("entity", c.EntityId);
        Add("actor", c.ActorId);
        Add("target", c.TargetId);
        Add("bossCode", c.BossCode);
        Add("hp", c.Hp);
        Add("skill", c.SkillCode);
        Add("damage", c.Damage);
        Add("dungeon", c.DungeonId);
        Add("stage", c.Stage);
        Add("buff", c.BuffId);
        Add("caster", c.CasterId);
        if (c.DeltaTime > 0) parts.Add($"deltaTime={c.DeltaTime}ms");
        if (c.Crit) parts.Add("crit=true");
        if (!string.IsNullOrWhiteSpace(c.Name)) parts.Add($"name={c.Name}");
        if (c.Members is { Count: > 0 }) parts.Add($"members={c.Members.Count}");
        return string.Join(" ", parts);

        void Add(string name, int value)
        {
            if (value != 0) parts.Add($"{name}={value}");
        }
    }

    private async Task SendFramesOverTcpAsync(IReadOnlyList<CommandFrameBatch> batches, int port, bool honorDeltaTime)
    {
        using var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start(1);
        using var client = new TcpClient(AddressFamily.InterNetwork) { NoDelay = true };
        var connectTask = client.ConnectAsync(IPAddress.Loopback, port);
        using var server = await listener.AcceptTcpClientAsync();
        await connectTask;
        server.NoDelay = true;

        await using var clientStream = client.GetStream();
        await using var serverStream = server.GetStream();
        using var cts = new CancellationTokenSource();
        var drain = DrainAsync(clientStream, cts.Token);

        var orderedBatches = honorDeltaTime
            ? batches.OrderBy(b => b.Command.DeltaTime).ThenBy(b => b.Order).ToList()
            : batches;
        var playbackClock = honorDeltaTime ? Stopwatch.StartNew() : null;
        foreach (var batch in orderedBatches)
        {
            if (playbackClock != null)
            {
                int waitMs = Math.Max(0, batch.Command.DeltaTime - (int)playbackClock.ElapsedMilliseconds);
                if (waitMs > 0)
                {
                    Log($"wait {waitMs}ms until absolute deltaTime={batch.Command.DeltaTime} before {batch.Command.Opcode}");
                    await Task.Delay(waitMs);
                }
            }

            foreach (var frame in batch.Frames)
            {
                await serverStream.WriteAsync(frame);
                await serverStream.FlushAsync();
                if (!honorDeltaTime)
                    await Task.Delay(25);
            }
        }

        cts.Cancel();
        try { await drain; } catch { }
    }

    private static async Task DrainAsync(NetworkStream stream, CancellationToken ct)
    {
        byte[] buffer = new byte[4096];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int read = await stream.ReadAsync(buffer, ct);
                if (read == 0) break;
            }
        }
        catch { }
    }

    private MockCommand BuildOpcodeCommand(MockOpcode opcode, int? hp = null)
    {
        var command = opcode switch
        {
            MockOpcode.DungeonEnter => new MockCommand { Opcode = "DungeonEnter", DungeonId = (int)_dungeonId.Value, Stage = (int)_stage.Value },
            MockOpcode.DungeonLeave => new MockCommand { Opcode = "DungeonLeave" },
            MockOpcode.SelfInfo => BuildMemberCommand(_self, "SelfInfo"),
            MockOpcode.CombatPower => BuildMemberCommand(_self, "CombatPower"),
            MockOpcode.PartyList => BuildPartyCommand("PartyList"),
            MockOpcode.PartyUpdate => BuildPartyCommand("PartyUpdate"),
            MockOpcode.PartyRequest => BuildMemberCommand(_self, "PartyRequest"),
            MockOpcode.PartyAccept => BuildMemberCommand(_self, "PartyAccept"),
            MockOpcode.PartyLeft => new MockCommand { Opcode = "PartyLeft" },
            MockOpcode.PartyKick => new MockCommand { Opcode = "PartyKick" },
            MockOpcode.MobSpawn => new MockCommand { Opcode = "MobSpawn", EntityId = (int)_bossEntityId.Value, BossCode = (int)_bossCode.Value, Hp = (int)_bossHp.Value },
            MockOpcode.Damage => new MockCommand { Opcode = "Damage", ActorId = (int)_actorId.Value, TargetId = (int)_targetId.Value, SkillCode = (int)_skillCode.Value, Damage = (int)_damage.Value, Crit = _crit.Checked },
            MockOpcode.BossHp => new MockCommand { Opcode = "BossHp", EntityId = (int)_bossEntityId.Value, Hp = hp ?? (int)_bossHp.Value },
            MockOpcode.EntityRemoved => new MockCommand { Opcode = "EntityRemoved", EntityId = (int)_bossEntityId.Value },
            MockOpcode.Buff => new MockCommand { Opcode = "Buff", EntityId = (int)_targetId.Value, BuffId = (int)_buffId.Value, DurationMs = (int)_duration.Value, CasterId = (int)_actorId.Value },
            _ => new MockCommand { Opcode = opcode.ToString() },
        };
        command.DeltaTime = (int)_deltaTime.Value;
        return command;
    }

    private MockCommand BuildMemberCommand(PacketMemberEditor editor, string opcode)
        => new()
        {
            DeltaTime = (int)_deltaTime.Value,
            Opcode = opcode,
            EntityId = editor.EntityId,
            ActorId = editor.EntityId,
            Name = editor.NameText,
            JobCode = editor.JobCode,
            ServerId = editor.ServerId,
            Level = editor.Level,
            CombatPower = editor.CombatPower,
        };

    private MockCommand BuildMemberCommand(PartyRow row, string opcode)
        => new()
        {
            DeltaTime = (int)_deltaTime.Value,
            Opcode = opcode,
            EntityId = row.EntityId,
            ActorId = row.EntityId,
            Name = row.NameText,
            JobCode = row.JobCode,
            ServerId = row.ServerId,
            Level = row.Level,
            CombatPower = row.CombatPower,
        };

    private MockCommand BuildPartyCommand(string opcode)
        => new()
        {
            DeltaTime = (int)_deltaTime.Value,
            Opcode = opcode,
            Members = GetPartyMembers(),
            DungeonId = (int)_dungeonId.Value,
            Stage = (int)_stage.Value,
        };

    private List<MockMember> GetPartyMembers()
    {
        var members = new List<MockMember>();
        foreach (var row in _partyRows)
        {
            members.Add(new MockMember
            {
                EntityId = row.EntityId,
                Name = row.NameText,
                JobCode = row.JobCode,
                ServerId = row.ServerId,
                Level = row.Level,
                CombatPower = row.CombatPower,
            });
        }
        return members;
    }

    private void RefreshOpcodeDetails()
    {
        var opcode = Enum.Parse<MockOpcode>(_opcode.SelectedItem?.ToString() ?? nameof(MockOpcode.Damage));
        _opcodeDetails.SuspendLayout();
        try
        {
            _opcodeDetails.Controls.Clear();
            AddOpcodeField("deltaTime(ms)", _deltaTime);
            switch (opcode)
            {
                case MockOpcode.DungeonEnter:
                    AddOpcodeField("Dungeon", _dungeonId);
                    AddOpcodeField("Stage", _stage);
                    break;
                case MockOpcode.PartyList:
                case MockOpcode.PartyUpdate:
                    AddOpcodeField("Dungeon", _dungeonId);
                    AddOpcodeField("Stage", _stage);
                    AddOpcodeNote("파티원 섹션의 현재 멤버 목록을 사용합니다.");
                    break;
                case MockOpcode.MobSpawn:
                    AddOpcodeField("Boss Entity", _bossEntityId);
                    AddOpcodeField("Mob/Boss Code", _bossCode);
                    AddOpcodeField("HP", _bossHp);
                    break;
                case MockOpcode.Damage:
                    AddOpcodeField("Actor", _actorId);
                    AddOpcodeField("Target", _targetId);
                    AddOpcodeField("Skill", _skillCode);
                    AddOpcodeField("Damage", _damage);
                    _opcodeDetails.Controls.Add(_crit);
                    break;
                case MockOpcode.BossHp:
                    AddOpcodeField("Boss Entity", _bossEntityId);
                    AddOpcodeField("HP", _bossHp);
                    break;
                case MockOpcode.EntityRemoved:
                    AddOpcodeField("Boss Entity", _bossEntityId);
                    break;
                case MockOpcode.Buff:
                    AddOpcodeField("Target", _targetId);
                    AddOpcodeField("Caster", _actorId);
                    AddOpcodeField("Buff", _buffId);
                    AddOpcodeField("Duration", _duration);
                    break;
                case MockOpcode.SelfInfo:
                case MockOpcode.CombatPower:
                case MockOpcode.PartyRequest:
                case MockOpcode.PartyAccept:
                    AddOpcodeNote("내 캐릭터 섹션의 값을 사용합니다.");
                    break;
                default:
                    AddOpcodeNote("추가 입력값이 없습니다.");
                    break;
            }
        }
        finally
        {
            _opcodeDetails.ResumeLayout();
        }
    }

    private void AddOpcodeField(string text, Control control)
    {
        _opcodeDetails.Controls.Add(Label(text));
        _opcodeDetails.Controls.Add(control);
    }

    private void AddOpcodeNote(string text)
        => _opcodeDetails.Controls.Add(new Label { Text = text, AutoSize = true, Margin = new Padding(8, 8, 2, 2), ForeColor = Color.DimGray });

    private void RenderSelectedOpcodeJson()
    {
        var opcode = Enum.Parse<MockOpcode>(_opcode.SelectedItem?.ToString() ?? nameof(MockOpcode.Damage));
        _json.Text += ToJsonLine(BuildOpcodeCommand(opcode)) + Environment.NewLine;
    }

    private void AppendSelectedOpcodeJson()
    {
        var opcode = Enum.Parse<MockOpcode>(_opcode.SelectedItem?.ToString() ?? nameof(MockOpcode.Damage));
        if (_json.TextLength > 0 && !_json.Text.EndsWith(Environment.NewLine, StringComparison.Ordinal))
            _json.AppendText(Environment.NewLine);
        _json.AppendText(ToJsonLine(BuildOpcodeCommand(opcode)) + Environment.NewLine);
    }

    private static string ToJsonLine(MockCommand c)
        => JsonSerializer.Serialize(ToJsonShape(c));

    private static object ToJsonShape(MockCommand c)
        => c.Opcode switch
        {
            "SelfInfo" => new { deltaTime = c.DeltaTime, c.Opcode, c.EntityId, c.Name, c.JobCode, c.ServerId, c.Level, c.CombatPower },
            "CombatPower" => new { deltaTime = c.DeltaTime, c.Opcode, c.EntityId, c.CombatPower },
            "PartyList" => new { deltaTime = c.DeltaTime, c.Opcode, c.Members },
            "PartyUpdate" => new { deltaTime = c.DeltaTime, c.Opcode, c.DungeonId, c.Stage, c.Members },
            "PartyRequest" => new { deltaTime = c.DeltaTime, c.Opcode, c.EntityId, c.Name, c.JobCode, c.ServerId, c.Level, c.CombatPower },
            "PartyAccept" => new { deltaTime = c.DeltaTime, c.Opcode, c.EntityId, c.Name, c.JobCode, c.ServerId, c.Level, c.CombatPower },
            "DungeonEnter" => new { deltaTime = c.DeltaTime, c.Opcode, c.DungeonId, c.Stage },
            "MobSpawn" => new { deltaTime = c.DeltaTime, c.Opcode, c.EntityId, c.BossCode, c.Hp },
            "Damage" => new { deltaTime = c.DeltaTime, c.Opcode, c.ActorId, c.TargetId, c.SkillCode, c.Damage, c.Crit },
            "BossHp" => new { deltaTime = c.DeltaTime, c.Opcode, c.EntityId, c.Hp },
            "EntityRemoved" => new { deltaTime = c.DeltaTime, c.Opcode, c.EntityId },
            "Buff" => new { deltaTime = c.DeltaTime, c.Opcode, c.EntityId, c.BuffId, c.DurationMs, c.CasterId },
            _ => new { deltaTime = c.DeltaTime, c.Opcode },
        };

    private static List<MockCommand> ParseJsonLines(string text)
        => ParseJsonLines(text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));

    private static List<MockCommand> ParseJsonLines(IEnumerable<string> lines)
    {
        var commands = new List<MockCommand>();
        int lineNo = 0;
        foreach (var rawLine in lines)
        {
            lineNo++;
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in doc.RootElement.EnumerateArray())
                    commands.Add(ParseCommand(item));
                continue;
            }

            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new FormatException($"line {lineNo}: JSON object expected.");

            commands.Add(ParseCommand(doc.RootElement));
        }

        if (commands.Count == 0)
            throw new FormatException("JSONL rows are empty.");

        return commands;
    }

    private static MockCommand ParseCommand(JsonElement e)
    {
        var command = new MockCommand();
        if (TryGetJsonProperty(e, "deltaTime", out var delta)) command.DeltaTime = Math.Max(0, delta.GetInt32());
        if (TryGetJsonProperty(e, "opcode", out var opcode)) command.Opcode = opcode.GetString() ?? "";
        if (TryGetJsonProperty(e, "entityId", out var entityId)) command.EntityId = entityId.GetInt32();
        if (TryGetJsonProperty(e, "actorId", out var actorId)) command.ActorId = actorId.GetInt32();
        if (TryGetJsonProperty(e, "targetId", out var targetId)) command.TargetId = targetId.GetInt32();
        if (TryGetJsonProperty(e, "name", out var name)) command.Name = name.GetString() ?? "";
        if (TryGetJsonProperty(e, "jobCode", out var job)) command.JobCode = job.GetInt32();
        if (TryGetJsonProperty(e, "serverId", out var server)) command.ServerId = server.GetInt32();
        if (TryGetJsonProperty(e, "level", out var level)) command.Level = level.GetInt32();
        if (TryGetJsonProperty(e, "combatPower", out var cp)) command.CombatPower = cp.GetInt32();
        if (TryGetJsonProperty(e, "dungeonId", out var dungeon)) command.DungeonId = dungeon.GetInt32();
        if (TryGetJsonProperty(e, "stage", out var stage)) command.Stage = stage.GetInt32();
        if (TryGetJsonProperty(e, "bossCode", out var bossCode)) command.BossCode = bossCode.GetInt32();
        if (TryGetJsonProperty(e, "hp", out var hp)) command.Hp = hp.GetInt32();
        if (TryGetJsonProperty(e, "skillCode", out var skill)) command.SkillCode = skill.GetInt32();
        if (TryGetJsonProperty(e, "damage", out var dmg)) command.Damage = dmg.GetInt32();
        if (TryGetJsonProperty(e, "crit", out var crit)) command.Crit = crit.GetBoolean();
        if (TryGetJsonProperty(e, "buffId", out var buff)) command.BuffId = buff.GetInt32();
        if (TryGetJsonProperty(e, "durationMs", out var dur)) command.DurationMs = dur.GetInt32();
        if (TryGetJsonProperty(e, "casterId", out var caster)) command.CasterId = caster.GetInt32();
        if (TryGetJsonProperty(e, "members", out var members) && members.ValueKind == JsonValueKind.Array)
        {
            command.Members = new List<MockMember>();
            foreach (var m in members.EnumerateArray())
            {
                command.Members.Add(new MockMember
                {
                    EntityId = GetInt(m, "entityId"),
                    Name = GetString(m, "name"),
                    JobCode = GetInt(m, "jobCode"),
                    ServerId = GetInt(m, "serverId"),
                    Level = GetInt(m, "level"),
                    CombatPower = GetInt(m, "combatPower"),
                });
            }
        }
        return command;
    }

    private static bool TryGetJsonProperty(JsonElement e, string name, out JsonElement value)
    {
        if (e.TryGetProperty(name, out value))
            return true;

        foreach (var property in e.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static int GetInt(JsonElement e, string name) => TryGetJsonProperty(e, name, out var v) ? v.GetInt32() : 0;
    private static string GetString(JsonElement e, string name) => TryGetJsonProperty(e, name, out var v) ? v.GetString() ?? "" : "";

    private void BrowseMeter()
    {
        using var dialog = new OpenFileDialog { Filter = "A2Meter.exe|A2Meter.exe|Executable|*.exe|All files|*.*", FileName = "A2Meter.exe" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _meterPath.Text = dialog.FileName;
    }

    private void BrowseSshKey()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "SSH private key|id_*;*.pem;*.key;*.ppk|All files|*.*",
            Title = "SSH 키 선택",
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _sshKeyPath.Text = dialog.FileName;
    }

    private void UpdateSshControlState()
    {
        bool enabled = _useSshTunnel.Checked;
        bool useKey = enabled && _useSshKey.Checked;
        _sshHost.Enabled = enabled;
        _sshPort.Enabled = enabled;
        _sshUser.Enabled = enabled;
        _sshPassword.Enabled = enabled && !_useSshKey.Checked;
        _useSshKey.Enabled = enabled;
        _sshKeyPath.Enabled = useKey;
        _browseSshKeyButton.Enabled = useKey;
        _sshPassphrase.Enabled = useKey;
        if (!enabled)
            StopSshTunnel();
    }

    private Task EnsureSshTunnelAsync()
    {
        if (!_useSshTunnel.Checked)
            return Task.CompletedTask;

        string sshHost = _sshHost.Text.Trim();
        int sshPort = (int)_sshPort.Value;
        string sshUser = _sshUser.Text.Trim();
        string sshPassword = _sshPassword.Text;
        bool useKey = _useSshKey.Checked;
        string keyPath = _sshKeyPath.Text.Trim();
        string passphrase = _sshPassphrase.Text;
        string remoteDbHost = _dbHost.Text.Trim();
        int remoteDbPort = (int)_dbPort.Value;
        string signature = $"{sshHost}:{sshPort}:{sshUser}:{useKey}:{keyPath}:{remoteDbHost}:{remoteDbPort}";

        if (_sshClient?.IsConnected == true && _sshTunnel?.IsStarted == true && _sshTunnelSignature == signature)
            return Task.CompletedTask;

        if (string.IsNullOrWhiteSpace(sshHost))
            throw new InvalidOperationException("SSH Host를 입력하세요.");
        if (string.IsNullOrWhiteSpace(sshUser))
            throw new InvalidOperationException("SSH User를 입력하세요.");
        if (useKey && !File.Exists(keyPath))
            throw new InvalidOperationException("SSH Key 파일을 선택하세요.");
        if (!useKey && string.IsNullOrEmpty(sshPassword))
            throw new InvalidOperationException("SSH Password를 입력하세요.");
        if (string.IsNullOrWhiteSpace(remoteDbHost))
            throw new InvalidOperationException("DB Host를 입력하세요.");

        return Task.Run(() =>
        {
            StopSshTunnel();
            SshClient? client = null;
            ForwardedPortLocal? tunnel = null;
            try
            {
                AuthenticationMethod auth;
                if (useKey)
                {
                    var keyFile = string.IsNullOrEmpty(passphrase)
                        ? new PrivateKeyFile(keyPath)
                        : new PrivateKeyFile(keyPath, passphrase);
                    auth = new PrivateKeyAuthenticationMethod(sshUser, keyFile);
                }
                else
                {
                    auth = new PasswordAuthenticationMethod(sshUser, sshPassword);
                }

                var info = new ConnectionInfo(sshHost, sshPort, sshUser, auth)
                {
                    Timeout = TimeSpan.FromSeconds(10),
                };

                client = new SshClient(info)
                {
                    KeepAliveInterval = TimeSpan.FromSeconds(30),
                };
                client.Connect();

                int localPort = GetFreeTcpPort();
                tunnel = new ForwardedPortLocal("127.0.0.1", (uint)localPort, remoteDbHost, (uint)remoteDbPort);
                client.AddForwardedPort(tunnel);
                tunnel.Start();

                _sshClient = client;
                _sshTunnel = tunnel;
                _sshForwardedLocalPort = localPort;
                _sshTunnelSignature = signature;
                client = null;
                tunnel = null;
                Log($"ssh tunnel started: 127.0.0.1:{localPort} -> {remoteDbHost}:{remoteDbPort}");
            }
            finally
            {
                tunnel?.Dispose();
                if (client?.IsConnected == true)
                    client.Disconnect();
                client?.Dispose();
            }
        });
    }

    private void StopSshTunnel()
    {
        try
        {
            if (_sshTunnel?.IsStarted == true)
                _sshTunnel.Stop();
        }
        catch
        {
        }
        finally
        {
            _sshTunnel?.Dispose();
            _sshTunnel = null;
        }

        try
        {
            if (_sshClient?.IsConnected == true)
                _sshClient.Disconnect();
        }
        catch
        {
        }
        finally
        {
            _sshClient?.Dispose();
            _sshClient = null;
            _sshForwardedLocalPort = 0;
            _sshTunnelSignature = "";
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string DisplayDungeonName(string baseName, string tier)
    {
        if (string.IsNullOrWhiteSpace(tier))
            return string.IsNullOrWhiteSpace(baseName) ? "(unnamed)" : baseName;
        if (string.IsNullOrWhiteSpace(baseName))
            return tier;
        return $"{baseName} {tier}";
    }

    private static void SetNumericValue(NumericUpDown control, int value)
    {
        decimal clamped = Math.Min(control.Maximum, Math.Max(control.Minimum, value));
        control.Value = clamped;
    }

    private static string FindDefaultMeterExePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string candidate = Path.Combine(dir.FullName, "src", "A2Meter", "bin", "Debug", "net8.0-windows", "win-x64", "A2Meter.exe");
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir.FullName, "src", "A2Meter", "bin", "x64", "Debug", "net8.0-windows", "win-x64", "A2Meter.exe");
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir.FullName, "A2Meter.exe");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return "";
    }

    private void Log(string text)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => Log(text));
            return;
        }
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private static Label Label(string text)
        => new() { Text = text, AutoSize = true, Margin = new Padding(8, 8, 2, 2) };

    private void _opcodeTab_Click(object sender, EventArgs e)
    {

    }
}


