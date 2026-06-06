using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

#nullable enable

namespace A2DebugPacketTool;

public partial class MainForm
{
    private IContainer? components;

    private TableLayoutPanel _rootLayout = null!;
    private TabControl _tabControl = null!;
    private TabPage _settingsTab = null!;
    private TabPage _partyTab = null!;
    private TabPage _opcodeTab = null!;
    private TableLayoutPanel _settingsLayout = null!;
    private TableLayoutPanel _opcodePageLayout = null!;

    private GroupBox _launchGroup = null!;
    private FlowLayoutPanel _launchPanel = null!;
    private Label _portLabel = null!;
    private NumericUpDown _port = null!;
    private CheckBox _enableUpload = null!;
    private Button _launchMeterButton = null!;
    private Label _meterPathLabel = null!;
    private TextBox _meterPath = null!;
    private Button _browseMeterButton = null!;

    private GroupBox _databaseGroup = null!;
    private TableLayoutPanel _databaseLayout = null!;
    private Label _dbHostLabel = null!;
    private TextBox _dbHost = null!;
    private Label _dbPortLabel = null!;
    private NumericUpDown _dbPort = null!;
    private Label _dbUserLabel = null!;
    private TextBox _dbUser = null!;
    private Label _dbPasswordLabel = null!;
    private TextBox _dbPassword = null!;
    private Label _dbNameLabel = null!;
    private TextBox _dbName = null!;
    private CheckBox _useSshTunnel = null!;
    private Label _sshHostLabel = null!;
    private TextBox _sshHost = null!;
    private Label _sshPortLabel = null!;
    private NumericUpDown _sshPort = null!;
    private Label _sshUserLabel = null!;
    private TextBox _sshUser = null!;
    private Label _sshPasswordLabel = null!;
    private TextBox _sshPassword = null!;
    private CheckBox _useSshKey = null!;
    private TextBox _sshKeyPath = null!;
    private Button _browseSshKeyButton = null!;
    private Label _sshPassphraseLabel = null!;
    private TextBox _sshPassphrase = null!;
    private Button _loadJobsButton = null!;

    private GroupBox _selfGroup = null!;
    private FlowLayoutPanel _selfPanel = null!;
    private Button _selfInfoButton = null!;
    private Button _combatPowerButton = null!;

    private GroupBox _partyGroup = null!;
    private TableLayoutPanel _partyLayout = null!;
    private FlowLayoutPanel _partyButtonsPanel = null!;
    private FlowLayoutPanel _partyList = null!;
    private Button _addPartyButton = null!;
    private Button _sendPartyListButton = null!;
    private Button _sendPartyUpdateButton = null!;

    private GroupBox _opcodeGroup = null!;
    private TableLayoutPanel _opcodeLayout = null!;
    private FlowLayoutPanel _opcodeHeaderPanel = null!;
    private Label _opcodeLabel = null!;
    private ComboBox _opcode = null!;
    private Button _searchDungeonButton = null!;
    private Button _searchBossButton = null!;
    private Button _searchSkillButton = null!;
    private Button _refreshJsonButton = null!;
    private Button _sendSelectedOpcodeButton = null!;
    private Button _combatSetupButton = null!;
    private Button _hitButton = null!;
    private Button _killButton = null!;
    private FlowLayoutPanel _opcodeDetails = null!;
    private NumericUpDown _dungeonId = null!;
    private NumericUpDown _stage = null!;
    private NumericUpDown _bossEntityId = null!;
    private NumericUpDown _bossCode = null!;
    private NumericUpDown _bossHp = null!;
    private NumericUpDown _actorId = null!;
    private NumericUpDown _targetId = null!;
    private NumericUpDown _skillCode = null!;
    private NumericUpDown _damage = null!;
    private CheckBox _crit = null!;
    private NumericUpDown _buffId = null!;
    private NumericUpDown _duration = null!;

    private GroupBox _jsonGroup = null!;
    private TableLayoutPanel _jsonLayout = null!;
    private FlowLayoutPanel _jsonButtonsPanel = null!;
    private TextBox _json = null!;
    private Button _sendJsonButton = null!;
    private Button _sendJsonFileButton = null!;
    private Button _clearLogButton = null!;
    private RichTextBox _log = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        _rootLayout = new TableLayoutPanel();
        _tabControl = new TabControl();
        _settingsTab = new TabPage();
        _settingsLayout = new TableLayoutPanel();
        _launchGroup = new GroupBox();
        _launchPanel = new FlowLayoutPanel();
        _portLabel = new Label();
        _port = new NumericUpDown();
        _enableUpload = new CheckBox();
        _launchMeterButton = new Button();
        _meterPathLabel = new Label();
        _meterPath = new TextBox();
        _browseMeterButton = new Button();
        _databaseGroup = new GroupBox();
        _databaseLayout = new TableLayoutPanel();
        _dbHostLabel = new Label();
        _dbHost = new TextBox();
        _dbPortLabel = new Label();
        _dbPort = new NumericUpDown();
        _dbUserLabel = new Label();
        _dbUser = new TextBox();
        _dbPasswordLabel = new Label();
        _dbPassword = new TextBox();
        _dbNameLabel = new Label();
        _dbName = new TextBox();
        _useSshTunnel = new CheckBox();
        _sshHostLabel = new Label();
        _sshHost = new TextBox();
        _sshPortLabel = new Label();
        _sshPort = new NumericUpDown();
        _sshUserLabel = new Label();
        _sshUser = new TextBox();
        _sshPasswordLabel = new Label();
        _sshPassword = new TextBox();
        _useSshKey = new CheckBox();
        _sshKeyPath = new TextBox();
        _browseSshKeyButton = new Button();
        _sshPassphraseLabel = new Label();
        _sshPassphrase = new TextBox();
        _loadJobsButton = new Button();
        _selfGroup = new GroupBox();
        _selfPanel = new FlowLayoutPanel();
        _partyTab = new TabPage();
        _partyGroup = new GroupBox();
        _partyLayout = new TableLayoutPanel();
        _partyButtonsPanel = new FlowLayoutPanel();
        _addPartyButton = new Button();
        _sendPartyListButton = new Button();
        _sendPartyUpdateButton = new Button();
        _partyList = new FlowLayoutPanel();
        _opcodeTab = new TabPage();
        _opcodePageLayout = new TableLayoutPanel();
        _opcodeGroup = new GroupBox();
        _opcodeLayout = new TableLayoutPanel();
        _opcodeHeaderPanel = new FlowLayoutPanel();
        _opcodeLabel = new Label();
        _opcode = new ComboBox();
        _searchDungeonButton = new Button();
        _searchBossButton = new Button();
        _searchSkillButton = new Button();
        _refreshJsonButton = new Button();
        _sendSelectedOpcodeButton = new Button();
        _combatSetupButton = new Button();
        _hitButton = new Button();
        _killButton = new Button();
        _opcodeDetails = new FlowLayoutPanel();
        _jsonGroup = new GroupBox();
        _jsonLayout = new TableLayoutPanel();
        _jsonButtonsPanel = new FlowLayoutPanel();
        _sendJsonButton = new Button();
        _sendJsonFileButton = new Button();
        _clearLogButton = new Button();
        _json = new TextBox();
        _log = new RichTextBox();
        _selfInfoButton = new Button();
        _combatPowerButton = new Button();
        _dungeonId = new NumericUpDown();
        _stage = new NumericUpDown();
        _bossEntityId = new NumericUpDown();
        _bossCode = new NumericUpDown();
        _bossHp = new NumericUpDown();
        _actorId = new NumericUpDown();
        _targetId = new NumericUpDown();
        _skillCode = new NumericUpDown();
        _damage = new NumericUpDown();
        _crit = new CheckBox();
        _buffId = new NumericUpDown();
        _duration = new NumericUpDown();
        _rootLayout.SuspendLayout();
        _tabControl.SuspendLayout();
        _settingsTab.SuspendLayout();
        _settingsLayout.SuspendLayout();
        _launchGroup.SuspendLayout();
        _launchPanel.SuspendLayout();
        ((ISupportInitialize)_port).BeginInit();
        _databaseGroup.SuspendLayout();
        _databaseLayout.SuspendLayout();
        ((ISupportInitialize)_dbPort).BeginInit();
        ((ISupportInitialize)_sshPort).BeginInit();
        _selfGroup.SuspendLayout();
        _partyTab.SuspendLayout();
        _partyGroup.SuspendLayout();
        _partyLayout.SuspendLayout();
        _partyButtonsPanel.SuspendLayout();
        _opcodeTab.SuspendLayout();
        _opcodePageLayout.SuspendLayout();
        _opcodeGroup.SuspendLayout();
        _opcodeLayout.SuspendLayout();
        _opcodeHeaderPanel.SuspendLayout();
        _jsonGroup.SuspendLayout();
        _jsonLayout.SuspendLayout();
        _jsonButtonsPanel.SuspendLayout();
        ((ISupportInitialize)_dungeonId).BeginInit();
        ((ISupportInitialize)_stage).BeginInit();
        ((ISupportInitialize)_bossEntityId).BeginInit();
        ((ISupportInitialize)_bossCode).BeginInit();
        ((ISupportInitialize)_bossHp).BeginInit();
        ((ISupportInitialize)_actorId).BeginInit();
        ((ISupportInitialize)_targetId).BeginInit();
        ((ISupportInitialize)_skillCode).BeginInit();
        ((ISupportInitialize)_damage).BeginInit();
        ((ISupportInitialize)_buffId).BeginInit();
        ((ISupportInitialize)_duration).BeginInit();
        SuspendLayout();
        // 
        // _rootLayout
        // 
        _rootLayout.ColumnCount = 1;
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _rootLayout.Controls.Add(_tabControl, 0, 0);
        _rootLayout.Controls.Add(_log, 0, 1);
        _rootLayout.Dock = DockStyle.Fill;
        _rootLayout.Location = new Point(0, 0);
        _rootLayout.Name = "_rootLayout";
        _rootLayout.Padding = new Padding(10);
        _rootLayout.RowCount = 2;
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 72F));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 28F));
        _rootLayout.Size = new Size(1084, 781);
        _rootLayout.TabIndex = 0;
        // 
        // _tabControl
        // 
        _tabControl.Controls.Add(_settingsTab);
        _tabControl.Controls.Add(_partyTab);
        _tabControl.Controls.Add(_opcodeTab);
        _tabControl.Dock = DockStyle.Fill;
        _tabControl.Location = new Point(13, 13);
        _tabControl.Name = "_tabControl";
        _tabControl.SelectedIndex = 0;
        _tabControl.Size = new Size(1058, 541);
        _tabControl.TabIndex = 0;
        // 
        // _settingsTab
        // 
        _settingsTab.Controls.Add(_settingsLayout);
        _settingsTab.Location = new Point(4, 24);
        _settingsTab.Name = "_settingsTab";
        _settingsTab.Padding = new Padding(8);
        _settingsTab.Size = new Size(1050, 513);
        _settingsTab.TabIndex = 0;
        _settingsTab.Text = "기본 설정";
        // 
        // _settingsLayout
        // 
        _settingsLayout.ColumnCount = 1;
        _settingsLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _settingsLayout.Controls.Add(_launchGroup, 0, 0);
        _settingsLayout.Controls.Add(_databaseGroup, 0, 1);
        _settingsLayout.Controls.Add(_selfGroup, 0, 2);
        _settingsLayout.Dock = DockStyle.Fill;
        _settingsLayout.Location = new Point(8, 8);
        _settingsLayout.Name = "_settingsLayout";
        _settingsLayout.RowCount = 3;
        _settingsLayout.RowStyles.Add(new RowStyle());
        _settingsLayout.RowStyles.Add(new RowStyle());
        _settingsLayout.RowStyles.Add(new RowStyle());
        _settingsLayout.Size = new Size(1034, 497);
        _settingsLayout.TabIndex = 0;
        // 
        // _launchGroup
        // 
        _launchGroup.AutoSize = true;
        _launchGroup.Controls.Add(_launchPanel);
        _launchGroup.Dock = DockStyle.Fill;
        _launchGroup.Location = new Point(3, 3);
        _launchGroup.Name = "_launchGroup";
        _launchGroup.Padding = new Padding(10);
        _launchGroup.Size = new Size(1028, 82);
        _launchGroup.TabIndex = 0;
        _launchGroup.TabStop = false;
        _launchGroup.Text = "미터기 실행";
        // 
        // _launchPanel
        // 
        _launchPanel.AutoSize = true;
        _launchPanel.Controls.Add(_portLabel);
        _launchPanel.Controls.Add(_port);
        _launchPanel.Controls.Add(_enableUpload);
        _launchPanel.Controls.Add(_launchMeterButton);
        _launchPanel.Controls.Add(_meterPathLabel);
        _launchPanel.Controls.Add(_meterPath);
        _launchPanel.Controls.Add(_browseMeterButton);
        _launchPanel.Dock = DockStyle.Fill;
        _launchPanel.Location = new Point(10, 26);
        _launchPanel.Name = "_launchPanel";
        _launchPanel.Padding = new Padding(4);
        _launchPanel.Size = new Size(1008, 46);
        _launchPanel.TabIndex = 0;
        // 
        // _portLabel
        // 
        _portLabel.AutoSize = true;
        _portLabel.Location = new Point(12, 12);
        _portLabel.Margin = new Padding(8, 8, 2, 2);
        _portLabel.Name = "_portLabel";
        _portLabel.Size = new Size(29, 15);
        _portLabel.TabIndex = 0;
        _portLabel.Text = "Port";
        // 
        // _port
        // 
        _port.Location = new Point(46, 7);
        _port.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
        _port.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _port.Name = "_port";
        _port.Size = new Size(80, 23);
        _port.TabIndex = 1;
        _port.Value = new decimal(new int[] { 40133, 0, 0, 0 });
        // 
        // _enableUpload
        // 
        _enableUpload.AutoSize = true;
        _enableUpload.Location = new Point(133, 10);
        _enableUpload.Margin = new Padding(4, 6, 8, 4);
        _enableUpload.Name = "_enableUpload";
        _enableUpload.Size = new Size(113, 19);
        _enableUpload.TabIndex = 2;
        _enableUpload.Text = "--enable-upload";
        // 
        // _launchMeterButton
        // 
        _launchMeterButton.AutoSize = true;
        _launchMeterButton.Location = new Point(258, 8);
        _launchMeterButton.Margin = new Padding(4);
        _launchMeterButton.Name = "_launchMeterButton";
        _launchMeterButton.Size = new Size(106, 30);
        _launchMeterButton.TabIndex = 3;
        _launchMeterButton.Text = "Launch A2Meter";
        // 
        // _meterPathLabel
        // 
        _meterPathLabel.AutoSize = true;
        _meterPathLabel.Location = new Point(376, 12);
        _meterPathLabel.Margin = new Padding(8, 8, 2, 2);
        _meterPathLabel.Name = "_meterPathLabel";
        _meterPathLabel.Size = new Size(74, 15);
        _meterPathLabel.TabIndex = 4;
        _meterPathLabel.Text = "A2Meter.exe";
        // 
        // _meterPath
        // 
        _meterPath.Location = new Point(455, 7);
        _meterPath.Name = "_meterPath";
        _meterPath.Size = new Size(398, 23);
        _meterPath.TabIndex = 5;
        // 
        // _browseMeterButton
        // 
        _browseMeterButton.AutoSize = true;
        _browseMeterButton.Location = new Point(860, 8);
        _browseMeterButton.Margin = new Padding(4);
        _browseMeterButton.Name = "_browseMeterButton";
        _browseMeterButton.Size = new Size(75, 30);
        _browseMeterButton.TabIndex = 6;
        _browseMeterButton.Text = "Browse";
        // 
        // _databaseGroup
        // 
        _databaseGroup.AutoSize = true;
        _databaseGroup.Controls.Add(_databaseLayout);
        _databaseGroup.Dock = DockStyle.Fill;
        _databaseGroup.Location = new Point(3, 91);
        _databaseGroup.Name = "_databaseGroup";
        _databaseGroup.Padding = new Padding(10);
        _databaseGroup.Size = new Size(1028, 326);
        _databaseGroup.TabIndex = 1;
        _databaseGroup.TabStop = false;
        _databaseGroup.Text = "DB / SSH";
        // 
        // _databaseLayout
        // 
        _databaseLayout.AutoSize = true;
        _databaseLayout.ColumnCount = 4;
        _databaseLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80F));
        _databaseLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
        _databaseLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 70F));
        _databaseLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
        _databaseLayout.Controls.Add(_dbHostLabel, 0, 0);
        _databaseLayout.Controls.Add(_dbHost, 1, 0);
        _databaseLayout.Controls.Add(_dbPortLabel, 2, 0);
        _databaseLayout.Controls.Add(_dbPort, 3, 0);
        _databaseLayout.Controls.Add(_dbUserLabel, 0, 1);
        _databaseLayout.Controls.Add(_dbUser, 1, 1);
        _databaseLayout.Controls.Add(_dbPasswordLabel, 0, 2);
        _databaseLayout.Controls.Add(_dbPassword, 1, 2);
        _databaseLayout.Controls.Add(_dbNameLabel, 0, 3);
        _databaseLayout.Controls.Add(_dbName, 1, 3);
        _databaseLayout.Controls.Add(_useSshTunnel, 1, 4);
        _databaseLayout.Controls.Add(_sshHostLabel, 0, 5);
        _databaseLayout.Controls.Add(_sshHost, 1, 5);
        _databaseLayout.Controls.Add(_sshPortLabel, 2, 5);
        _databaseLayout.Controls.Add(_sshPort, 3, 5);
        _databaseLayout.Controls.Add(_sshUserLabel, 0, 6);
        _databaseLayout.Controls.Add(_sshUser, 1, 6);
        _databaseLayout.Controls.Add(_sshPasswordLabel, 0, 7);
        _databaseLayout.Controls.Add(_sshPassword, 1, 7);
        _databaseLayout.Controls.Add(_useSshKey, 1, 8);
        _databaseLayout.Controls.Add(_sshKeyPath, 2, 8);
        _databaseLayout.Controls.Add(_browseSshKeyButton, 3, 8);
        _databaseLayout.Controls.Add(_sshPassphraseLabel, 0, 9);
        _databaseLayout.Controls.Add(_sshPassphrase, 1, 9);
        _databaseLayout.Controls.Add(_loadJobsButton, 3, 9);
        _databaseLayout.Dock = DockStyle.Fill;
        _databaseLayout.Location = new Point(10, 26);
        _databaseLayout.Name = "_databaseLayout";
        _databaseLayout.RowCount = 10;
        _databaseLayout.RowStyles.Add(new RowStyle());
        _databaseLayout.RowStyles.Add(new RowStyle());
        _databaseLayout.RowStyles.Add(new RowStyle());
        _databaseLayout.RowStyles.Add(new RowStyle());
        _databaseLayout.RowStyles.Add(new RowStyle());
        _databaseLayout.RowStyles.Add(new RowStyle());
        _databaseLayout.RowStyles.Add(new RowStyle());
        _databaseLayout.RowStyles.Add(new RowStyle());
        _databaseLayout.RowStyles.Add(new RowStyle());
        _databaseLayout.RowStyles.Add(new RowStyle());
        _databaseLayout.Size = new Size(1008, 290);
        _databaseLayout.TabIndex = 0;
        // 
        // _dbHostLabel
        // 
        _dbHostLabel.Anchor = AnchorStyles.Left;
        _dbHostLabel.AutoSize = true;
        _dbHostLabel.Location = new Point(3, 7);
        _dbHostLabel.Name = "_dbHostLabel";
        _dbHostLabel.Size = new Size(32, 15);
        _dbHostLabel.TabIndex = 0;
        _dbHostLabel.Text = "Host";
        // 
        // _dbHost
        // 
        _dbHost.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _dbHost.Location = new Point(83, 3);
        _dbHost.Name = "_dbHost";
        _dbHost.Size = new Size(551, 23);
        _dbHost.TabIndex = 1;
        _dbHost.Text = "localhost";
        // 
        // _dbPortLabel
        // 
        _dbPortLabel.Anchor = AnchorStyles.Left;
        _dbPortLabel.AutoSize = true;
        _dbPortLabel.Location = new Point(640, 7);
        _dbPortLabel.Name = "_dbPortLabel";
        _dbPortLabel.Size = new Size(29, 15);
        _dbPortLabel.TabIndex = 2;
        _dbPortLabel.Text = "Port";
        // 
        // _dbPort
        // 
        _dbPort.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _dbPort.Location = new Point(710, 3);
        _dbPort.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
        _dbPort.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _dbPort.Name = "_dbPort";
        _dbPort.Size = new Size(295, 23);
        _dbPort.TabIndex = 3;
        _dbPort.Value = new decimal(new int[] { 5432, 0, 0, 0 });
        // 
        // _dbUserLabel
        // 
        _dbUserLabel.Anchor = AnchorStyles.Left;
        _dbUserLabel.AutoSize = true;
        _dbUserLabel.Location = new Point(3, 36);
        _dbUserLabel.Name = "_dbUserLabel";
        _dbUserLabel.Size = new Size(30, 15);
        _dbUserLabel.TabIndex = 4;
        _dbUserLabel.Text = "User";
        // 
        // _dbUser
        // 
        _dbUser.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _databaseLayout.SetColumnSpan(_dbUser, 3);
        _dbUser.Location = new Point(83, 32);
        _dbUser.Name = "_dbUser";
        _dbUser.Size = new Size(922, 23);
        _dbUser.TabIndex = 5;
        _dbUser.Text = "a2web";
        // 
        // _dbPasswordLabel
        // 
        _dbPasswordLabel.Anchor = AnchorStyles.Left;
        _dbPasswordLabel.AutoSize = true;
        _dbPasswordLabel.Location = new Point(3, 65);
        _dbPasswordLabel.Name = "_dbPasswordLabel";
        _dbPasswordLabel.Size = new Size(57, 15);
        _dbPasswordLabel.TabIndex = 6;
        _dbPasswordLabel.Text = "Password";
        // 
        // _dbPassword
        // 
        _dbPassword.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _databaseLayout.SetColumnSpan(_dbPassword, 3);
        _dbPassword.Location = new Point(83, 61);
        _dbPassword.Name = "_dbPassword";
        _dbPassword.Size = new Size(922, 23);
        _dbPassword.TabIndex = 7;
        _dbPassword.Text = "a2web";
        _dbPassword.UseSystemPasswordChar = true;
        // 
        // _dbNameLabel
        // 
        _dbNameLabel.Anchor = AnchorStyles.Left;
        _dbNameLabel.AutoSize = true;
        _dbNameLabel.Location = new Point(3, 94);
        _dbNameLabel.Name = "_dbNameLabel";
        _dbNameLabel.Size = new Size(56, 15);
        _dbNameLabel.TabIndex = 8;
        _dbNameLabel.Text = "Database";
        // 
        // _dbName
        // 
        _dbName.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _databaseLayout.SetColumnSpan(_dbName, 3);
        _dbName.Location = new Point(83, 90);
        _dbName.Name = "_dbName";
        _dbName.Size = new Size(922, 23);
        _dbName.TabIndex = 9;
        _dbName.Text = "a2web";
        // 
        // _useSshTunnel
        // 
        _useSshTunnel.AutoSize = true;
        _useSshTunnel.Checked = true;
        _useSshTunnel.CheckState = CheckState.Checked;
        _databaseLayout.SetColumnSpan(_useSshTunnel, 3);
        _useSshTunnel.Location = new Point(83, 119);
        _useSshTunnel.Name = "_useSshTunnel";
        _useSshTunnel.Size = new Size(78, 19);
        _useSshTunnel.TabIndex = 10;
        _useSshTunnel.Text = "Over SSH";
        // 
        // _sshHostLabel
        // 
        _sshHostLabel.Anchor = AnchorStyles.Left;
        _sshHostLabel.AutoSize = true;
        _sshHostLabel.Location = new Point(3, 148);
        _sshHostLabel.Name = "_sshHostLabel";
        _sshHostLabel.Size = new Size(40, 15);
        _sshHostLabel.TabIndex = 11;
        _sshHostLabel.Text = "Server";
        // 
        // _sshHost
        // 
        _sshHost.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _sshHost.Location = new Point(83, 144);
        _sshHost.Name = "_sshHost";
        _sshHost.Size = new Size(551, 23);
        _sshHost.TabIndex = 12;
        _sshHost.Text = "115.68.231.85";
        // 
        // _sshPortLabel
        // 
        _sshPortLabel.Anchor = AnchorStyles.Left;
        _sshPortLabel.AutoSize = true;
        _sshPortLabel.Location = new Point(640, 148);
        _sshPortLabel.Name = "_sshPortLabel";
        _sshPortLabel.Size = new Size(29, 15);
        _sshPortLabel.TabIndex = 13;
        _sshPortLabel.Text = "Port";
        // 
        // _sshPort
        // 
        _sshPort.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _sshPort.Location = new Point(710, 144);
        _sshPort.Maximum = new decimal(new int[] { 65535, 0, 0, 0 });
        _sshPort.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _sshPort.Name = "_sshPort";
        _sshPort.Size = new Size(295, 23);
        _sshPort.TabIndex = 14;
        _sshPort.Value = new decimal(new int[] { 22, 0, 0, 0 });
        // 
        // _sshUserLabel
        // 
        _sshUserLabel.Anchor = AnchorStyles.Left;
        _sshUserLabel.AutoSize = true;
        _sshUserLabel.Location = new Point(3, 177);
        _sshUserLabel.Name = "_sshUserLabel";
        _sshUserLabel.Size = new Size(30, 15);
        _sshUserLabel.TabIndex = 15;
        _sshUserLabel.Text = "User";
        // 
        // _sshUser
        // 
        _sshUser.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _databaseLayout.SetColumnSpan(_sshUser, 3);
        _sshUser.Location = new Point(83, 173);
        _sshUser.Name = "_sshUser";
        _sshUser.Size = new Size(922, 23);
        _sshUser.TabIndex = 16;
        _sshUser.Text = "root";
        // 
        // _sshPasswordLabel
        // 
        _sshPasswordLabel.Anchor = AnchorStyles.Left;
        _sshPasswordLabel.AutoSize = true;
        _sshPasswordLabel.Location = new Point(3, 206);
        _sshPasswordLabel.Name = "_sshPasswordLabel";
        _sshPasswordLabel.Size = new Size(57, 15);
        _sshPasswordLabel.TabIndex = 17;
        _sshPasswordLabel.Text = "Password";
        // 
        // _sshPassword
        // 
        _sshPassword.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _databaseLayout.SetColumnSpan(_sshPassword, 3);
        _sshPassword.Location = new Point(83, 202);
        _sshPassword.Name = "_sshPassword";
        _sshPassword.Size = new Size(922, 23);
        _sshPassword.TabIndex = 18;
        _sshPassword.UseSystemPasswordChar = true;
        // 
        // _useSshKey
        // 
        _useSshKey.Anchor = AnchorStyles.Left;
        _useSshKey.AutoSize = true;
        _useSshKey.Checked = true;
        _useSshKey.CheckState = CheckState.Checked;
        _useSshKey.Location = new Point(83, 234);
        _useSshKey.Name = "_useSshKey";
        _useSshKey.Size = new Size(95, 19);
        _useSshKey.TabIndex = 19;
        _useSshKey.Text = "Use SSH Key";
        // 
        // _sshKeyPath
        // 
        _sshKeyPath.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _sshKeyPath.Location = new Point(640, 232);
        _sshKeyPath.Name = "_sshKeyPath";
        _sshKeyPath.Size = new Size(64, 23);
        _sshKeyPath.TabIndex = 20;
        _sshKeyPath.Text = "C:\\Users\\Administrator\\.ssh\\id_rsa";
        // 
        // _browseSshKeyButton
        // 
        _browseSshKeyButton.Anchor = AnchorStyles.Left;
        _browseSshKeyButton.AutoSize = true;
        _browseSshKeyButton.Location = new Point(710, 231);
        _browseSshKeyButton.Name = "_browseSshKeyButton";
        _browseSshKeyButton.Size = new Size(26, 25);
        _browseSshKeyButton.TabIndex = 21;
        _browseSshKeyButton.Text = "...";
        // 
        // _sshPassphraseLabel
        // 
        _sshPassphraseLabel.Anchor = AnchorStyles.Left;
        _sshPassphraseLabel.AutoSize = true;
        _sshPassphraseLabel.Location = new Point(3, 267);
        _sshPassphraseLabel.Name = "_sshPassphraseLabel";
        _sshPassphraseLabel.Size = new Size(65, 15);
        _sshPassphraseLabel.TabIndex = 22;
        _sshPassphraseLabel.Text = "Passphrase";
        // 
        // _sshPassphrase
        // 
        _sshPassphrase.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        _databaseLayout.SetColumnSpan(_sshPassphrase, 2);
        _sshPassphrase.Location = new Point(83, 263);
        _sshPassphrase.Name = "_sshPassphrase";
        _sshPassphrase.Size = new Size(621, 23);
        _sshPassphrase.TabIndex = 23;
        _sshPassphrase.UseSystemPasswordChar = true;
        // 
        // _loadJobsButton
        // 
        _loadJobsButton.Anchor = AnchorStyles.Right;
        _loadJobsButton.AutoSize = true;
        _loadJobsButton.Location = new Point(935, 262);
        _loadJobsButton.Name = "_loadJobsButton";
        _loadJobsButton.Size = new Size(70, 25);
        _loadJobsButton.TabIndex = 24;
        _loadJobsButton.Text = "Load Jobs";
        // 
        // _selfGroup
        // 
        _selfGroup.AutoSize = true;
        _selfGroup.Controls.Add(_selfPanel);
        _selfGroup.Dock = DockStyle.Fill;
        _selfGroup.Location = new Point(3, 423);
        _selfGroup.Name = "_selfGroup";
        _selfGroup.Padding = new Padding(10);
        _selfGroup.Size = new Size(1028, 71);
        _selfGroup.TabIndex = 2;
        _selfGroup.TabStop = false;
        _selfGroup.Text = "내 캐릭터";
        // 
        // _selfPanel
        // 
        _selfPanel.AutoSize = true;
        _selfPanel.Dock = DockStyle.Fill;
        _selfPanel.Location = new Point(10, 26);
        _selfPanel.Name = "_selfPanel";
        _selfPanel.Padding = new Padding(4);
        _selfPanel.Size = new Size(1008, 35);
        _selfPanel.TabIndex = 0;
        // 
        // _partyTab
        // 
        _partyTab.Controls.Add(_partyGroup);
        _partyTab.Location = new Point(4, 24);
        _partyTab.Name = "_partyTab";
        _partyTab.Padding = new Padding(8);
        _partyTab.Size = new Size(1050, 513);
        _partyTab.TabIndex = 1;
        _partyTab.Text = "파티원 구성";
        // 
        // _partyGroup
        // 
        _partyGroup.Controls.Add(_partyLayout);
        _partyGroup.Dock = DockStyle.Fill;
        _partyGroup.Location = new Point(8, 8);
        _partyGroup.Name = "_partyGroup";
        _partyGroup.Padding = new Padding(10);
        _partyGroup.Size = new Size(1034, 497);
        _partyGroup.TabIndex = 0;
        _partyGroup.TabStop = false;
        _partyGroup.Text = "파티원";
        // 
        // _partyLayout
        // 
        _partyLayout.ColumnCount = 1;
        _partyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _partyLayout.Controls.Add(_partyButtonsPanel, 0, 0);
        _partyLayout.Controls.Add(_partyList, 0, 1);
        _partyLayout.Dock = DockStyle.Fill;
        _partyLayout.Location = new Point(10, 26);
        _partyLayout.Name = "_partyLayout";
        _partyLayout.RowCount = 2;
        _partyLayout.RowStyles.Add(new RowStyle());
        _partyLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _partyLayout.Size = new Size(1014, 461);
        _partyLayout.TabIndex = 0;
        // 
        // _partyButtonsPanel
        // 
        _partyButtonsPanel.AutoSize = true;
        _partyButtonsPanel.Controls.Add(_addPartyButton);
        _partyButtonsPanel.Controls.Add(_sendPartyListButton);
        _partyButtonsPanel.Controls.Add(_sendPartyUpdateButton);
        _partyButtonsPanel.Dock = DockStyle.Fill;
        _partyButtonsPanel.Location = new Point(3, 3);
        _partyButtonsPanel.Name = "_partyButtonsPanel";
        _partyButtonsPanel.Padding = new Padding(4);
        _partyButtonsPanel.Size = new Size(1008, 46);
        _partyButtonsPanel.TabIndex = 0;
        // 
        // _addPartyButton
        // 
        _addPartyButton.AutoSize = true;
        _addPartyButton.Location = new Point(8, 8);
        _addPartyButton.Margin = new Padding(4);
        _addPartyButton.Name = "_addPartyButton";
        _addPartyButton.Size = new Size(101, 30);
        _addPartyButton.TabIndex = 0;
        _addPartyButton.Text = "[+] 파티원 추가";
        // 
        // _sendPartyListButton
        // 
        _sendPartyListButton.AutoSize = true;
        _sendPartyListButton.Location = new Point(117, 8);
        _sendPartyListButton.Margin = new Padding(4);
        _sendPartyListButton.Name = "_sendPartyListButton";
        _sendPartyListButton.Size = new Size(90, 30);
        _sendPartyListButton.TabIndex = 1;
        _sendPartyListButton.Text = "PartyList 전송";
        // 
        // _sendPartyUpdateButton
        // 
        _sendPartyUpdateButton.AutoSize = true;
        _sendPartyUpdateButton.Location = new Point(215, 8);
        _sendPartyUpdateButton.Margin = new Padding(4);
        _sendPartyUpdateButton.Name = "_sendPartyUpdateButton";
        _sendPartyUpdateButton.Size = new Size(110, 30);
        _sendPartyUpdateButton.TabIndex = 2;
        _sendPartyUpdateButton.Text = "PartyUpdate 전송";
        // 
        // _partyList
        // 
        _partyList.AutoScroll = true;
        _partyList.Dock = DockStyle.Fill;
        _partyList.FlowDirection = FlowDirection.TopDown;
        _partyList.Location = new Point(3, 55);
        _partyList.Name = "_partyList";
        _partyList.Padding = new Padding(4);
        _partyList.Size = new Size(1008, 403);
        _partyList.TabIndex = 1;
        _partyList.WrapContents = false;
        // 
        // _opcodeTab
        // 
        _opcodeTab.Controls.Add(_opcodePageLayout);
        _opcodeTab.Location = new Point(4, 24);
        _opcodeTab.Name = "_opcodeTab";
        _opcodeTab.Padding = new Padding(8);
        _opcodeTab.Size = new Size(1050, 513);
        _opcodeTab.TabIndex = 2;
        _opcodeTab.Text = "OPCode 전송";
        _opcodeTab.Click += _opcodeTab_Click;
        // 
        // _opcodePageLayout
        // 
        _opcodePageLayout.ColumnCount = 1;
        _opcodePageLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _opcodePageLayout.Controls.Add(_opcodeGroup, 0, 0);
        _opcodePageLayout.Controls.Add(_jsonGroup, 0, 1);
        _opcodePageLayout.Dock = DockStyle.Fill;
        _opcodePageLayout.Location = new Point(8, 8);
        _opcodePageLayout.Name = "_opcodePageLayout";
        _opcodePageLayout.RowCount = 2;
        _opcodePageLayout.RowStyles.Add(new RowStyle());
        _opcodePageLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _opcodePageLayout.Size = new Size(1034, 497);
        _opcodePageLayout.TabIndex = 0;
        // 
        // _opcodeGroup
        // 
        _opcodeGroup.AutoSize = true;
        _opcodeGroup.Controls.Add(_opcodeLayout);
        _opcodeGroup.Dock = DockStyle.Fill;
        _opcodeGroup.Location = new Point(3, 3);
        _opcodeGroup.Name = "_opcodeGroup";
        _opcodeGroup.Padding = new Padding(10);
        _opcodeGroup.Size = new Size(1028, 308);
        _opcodeGroup.TabIndex = 0;
        _opcodeGroup.TabStop = false;
        _opcodeGroup.Text = "OPCode 상세설정";
        // 
        // _opcodeLayout
        // 
        _opcodeLayout.AutoSize = true;
        _opcodeLayout.ColumnCount = 1;
        _opcodeLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _opcodeLayout.Controls.Add(_opcodeHeaderPanel, 0, 0);
        _opcodeLayout.Controls.Add(_opcodeDetails, 0, 1);
        _opcodeLayout.Dock = DockStyle.Fill;
        _opcodeLayout.Location = new Point(10, 26);
        _opcodeLayout.Name = "_opcodeLayout";
        _opcodeLayout.RowCount = 2;
        _opcodeLayout.RowStyles.Add(new RowStyle());
        _opcodeLayout.RowStyles.Add(new RowStyle());
        _opcodeLayout.Size = new Size(1008, 272);
        _opcodeLayout.TabIndex = 0;
        // 
        // _opcodeHeaderPanel
        // 
        _opcodeHeaderPanel.AutoSize = true;
        _opcodeHeaderPanel.Controls.Add(_opcodeLabel);
        _opcodeHeaderPanel.Controls.Add(_opcode);
        _opcodeHeaderPanel.Controls.Add(_searchDungeonButton);
        _opcodeHeaderPanel.Controls.Add(_searchBossButton);
        _opcodeHeaderPanel.Controls.Add(_searchSkillButton);
        _opcodeHeaderPanel.Controls.Add(_refreshJsonButton);
        _opcodeHeaderPanel.Controls.Add(_sendSelectedOpcodeButton);
        _opcodeHeaderPanel.Controls.Add(_combatSetupButton);
        _opcodeHeaderPanel.Controls.Add(_hitButton);
        _opcodeHeaderPanel.Controls.Add(_killButton);
        _opcodeHeaderPanel.Dock = DockStyle.Fill;
        _opcodeHeaderPanel.Location = new Point(3, 3);
        _opcodeHeaderPanel.Name = "_opcodeHeaderPanel";
        _opcodeHeaderPanel.Padding = new Padding(4);
        _opcodeHeaderPanel.Size = new Size(1002, 46);
        _opcodeHeaderPanel.TabIndex = 0;
        // 
        // _opcodeLabel
        // 
        _opcodeLabel.AutoSize = true;
        _opcodeLabel.Location = new Point(12, 12);
        _opcodeLabel.Margin = new Padding(8, 8, 2, 2);
        _opcodeLabel.Name = "_opcodeLabel";
        _opcodeLabel.Size = new Size(51, 15);
        _opcodeLabel.TabIndex = 0;
        _opcodeLabel.Text = "OPCode";
        // 
        // _opcode
        // 
        _opcode.DropDownStyle = ComboBoxStyle.DropDownList;
        _opcode.Location = new Point(68, 7);
        _opcode.Name = "_opcode";
        _opcode.Size = new Size(190, 23);
        _opcode.TabIndex = 1;
        // 
        // _searchDungeonButton
        // 
        _searchDungeonButton.AutoSize = true;
        _searchDungeonButton.Location = new Point(265, 8);
        _searchDungeonButton.Margin = new Padding(4);
        _searchDungeonButton.Name = "_searchDungeonButton";
        _searchDungeonButton.Size = new Size(75, 30);
        _searchDungeonButton.TabIndex = 2;
        _searchDungeonButton.Text = "던전 검색";
        // 
        // _searchBossButton
        // 
        _searchBossButton.AutoSize = true;
        _searchBossButton.Location = new Point(348, 8);
        _searchBossButton.Margin = new Padding(4);
        _searchBossButton.Name = "_searchBossButton";
        _searchBossButton.Size = new Size(75, 30);
        _searchBossButton.TabIndex = 3;
        _searchBossButton.Text = "보스 검색";
        // 
        // _searchSkillButton
        // 
        _searchSkillButton.AutoSize = true;
        _searchSkillButton.Location = new Point(431, 8);
        _searchSkillButton.Margin = new Padding(4);
        _searchSkillButton.Name = "_searchSkillButton";
        _searchSkillButton.Size = new Size(75, 30);
        _searchSkillButton.TabIndex = 4;
        _searchSkillButton.Text = "스킬 검색";
        // 
        // _refreshJsonButton
        // 
        _refreshJsonButton.AutoSize = true;
        _refreshJsonButton.Location = new Point(514, 8);
        _refreshJsonButton.Margin = new Padding(4);
        _refreshJsonButton.Name = "_refreshJsonButton";
        _refreshJsonButton.Size = new Size(75, 30);
        _refreshJsonButton.TabIndex = 5;
        _refreshJsonButton.Text = "JSON 갱신";
        // 
        // _sendSelectedOpcodeButton
        // 
        _sendSelectedOpcodeButton.AutoSize = true;
        _sendSelectedOpcodeButton.Location = new Point(597, 8);
        _sendSelectedOpcodeButton.Margin = new Padding(4);
        _sendSelectedOpcodeButton.Name = "_sendSelectedOpcodeButton";
        _sendSelectedOpcodeButton.Size = new Size(117, 30);
        _sendSelectedOpcodeButton.TabIndex = 6;
        _sendSelectedOpcodeButton.Text = "선택 OPCode 전송";
        // 
        // _combatSetupButton
        // 
        _combatSetupButton.AutoSize = true;
        _combatSetupButton.Location = new Point(722, 8);
        _combatSetupButton.Margin = new Padding(4);
        _combatSetupButton.Name = "_combatSetupButton";
        _combatSetupButton.Size = new Size(97, 30);
        _combatSetupButton.TabIndex = 7;
        _combatSetupButton.Text = "기본 전투 세팅";
        // 
        // _hitButton
        // 
        _hitButton.AutoSize = true;
        _hitButton.Location = new Point(827, 8);
        _hitButton.Margin = new Padding(4);
        _hitButton.Name = "_hitButton";
        _hitButton.Size = new Size(75, 30);
        _hitButton.TabIndex = 8;
        _hitButton.Text = "Hit";
        // 
        // _killButton
        // 
        _killButton.AutoSize = true;
        _killButton.Location = new Point(910, 8);
        _killButton.Margin = new Padding(4);
        _killButton.Name = "_killButton";
        _killButton.Size = new Size(75, 30);
        _killButton.TabIndex = 9;
        _killButton.Text = "Kill";
        // 
        // _opcodeDetails
        // 
        _opcodeDetails.AutoSize = true;
        _opcodeDetails.Dock = DockStyle.Fill;
        _opcodeDetails.Location = new Point(3, 55);
        _opcodeDetails.Name = "_opcodeDetails";
        _opcodeDetails.Padding = new Padding(4);
        _opcodeDetails.Size = new Size(1002, 214);
        _opcodeDetails.TabIndex = 1;
        // 
        // _jsonGroup
        // 
        _jsonGroup.Controls.Add(_jsonLayout);
        _jsonGroup.Dock = DockStyle.Fill;
        _jsonGroup.Location = new Point(3, 317);
        _jsonGroup.Name = "_jsonGroup";
        _jsonGroup.Padding = new Padding(10);
        _jsonGroup.Size = new Size(1028, 177);
        _jsonGroup.TabIndex = 1;
        _jsonGroup.TabStop = false;
        _jsonGroup.Text = "JSON 구조";
        // 
        // _jsonLayout
        // 
        _jsonLayout.ColumnCount = 1;
        _jsonLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _jsonLayout.Controls.Add(_jsonButtonsPanel, 0, 0);
        _jsonLayout.Controls.Add(_json, 0, 1);
        _jsonLayout.Dock = DockStyle.Fill;
        _jsonLayout.Location = new Point(10, 26);
        _jsonLayout.Name = "_jsonLayout";
        _jsonLayout.RowCount = 2;
        _jsonLayout.RowStyles.Add(new RowStyle());
        _jsonLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _jsonLayout.Size = new Size(1008, 141);
        _jsonLayout.TabIndex = 0;
        // 
        // _jsonButtonsPanel
        // 
        _jsonButtonsPanel.AutoSize = true;
        _jsonButtonsPanel.Controls.Add(_sendJsonButton);
        _jsonButtonsPanel.Controls.Add(_sendJsonFileButton);
        _jsonButtonsPanel.Controls.Add(_clearLogButton);
        _jsonButtonsPanel.Dock = DockStyle.Fill;
        _jsonButtonsPanel.Location = new Point(3, 3);
        _jsonButtonsPanel.Name = "_jsonButtonsPanel";
        _jsonButtonsPanel.Padding = new Padding(4);
        _jsonButtonsPanel.Size = new Size(1002, 46);
        _jsonButtonsPanel.TabIndex = 0;
        // 
        // _sendJsonButton
        // 
        _sendJsonButton.AutoSize = true;
        _sendJsonButton.Location = new Point(8, 8);
        _sendJsonButton.Margin = new Padding(4);
        _sendJsonButton.Name = "_sendJsonButton";
        _sendJsonButton.Size = new Size(86, 30);
        _sendJsonButton.TabIndex = 0;
        _sendJsonButton.Text = "JSON 보내기";
        // 
        // _sendJsonFileButton
        // 
        _sendJsonFileButton.AutoSize = true;
        _sendJsonFileButton.Location = new Point(102, 8);
        _sendJsonFileButton.Margin = new Padding(4);
        _sendJsonFileButton.Name = "_sendJsonFileButton";
        _sendJsonFileButton.Size = new Size(118, 30);
        _sendJsonFileButton.TabIndex = 1;
        _sendJsonFileButton.Text = "Send JSONL File";
        // 
        // _clearLogButton
        // 
        _clearLogButton.AutoSize = true;
        _clearLogButton.Location = new Point(228, 8);
        _clearLogButton.Margin = new Padding(4);
        _clearLogButton.Name = "_clearLogButton";
        _clearLogButton.Size = new Size(81, 30);
        _clearLogButton.TabIndex = 2;
        _clearLogButton.Text = "로그 지우기";
        // 
        // _json
        // 
        _json.Dock = DockStyle.Fill;
        _json.Location = new Point(3, 55);
        _json.MaxLength = int.MaxValue;
        _json.Multiline = true;
        _json.Name = "_json";
        _json.ScrollBars = ScrollBars.Vertical;
        _json.Size = new Size(1002, 83);
        _json.TabIndex = 1;
        // 
        // _log
        // 
        _log.BackColor = Color.FromArgb(24, 26, 30);
        _log.Dock = DockStyle.Fill;
        _log.ForeColor = Color.Gainsboro;
        _log.Location = new Point(13, 560);
        _log.Name = "_log";
        _log.ReadOnly = true;
        _log.Size = new Size(1058, 208);
        _log.TabIndex = 1;
        _log.Text = "";
        // 
        // _selfInfoButton
        // 
        _selfInfoButton.AutoSize = true;
        _selfInfoButton.Location = new Point(0, 0);
        _selfInfoButton.Margin = new Padding(4);
        _selfInfoButton.Name = "_selfInfoButton";
        _selfInfoButton.Size = new Size(75, 30);
        _selfInfoButton.TabIndex = 0;
        _selfInfoButton.Text = "SelfInfo";
        // 
        // _combatPowerButton
        // 
        _combatPowerButton.AutoSize = true;
        _combatPowerButton.Location = new Point(0, 0);
        _combatPowerButton.Margin = new Padding(4);
        _combatPowerButton.Name = "_combatPowerButton";
        _combatPowerButton.Size = new Size(75, 30);
        _combatPowerButton.TabIndex = 0;
        _combatPowerButton.Text = "CombatPower";
        // 
        // _dungeonId
        // 
        _dungeonId.Location = new Point(0, 0);
        _dungeonId.Maximum = new decimal(new int[] { int.MaxValue, 0, 0, 0 });
        _dungeonId.Name = "_dungeonId";
        _dungeonId.Size = new Size(100, 23);
        _dungeonId.TabIndex = 0;
        _dungeonId.Value = new decimal(new int[] { 620021, 0, 0, 0 });
        // 
        // _stage
        // 
        _stage.Location = new Point(0, 0);
        _stage.Maximum = new decimal(new int[] { 10, 0, 0, 0 });
        _stage.Name = "_stage";
        _stage.Size = new Size(60, 23);
        _stage.TabIndex = 0;
        // 
        // _bossEntityId
        // 
        _bossEntityId.Location = new Point(0, 0);
        _bossEntityId.Maximum = new decimal(new int[] { int.MaxValue, 0, 0, 0 });
        _bossEntityId.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _bossEntityId.Name = "_bossEntityId";
        _bossEntityId.Size = new Size(100, 23);
        _bossEntityId.TabIndex = 0;
        _bossEntityId.Value = new decimal(new int[] { 9001, 0, 0, 0 });
        // 
        // _bossCode
        // 
        _bossCode.Location = new Point(0, 0);
        _bossCode.Maximum = new decimal(new int[] { int.MaxValue, 0, 0, 0 });
        _bossCode.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _bossCode.Name = "_bossCode";
        _bossCode.Size = new Size(100, 23);
        _bossCode.TabIndex = 0;
        _bossCode.Value = new decimal(new int[] { 2301059, 0, 0, 0 });
        // 
        // _bossHp
        // 
        _bossHp.Increment = new decimal(new int[] { 1000000, 0, 0, 0 });
        _bossHp.Location = new Point(0, 0);
        _bossHp.Maximum = new decimal(new int[] { int.MaxValue, 0, 0, 0 });
        _bossHp.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _bossHp.Name = "_bossHp";
        _bossHp.Size = new Size(120, 23);
        _bossHp.TabIndex = 0;
        _bossHp.Value = new decimal(new int[] { 100000000, 0, 0, 0 });
        // 
        // _actorId
        // 
        _actorId.Location = new Point(0, 0);
        _actorId.Maximum = new decimal(new int[] { int.MaxValue, 0, 0, 0 });
        _actorId.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _actorId.Name = "_actorId";
        _actorId.Size = new Size(100, 23);
        _actorId.TabIndex = 0;
        _actorId.Value = new decimal(new int[] { 1001, 0, 0, 0 });
        // 
        // _targetId
        // 
        _targetId.Location = new Point(0, 0);
        _targetId.Maximum = new decimal(new int[] { int.MaxValue, 0, 0, 0 });
        _targetId.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _targetId.Name = "_targetId";
        _targetId.Size = new Size(100, 23);
        _targetId.TabIndex = 0;
        _targetId.Value = new decimal(new int[] { 9001, 0, 0, 0 });
        // 
        // _skillCode
        // 
        _skillCode.Location = new Point(0, 0);
        _skillCode.Maximum = new decimal(new int[] { int.MaxValue, 0, 0, 0 });
        _skillCode.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _skillCode.Name = "_skillCode";
        _skillCode.Size = new Size(110, 23);
        _skillCode.TabIndex = 0;
        _skillCode.Value = new decimal(new int[] { 11000000, 0, 0, 0 });
        // 
        // _damage
        // 
        _damage.Increment = new decimal(new int[] { 100000, 0, 0, 0 });
        _damage.Location = new Point(0, 0);
        _damage.Maximum = new decimal(new int[] { int.MaxValue, 0, 0, 0 });
        _damage.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _damage.Name = "_damage";
        _damage.Size = new Size(120, 23);
        _damage.TabIndex = 0;
        _damage.Value = new decimal(new int[] { 4200000, 0, 0, 0 });
        // 
        // _crit
        // 
        _crit.AutoSize = true;
        _crit.Location = new Point(0, 0);
        _crit.Name = "_crit";
        _crit.Size = new Size(104, 24);
        _crit.TabIndex = 0;
        _crit.Text = "Crit";
        // 
        // _buffId
        // 
        _buffId.Location = new Point(0, 0);
        _buffId.Maximum = new decimal(new int[] { int.MaxValue, 0, 0, 0 });
        _buffId.Minimum = new decimal(new int[] { 1, 0, 0, 0 });
        _buffId.Name = "_buffId";
        _buffId.Size = new Size(110, 23);
        _buffId.TabIndex = 0;
        _buffId.Value = new decimal(new int[] { 11000000, 0, 0, 0 });
        // 
        // _duration
        // 
        _duration.Increment = new decimal(new int[] { 1000, 0, 0, 0 });
        _duration.Location = new Point(0, 0);
        _duration.Maximum = new decimal(new int[] { int.MaxValue, 0, 0, 0 });
        _duration.Name = "_duration";
        _duration.Size = new Size(100, 23);
        _duration.TabIndex = 0;
        _duration.Value = new decimal(new int[] { 30000, 0, 0, 0 });
        // 
        // MainForm
        // 
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1084, 781);
        Controls.Add(_rootLayout);
        MinimumSize = new Size(1100, 820);
        Name = "MainForm";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "A2Meter TCP Packet Mock";
        _rootLayout.ResumeLayout(false);
        _tabControl.ResumeLayout(false);
        _settingsTab.ResumeLayout(false);
        _settingsLayout.ResumeLayout(false);
        _settingsLayout.PerformLayout();
        _launchGroup.ResumeLayout(false);
        _launchGroup.PerformLayout();
        _launchPanel.ResumeLayout(false);
        _launchPanel.PerformLayout();
        ((ISupportInitialize)_port).EndInit();
        _databaseGroup.ResumeLayout(false);
        _databaseGroup.PerformLayout();
        _databaseLayout.ResumeLayout(false);
        _databaseLayout.PerformLayout();
        ((ISupportInitialize)_dbPort).EndInit();
        ((ISupportInitialize)_sshPort).EndInit();
        _selfGroup.ResumeLayout(false);
        _selfGroup.PerformLayout();
        _partyTab.ResumeLayout(false);
        _partyGroup.ResumeLayout(false);
        _partyLayout.ResumeLayout(false);
        _partyLayout.PerformLayout();
        _partyButtonsPanel.ResumeLayout(false);
        _partyButtonsPanel.PerformLayout();
        _opcodeTab.ResumeLayout(false);
        _opcodePageLayout.ResumeLayout(false);
        _opcodePageLayout.PerformLayout();
        _opcodeGroup.ResumeLayout(false);
        _opcodeGroup.PerformLayout();
        _opcodeLayout.ResumeLayout(false);
        _opcodeLayout.PerformLayout();
        _opcodeHeaderPanel.ResumeLayout(false);
        _opcodeHeaderPanel.PerformLayout();
        _jsonGroup.ResumeLayout(false);
        _jsonLayout.ResumeLayout(false);
        _jsonLayout.PerformLayout();
        _jsonButtonsPanel.ResumeLayout(false);
        _jsonButtonsPanel.PerformLayout();
        ((ISupportInitialize)_dungeonId).EndInit();
        ((ISupportInitialize)_stage).EndInit();
        ((ISupportInitialize)_bossEntityId).EndInit();
        ((ISupportInitialize)_bossCode).EndInit();
        ((ISupportInitialize)_bossHp).EndInit();
        ((ISupportInitialize)_actorId).EndInit();
        ((ISupportInitialize)_targetId).EndInit();
        ((ISupportInitialize)_skillCode).EndInit();
        ((ISupportInitialize)_damage).EndInit();
        ((ISupportInitialize)_buffId).EndInit();
        ((ISupportInitialize)_duration).EndInit();
        ResumeLayout(false);
    }

}
