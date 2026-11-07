using System.Drawing;
using System.Windows.Forms;
using HackTK.Next.Abstractions;
using HackTK.Next.Configuration;
using HackTK.Next.Core;
using HackTK.Next.Diagnostics;

namespace HackTK.Next.UI;

public sealed class EngineDashboardForm : Form
{
    private readonly DashboardPresenter _presenter;
    private readonly System.Windows.Forms.Timer _heartbeatTimer = new() { Interval = 1000 };
    private readonly Font _uiFont = new("Segoe UI", 10f);
    private bool _suppressUiEvents;

    private readonly ComboBox _cmbProcesses = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 550 };
    private readonly Label _lblConnectionBanner = new()
    {
        Dock = DockStyle.Fill,
        AutoSize = false,
        Height = 52,
        ForeColor = Color.FromArgb(158, 215, 244),
        Text = "Chưa kết nối — cần module mới được nạp trong Unity."
    };

    private readonly Label _lblStatus = new()
    {
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        Text = "Cấu hình chỉ có tác dụng sau khi module xác nhận nhận lệnh."
    };

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };

    private readonly DataGridView _gridLiveModules = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = Color.White,
        BorderStyle = BorderStyle.None
    };

    private readonly CheckBox _chkAimEnabled = new() { Text = "Bật hỗ trợ ngắm", Width = 880, Height = 29, Margin = new Padding(0, 3, 0, 3) };
    private readonly CheckBox _chkDrawAimRadius = new() { Text = "Vẽ vòng bán kính chọn mục tiêu", Width = 880, Height = 29, Margin = new Padding(0, 3, 0, 3) };
    private readonly NumericUpDown _numAimRadius = new() { Minimum = 1, Maximum = 2048, DecimalPlaces = 1, Increment = 0.1m, Width = 130 };
    private readonly NumericUpDown _numSmooth = new() { Minimum = 1, Maximum = 10, DecimalPlaces = 1, Increment = 0.1m, Width = 130 };
    private readonly ComboBox _cmbTargetPart = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, DrawMode = DrawMode.OwnerDrawFixed };
    private readonly ComboBox _cmbActivationKey = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, DrawMode = DrawMode.OwnerDrawFixed };
    private readonly CheckBox _chkRecoilComp = new() { Text = "Bù giật theo PunchYaw/PunchPitch (chỉ khi Aimbot có mục tiêu)", Width = 880, Height = 29, Margin = new Padding(0, 3, 0, 3) };
    private readonly CheckBox _chkSmoothRecoil = new() { Text = "Làm êm camera; khi kèm No spread sẽ thử bù punch trong command", Width = 880, Height = 29, Margin = new Padding(0, 3, 0, 3) };
    private readonly CheckBox _chkSpreadComp = new() { Text = "Thử bù spread qua góc command (đứng/đi/chạy/strafe/xoay/nhảy), tự ghi audit; chưa xác minh máy chủ", Width = 880, Height = 29, Margin = new Padding(0, 3, 0, 3) };
    private readonly CheckBox _chkDiagnostics = new() { Text = "Ghi chẩn đoán đường bắn và HP từ snapshot (tệp TSV, tối đa 32 MB/lượt)", Width = 880, Height = 29, Margin = new Padding(0, 3, 0, 3) };
    private readonly CheckBox _chkManageRawInput = new() { Text = "Tạm tắt RawInput khi Aimbot hoạt động; có khôi phục (API đã xác minh runtime)", Width = 880, Height = 29, Margin = new Padding(0, 3, 0, 3) };

    private readonly CheckBox _chkEspBox = new() { Text = "ESP hộp", Width = 880, Height = 29, Margin = new Padding(0, 3, 0, 3) };
    private readonly CheckBox _chkEspSkeleton = new() { Text = "ESP xương", Width = 880, Height = 29, Margin = new Padding(0, 3, 0, 3) };
    private readonly CheckBox _chkEspSnaplines = new() { Text = "Đường nối màn hình → mục tiêu", Width = 880, Height = 29, Margin = new Padding(0, 3, 0, 3) };
    private readonly CheckBox _chkEspNames = new() { Text = "Tên người chơi", Width = 880, Height = 29, Margin = new Padding(0, 3, 0, 3) };
    private readonly CheckBox _chkEspHealth = new() { Text = "Thanh máu", Width = 880, Height = 29, Margin = new Padding(0, 3, 0, 3) };
    private readonly NumericUpDown _numBoxHue = new() { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.01m, Width = 130 };
    private readonly NumericUpDown _numBoneHue = new() { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.01m, Width = 130 };
    private readonly CheckBox _chkPlayerChams = new() { Text = "Chams người chơi địch + vũ khí góc nhìn thứ ba", Width = 880, Height = 29, Margin = new Padding(0, 3, 0, 3) };
    private readonly CheckBox _chkHandChams = new() { Text = "Chams tay góc nhìn thứ nhất", Width = 880, Height = 29, Margin = new Padding(0, 3, 0, 3) };
    private readonly CheckBox _chkWeaponChams = new() { Text = "Chams vũ khí góc nhìn thứ nhất", Width = 880, Height = 29, Margin = new Padding(0, 3, 0, 3) };
    private readonly ComboBox _cmbChamsMode = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, DrawMode = DrawMode.OwnerDrawFixed };
    private readonly NumericUpDown _numPlayerHue = new() { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.01m, Width = 130 };
    private readonly NumericUpDown _numHandHue = new() { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.01m, Width = 130 };
    private readonly NumericUpDown _numWeaponHue = new() { Minimum = 0, Maximum = 1, DecimalPlaces = 2, Increment = 0.01m, Width = 130 };

    private readonly CheckBox _chkCrosshair = new() { Text = "Tâm ngắm +", Width = 880, Height = 29, Margin = new Padding(0, 3, 0, 3) };
    private readonly CheckBox _chkBunnyHop = new() { Text = "Bhop — giữ Space, cần first-person + chạm đất (thử nghiệm)", Width = 880, Height = 29, Margin = new Padding(0, 3, 0, 3) };
    private readonly CheckBox _chkViewmodel = new() { Text = "Đặt vị trí tay / viewmodel", Width = 880, Height = 29, Margin = new Padding(0, 3, 0, 3) };
    private readonly NumericUpDown _numViewX = new() { Minimum = -10, Maximum = 10, DecimalPlaces = 2, Increment = 0.01m, Width = 130 };
    private readonly NumericUpDown _numViewY = new() { Minimum = -10, Maximum = 10, DecimalPlaces = 2, Increment = 0.01m, Width = 130 };
    private readonly NumericUpDown _numViewZ = new() { Minimum = -10, Maximum = 10, DecimalPlaces = 2, Increment = 0.01m, Width = 130 };

    public EngineDashboardForm(DashboardPresenter presenter)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));

        Text = "HackTK Rebuild 0.5.0 — kiểm tra No spread thử nghiệm trong verification/v0.5.0";
        ClientSize = new Size(1100, 800);
        MinimumSize = new Size(940, 720);
        StartPosition = FormStartPosition.CenterScreen;
        Font = _uiFont;
        BackColor = Color.FromArgb(242, 245, 249);

        ConfigureOwnerDrawCombo(_cmbTargetPart, Enum.GetValues<TargetPart>().Cast<object>().ToArray());
        ConfigureOwnerDrawCombo(_cmbActivationKey, Enum.GetValues<TriggerKey>().Cast<object>().ToArray());
        ConfigureOwnerDrawCombo(_cmbChamsMode, Enum.GetValues<ShaderRenderMode>().Cast<object>().ToArray());

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        Controls.Add(layout);

        layout.Controls.Add(BuildConnectionHeader(), 0, 0);
        layout.Controls.Add(_tabs, 0, 1);
        layout.Controls.Add(BuildActionBar(), 0, 2);
        layout.Controls.Add(_lblStatus, 0, 3);

        BuildAimTab();
        BuildVisualsTab();
        BuildMiscTab();
        BuildSettingsAndStatusTab();

        PopulateDefaultStatusRows();
        SyncControlsFromProfile();

        if (File.Exists(_presenter.DefaultProfilePath))
        {
            _presenter.TryLoadProfile(null, out string loadStatus);
            SyncControlsFromProfile();
            _lblStatus.Text = loadStatus;
        }

        _lblConnectionBanner.Text = _presenter.ConnectionBanner;

        Shown += (_, _) =>
        {
            RefreshProcessList();
            _heartbeatTimer.Start();
        };
        _heartbeatTimer.Tick += (_, _) => OnHeartbeatTick();
        FormClosing += (_, _) =>
        {
            _heartbeatTimer.Stop();
            _presenter.Disconnect(out _);
        };
    }

    private static void ConfigureOwnerDrawCombo(ComboBox control, object[] items)
    {
        control.Items.AddRange(items);
        control.DrawItem += (_, e) =>
        {
            e.DrawBackground();
            string text = e.Index >= 0
                ? control.Items[e.Index]?.ToString() ?? string.Empty
                : control.SelectedItem?.ToString() ?? string.Empty;
            TextRenderer.DrawText(e.Graphics, text, e.Font, e.Bounds, e.ForeColor, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            e.DrawFocusRectangle();
        };
    }

    private Panel BuildConnectionHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.FromArgb(24, 39, 59),
            Padding = new Padding(12),
            RowCount = 2,
            ColumnCount = 1
        };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var strip = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false
        };
        strip.Controls.Add(_cmbProcesses);
        AddButton(strip, "Tìm game", RefreshProcessList);
        AddButton(strip, "Bật Runtime Thử Nghiệm", StartAuthorizedTestRuntime);
        AddButton(strip, "Kết nối", ConnectToSelectedProcess);
        AddButton(strip, "Ngắt kết nối", DisconnectFromGame);

        header.Controls.Add(strip, 0, 0);
        header.Controls.Add(_lblConnectionBanner, 0, 1);
        return header;
    }

    private Panel BuildActionBar()
    {
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            WrapContents = false,
            Padding = new Padding(0, 5, 0, 0)
        };
        AddButton(actions, "Áp dụng vào module", ApplyCurrentToGame);
        AddButton(actions, "Lưu cấu hình…", SaveProfileDialog);
        AddButton(actions, "Đọc cấu hình…", LoadProfileDialog);
        AddButton(actions, "Tắt mọi chức năng", DisableAllFeatures);
        return actions;
    }

    private static void AddButton(Control parent, string text, Action action)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 33,
            FlatStyle = FlatStyle.System,
            Margin = new Padding(3)
        };
        button.Click += (_, _) =>
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "HackTK Rebuild", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        };
        parent.Controls.Add(button);
    }

    private FlowLayoutPanel CreateTabPage(string title)
    {
        var page = new TabPage(title) { BackColor = Color.White };
        var content = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            Padding = new Padding(16)
        };
        page.Controls.Add(content);
        _tabs.TabPages.Add(page);
        return content;
    }

    private static void AddNote(Control parent, string text) =>
        parent.Controls.Add(new Label
        {
            Text = text,
            Width = 920,
            AutoSize = false,
            Height = 56,
            ForeColor = Color.FromArgb(83, 98, 116),
            Margin = new Padding(0, 5, 0, 8)
        });

    private static void AddLabeledRow(Control parent, string title, Control editor)
    {
        var row = new FlowLayoutPanel
        {
            Width = 900,
            Height = 35,
            WrapContents = false,
            Margin = new Padding(0, 2, 0, 2)
        };
        row.Controls.Add(new Label
        {
            Text = title,
            Width = 480,
            Height = 30,
            TextAlign = ContentAlignment.MiddleLeft
        });
        row.Controls.Add(editor);
        parent.Controls.Add(row);
    }

    private void BuildAimTab()
    {
        var aim = CreateTabPage("Aim");
        AddNote(aim, "Đã thử ngắm Head với Alt trái, Smooth=5 và tạm tắt RawInput. RCS cần đối chiếu riêng; xem báo cáo kiểm chứng.");
        aim.Controls.Add(_chkAimEnabled);
        aim.Controls.Add(_chkDrawAimRadius);
        AddLabeledRow(aim, "Bán kính chọn mục tiêu (px tại 1080p; tự theo độ phân giải)", _numAimRadius);
        AddLabeledRow(aim, "Smooth", _numSmooth);
        AddLabeledRow(aim, "Xương mục tiêu", _cmbTargetPart);
        AddLabeledRow(aim, "Giữ phím để ngắm", _cmbActivationKey);
        aim.Controls.Add(_chkRecoilComp);
        aim.Controls.Add(_chkSmoothRecoil);
        aim.Controls.Add(_chkSpreadComp);
        aim.Controls.Add(_chkDiagnostics);
        AddNote(aim, "Aimbot chỉ chọn khi đầu rõ từ camera và điểm bắn qua physics.net; mất visibility sẽ nhả mục tiêu. HP từ mạng không tự chứng minh từng viên đã trúng.");
        aim.Controls.Add(_chkManageRawInput);
        AddNote(aim, "Không ép spread/punch client về 0. Camera êm không bảo đảm đạn thẳng hoặc máy chủ chấp nhận sát thương; tracer/decal do game tự tính.");
    }

    private void BuildVisualsTab()
    {
        var visuals = CreateTabPage("Visuals");
        visuals.Controls.Add(_chkEspBox);
        visuals.Controls.Add(_chkEspSkeleton);
        visuals.Controls.Add(_chkEspSnaplines);
        visuals.Controls.Add(_chkEspNames);
        visuals.Controls.Add(_chkEspHealth);
        AddLabeledRow(visuals, "Màu hộp / snaplines (HSV hue)", _numBoxHue);
        AddLabeledRow(visuals, "Màu xương (HSV hue)", _numBoneHue);
        visuals.Controls.Add(_chkPlayerChams);
        visuals.Controls.Add(_chkHandChams);
        visuals.Controls.Add(_chkWeaponChams);
        AddLabeledRow(visuals, "Chế độ shader", _cmbChamsMode);
        AddLabeledRow(visuals, "Màu người chơi", _numPlayerHue);
        AddLabeledRow(visuals, "Màu tay", _numHandHue);
        AddLabeledRow(visuals, "Màu vũ khí", _numWeaponHue);
        AddNote(visuals, "Shader được kiểm tra khi module chạy. Thiếu shader/thuộc tính sẽ báo không khả dụng; material cũ được khôi phục khi tắt.");
    }

    private void BuildMiscTab()
    {
        var misc = CreateTabPage("Misc");
        misc.Controls.Add(_chkCrosshair);
        misc.Controls.Add(_chkBunnyHop);
        misc.Controls.Add(_chkViewmodel);
        AddLabeledRow(misc, "HandGameObject localPosition X", _numViewX);
        AddLabeledRow(misc, "HandGameObject localPosition Y", _numViewY);
        AddLabeledRow(misc, "HandGameObject localPosition Z", _numViewZ);
        AddNote(misc, "Viewmodel đặt tọa độ local tuyệt đối theo DLL gốc; không thay camera.fieldOfView. Khi tắt sẽ trả vị trí đã ghi nhận ban đầu.");
    }

    private void BuildSettingsAndStatusTab()
    {
        var settings = new TabPage("Settings / Trạng thái") { BackColor = Color.White };
        _tabs.TabPages.Add(settings);
        var statusLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(12)
        };
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        statusLayout.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Text = "WinForms .NET 10 x64 + module Unity .NET Framework 4.7.2 x86.\r\nKết nối chỉ được công nhận với heartbeat từ luồng chính Unity và PID/thời điểm khởi động khớp.\r\nBảng điều khiển đóng hoặc mất heartbeat: lệnh hết hạn sau 5 giây. Game mất focus: tạm dừng.\r\nBằng chứng từng chức năng: analysis/FEATURE-STATUS.csv và verification/recheck/LIVE-RESULTS.md."
        });

        _gridLiveModules.Columns.Add("Feature", "Chức năng");
        _gridLiveModules.Columns.Add("State", "Trạng thái module");
        _gridLiveModules.Columns.Add("Detail", "Chi tiết");
        _gridLiveModules.Columns[0].FillWeight = 22;
        _gridLiveModules.Columns[1].FillWeight = 28;
        _gridLiveModules.Columns[2].FillWeight = 50;
        _gridLiveModules.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;
        _gridLiveModules.DefaultCellStyle.WrapMode = DataGridViewTriState.True;

        statusLayout.Controls.Add(_gridLiveModules);
        settings.Controls.Add(statusLayout);
    }

    private void RefreshProcessList()
    {
        int? previousPid = (_cmbProcesses.SelectedItem as RuntimeProcessOption)?.ProcessId;
        var options = _presenter.DiscoverProcesses(out string msg);

        _cmbProcesses.Items.Clear();
        foreach (var opt in options)
            _cmbProcesses.Items.Add(opt);

        if (options.Count > 0)
        {
            var match = options.FirstOrDefault(o => o.ProcessId == previousPid && o.HasStatusFile)
                        ?? options.FirstOrDefault(o => o.HasStatusFile)
                        ?? options[0];
            _cmbProcesses.SelectedItem = match;
        }
        else
        {
            _lblStatus.Text = msg;
        }
    }

    private void StartAuthorizedTestRuntime()
    {
        if (!_presenter.TryStartAuthorizedTestRuntime(out var started, out string msg))
        {
            _lblStatus.Text = msg;
            throw new InvalidOperationException(msg);
        }

        RefreshProcessList();
        if (started is not null)
        {
            foreach (var item in _cmbProcesses.Items.OfType<RuntimeProcessOption>())
            {
                if (item.ProcessId == started.ProcessId)
                {
                    _cmbProcesses.SelectedItem = item;
                    break;
                }
            }
        }

        _lblStatus.Text = msg;
    }

    private void ConnectToSelectedProcess()
    {
        if (_cmbProcesses.SelectedItem is not RuntimeProcessOption selected ||
            !DashboardPresenter.IsProcessAlive(selected) ||
            !File.Exists(RuntimeSessionPaths.StatusFile(selected.ProcessId)))
        {
            RefreshProcessList();
        }

        if (_cmbProcesses.SelectedItem is not RuntimeProcessOption candidate)
            throw new InvalidOperationException("Chọn tiến trình game đang chạy trước.");

        if (!_presenter.TryConnect(candidate, out var liveStatus, out string msg))
        {
            _lblConnectionBanner.Text = _presenter.ConnectionBanner;
            PopulateDefaultStatusRows();
            throw new InvalidOperationException(msg);
        }

        _lblConnectionBanner.Text = _presenter.ConnectionBanner;
        _lblStatus.Text = msg;
        UpdateLiveModuleGrid(liveStatus);
    }

    private void ApplyCurrentToGame()
    {
        PushControlsToProfile();
        if (!_presenter.TryApplyCurrentProfile(out var confirmedStatus, out string msg))
            throw new InvalidOperationException(msg);

        _lblConnectionBanner.Text = _presenter.ConnectionBanner;
        _lblStatus.Text = msg;
        UpdateLiveModuleGrid(confirmedStatus);
    }

    private void DisconnectFromGame()
    {
        _presenter.Disconnect(out string msg);
        _lblConnectionBanner.Text = _presenter.ConnectionBanner;
        if (!string.Equals(msg, "Đã ngắt kết nối an toàn.", StringComparison.Ordinal))
            _lblStatus.Text = msg;
        PopulateDefaultStatusRows();
    }

    private void DisableAllFeatures()
    {
        _suppressUiEvents = true;
        try
        {
            CopyProfileValues(new EngineProfile(), _presenter.CurrentProfile);
        }
        finally
        {
            _suppressUiEvents = false;
        }
        SyncControlsFromProfile();

        if (_presenter.IsConnected)
            ApplyCurrentToGame();
        else
            _lblStatus.Text = "Đã tắt trong cấu hình cục bộ; chưa có kết nối game.";
    }

    private void SaveProfileDialog()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "HackTK XML (*.xml)|*.xml",
            FileName = "HackTK-profile.xml"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        PushControlsToProfile();
        if (!_presenter.TrySaveProfile(dialog.FileName, out string msg))
            throw new InvalidDataException(msg);

        _lblStatus.Text = msg;
    }

    private void LoadProfileDialog()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "HackTK XML (*.xml)|*.xml"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        if (!_presenter.TryLoadProfile(dialog.FileName, out string msg))
            throw new InvalidDataException(msg);

        SyncControlsFromProfile();
        _lblStatus.Text = msg;
    }

    private void OnHeartbeatTick()
    {
        if (!_presenter.IsConnected)
        {
            if (_cmbProcesses.SelectedItem is not RuntimeProcessOption current || !DashboardPresenter.IsProcessAlive(current))
                RefreshProcessList();
            return;
        }

        if (_presenter.TryTickConnection(out var liveStatus, out string msg))
        {
            _lblConnectionBanner.Text = _presenter.ConnectionBanner;
            UpdateLiveModuleGrid(liveStatus);
        }
        else
        {
            _lblConnectionBanner.Text = _presenter.ConnectionBanner;
            _lblStatus.Text = msg;
            PopulateDefaultStatusRows();
        }
    }

    private void UpdateLiveModuleGrid(RuntimeSessionStatus? status)
    {
        if (status is null) return;
        _gridLiveModules.Rows.Clear();
        foreach (var f in status.Features ?? Array.Empty<RuntimeFeatureState>())
        {
            if (f is not null)
                _gridLiveModules.Rows.Add(f.Feature, f.State, f.Detail);
        }

        if (!string.IsNullOrEmpty(status.TransportError) || !string.IsNullOrEmpty(status.CommandError))
            _gridLiveModules.Rows.Add("IPC", status.TransportError, status.CommandError);
    }

    private void PopulateDefaultStatusRows()
    {
        _gridLiveModules.Rows.Clear();
        string[] features =
        {
            "Aimbot + FOV + Smooth",
            "RCS theo punch",
            "No recoil / No spread",
            "RawInput tạm thời",
            "ESP hộp / xương / tên / máu / snaplines",
            "Chams",
            "Crosshair",
            "Bhop",
            "Viewmodel localPosition"
        };

        foreach (string feat in features)
            _gridLiveModules.Rows.Add(feat, "Chưa có module xác nhận", "Chưa có xác nhận trong phiên này; xem bảng bằng chứng từng chức năng trong README.");
        _gridLiveModules.Rows.Add("Sửa accuracy qua reflection", "Không tương thích / chưa triển khai", "Type hiện tại không có field accuracy/maxAccuracy.");
    }

    public void SyncControlsFromProfile()
    {
        _suppressUiEvents = true;
        try
        {
            var p = _presenter.CurrentProfile;
            var errors = p.Validate();
            if (errors.Count > 0)
                throw new InvalidDataException(string.Join(Environment.NewLine, errors));

            _chkAimEnabled.Checked = p.AimEnabled;
            _chkDrawAimRadius.Checked = p.DrawAimRadius;
            _chkRecoilComp.Checked = p.RecoilCompensation;
            _chkSmoothRecoil.Checked = p.SmoothCameraRecoil;
            _chkSpreadComp.Checked = p.CompensateCommandSpread;
            _chkDiagnostics.Checked = p.RecordDiagnostics;
            _chkManageRawInput.Checked = p.ManageRawInput;
            _numAimRadius.Value = (decimal)Math.Clamp(p.AimRadiusPixels, 1f, 2048f);
            _numSmooth.Value = (decimal)Math.Clamp(p.Smooth, 1f, 10f);
            _cmbTargetPart.SelectedItem = p.Target;
            _cmbActivationKey.SelectedItem = p.ActivationKey;

            _chkEspBox.Checked = p.EspBox;
            _chkEspSkeleton.Checked = p.EspSkeleton;
            _chkEspNames.Checked = p.EspNames;
            _chkEspHealth.Checked = p.EspHealth;
            _chkEspSnaplines.Checked = p.EspSnaplines;
            _numBoxHue.Value = (decimal)Math.Clamp(p.BoxHue, 0f, 1f);
            _numBoneHue.Value = (decimal)Math.Clamp(p.BoneHue, 0f, 1f);
            _chkPlayerChams.Checked = p.PlayerChams;
            _chkHandChams.Checked = p.HandChams;
            _chkWeaponChams.Checked = p.WeaponChams;
            _cmbChamsMode.SelectedItem = p.ChamsMode;
            _numPlayerHue.Value = (decimal)Math.Clamp(p.PlayerHue, 0f, 1f);
            _numHandHue.Value = (decimal)Math.Clamp(p.HandHue, 0f, 1f);
            _numWeaponHue.Value = (decimal)Math.Clamp(p.WeaponHue, 0f, 1f);

            _chkCrosshair.Checked = p.Crosshair;
            _chkBunnyHop.Checked = p.BunnyHop;
            _chkViewmodel.Checked = p.ViewmodelOffset;
            _numViewX.Value = (decimal)Math.Clamp(p.ViewX, -10f, 10f);
            _numViewY.Value = (decimal)Math.Clamp(p.ViewY, -10f, 10f);
            _numViewZ.Value = (decimal)Math.Clamp(p.ViewZ, -10f, 10f);
        }
        finally
        {
            _suppressUiEvents = false;
        }
    }

    public void PushControlsToProfile()
    {
        if (_suppressUiEvents) return;
        var p = _presenter.CurrentProfile;
        p.AimEnabled = _chkAimEnabled.Checked;
        p.DrawAimRadius = _chkDrawAimRadius.Checked;
        p.RecoilCompensation = _chkRecoilComp.Checked;
        p.SmoothCameraRecoil = _chkSmoothRecoil.Checked;
        p.CompensateCommandSpread = _chkSpreadComp.Checked;
        p.RecordDiagnostics = _chkDiagnostics.Checked;
        p.ManageRawInput = _chkManageRawInput.Checked;
        p.AimRadiusPixels = (float)_numAimRadius.Value;
        p.Smooth = (float)_numSmooth.Value;
        if (_cmbTargetPart.SelectedItem is TargetPart tp) p.Target = tp;
        if (_cmbActivationKey.SelectedItem is TriggerKey tk) p.ActivationKey = tk;

        p.EspBox = _chkEspBox.Checked;
        p.EspSkeleton = _chkEspSkeleton.Checked;
        p.EspNames = _chkEspNames.Checked;
        p.EspHealth = _chkEspHealth.Checked;
        p.EspSnaplines = _chkEspSnaplines.Checked;
        p.BoxHue = (float)_numBoxHue.Value;
        p.BoneHue = (float)_numBoneHue.Value;
        p.PlayerChams = _chkPlayerChams.Checked;
        p.HandChams = _chkHandChams.Checked;
        p.WeaponChams = _chkWeaponChams.Checked;
        if (_cmbChamsMode.SelectedItem is ShaderRenderMode sm) p.ChamsMode = sm;
        p.PlayerHue = (float)_numPlayerHue.Value;
        p.HandHue = (float)_numHandHue.Value;
        p.WeaponHue = (float)_numWeaponHue.Value;

        p.Crosshair = _chkCrosshair.Checked;
        p.BunnyHop = _chkBunnyHop.Checked;
        p.ViewmodelOffset = _chkViewmodel.Checked;
        p.ViewX = (float)_numViewX.Value;
        p.ViewY = (float)_numViewY.Value;
        p.ViewZ = (float)_numViewZ.Value;
    }

    private static void CopyProfileValues(EngineProfile source, EngineProfile target)
    {
        target.AimEnabled = source.AimEnabled;
        target.DrawAimRadius = source.DrawAimRadius;
        target.AimRadiusPixels = source.AimRadiusPixels;
        target.Smooth = source.Smooth;
        target.Target = source.Target;
        target.ActivationKey = source.ActivationKey;
        target.RecoilCompensation = source.RecoilCompensation;
        target.SmoothCameraRecoil = source.SmoothCameraRecoil;
        target.CompensateCommandSpread = source.CompensateCommandSpread;
        target.RecordDiagnostics = source.RecordDiagnostics;
        target.ManageRawInput = source.ManageRawInput;
        target.EspBox = source.EspBox;
        target.EspSkeleton = source.EspSkeleton;
        target.EspSnaplines = source.EspSnaplines;
        target.EspNames = source.EspNames;
        target.EspHealth = source.EspHealth;
        target.BoxHue = source.BoxHue;
        target.BoneHue = source.BoneHue;
        target.PlayerChams = source.PlayerChams;
        target.HandChams = source.HandChams;
        target.WeaponChams = source.WeaponChams;
        target.ChamsMode = source.ChamsMode;
        target.PlayerHue = source.PlayerHue;
        target.HandHue = source.HandHue;
        target.WeaponHue = source.WeaponHue;
        target.Crosshair = source.Crosshair;
        target.BunnyHop = source.BunnyHop;
        target.ViewmodelOffset = source.ViewmodelOffset;
        target.ViewX = source.ViewX;
        target.ViewY = source.ViewY;
        target.ViewZ = source.ViewZ;
    }

    public static bool RunSmokeVerification(string workDirectory, out string details)
    {
        Directory.CreateDirectory(workDirectory);
        string xmlPath = Path.Combine(workDirectory, "smoke-profile.xml");
        var profileStore = new AtomicXmlConfigurationStore<EngineProfile>();
        var commandStore = new AtomicXmlConfigurationStore<SessionCommandEnvelope>();
        var statusStore = new AtomicXmlConfigurationStore<RuntimeSessionStatus>();
        var timeProvider = new SystemTimeProvider();
        using var logger = new StructuredDiagnosticLogger(timeProvider);
        var presenter = new DashboardPresenter(profileStore, commandStore, statusStore, timeProvider, logger, xmlPath);

        using var form = new EngineDashboardForm(presenter);
        form.Show();
        Application.DoEvents();
        form._heartbeatTimer.Stop();

        if (form._tabs.TabPages.Count != 4 ||
            form._tabs.TabPages[0].Text != "Aim" ||
            form._tabs.TabPages[1].Text != "Visuals" ||
            form._tabs.TabPages[2].Text != "Misc" ||
            form._tabs.TabPages[3].Text != "Settings / Trạng thái")
        {
            details = "Cấu trúc 4 tab giao diện chuẩn (Aim, Visuals, Misc, Settings / Trạng thái) không khớp.";
            return false;
        }

        form._chkAimEnabled.Checked = true;
        form._chkSpreadComp.Checked = true;
        form._chkSmoothRecoil.Checked = true;
        form._chkDiagnostics.Checked = true;
        form._chkCrosshair.Checked = true;
        form._numAimRadius.Value = 1m;
        form._numSmooth.Value = 3.5m;
        form._numViewX.Value = -2.5m;
        form._cmbTargetPart.SelectedItem = TargetPart.Chest;
        form._cmbChamsMode.SelectedItem = ShaderRenderMode.Glow;
        form.PushControlsToProfile();

        if (_ComboMissingSelection(form._cmbTargetPart) ||
            _ComboMissingSelection(form._cmbActivationKey) ||
            _ComboMissingSelection(form._cmbChamsMode))
        {
            details = "ComboBox thiếu giá trị hiển thị.";
            return false;
        }

        if (!presenter.TrySaveProfile(xmlPath, out string saveMsg))
        {
            details = saveMsg;
            return false;
        }

        var reloaded = profileStore.Load(xmlPath);
        if (!reloaded.AimEnabled ||
            !reloaded.CompensateCommandSpread ||
            !reloaded.SmoothCameraRecoil ||
            !reloaded.RecordDiagnostics ||
            !reloaded.Crosshair ||
            Math.Abs(reloaded.AimRadiusPixels - 1f) > 0.01f ||
            Math.Abs(reloaded.Smooth - 3.5f) > 0.01f ||
            Math.Abs(reloaded.ViewX - (-2.5f)) > 0.01f ||
            reloaded.Target != TargetPart.Chest ||
            reloaded.ChamsMode != ShaderRenderMode.Glow)
        {
            details = "Roundtrip XML từ UI không khớp với giá trị control.";
            return false;
        }

        if (presenter.TryApplyCurrentProfile(out _))
        {
            details = "Apply khi chưa kết nối phải bị từ chối.";
            return false;
        }

        // Verify Command XML compatibility with wire protocol (<Command><Protocol>3</Protocol><Settings><NoSpread>true</NoSpread>...</Settings></Command>)
        string cmdXmlPath = Path.Combine(workDirectory, "smoke-command.xml");
        commandStore.Save(cmdXmlPath, new SessionCommandEnvelope
        {
            ProtocolVersion = NextWireProtocol.CurrentVersion,
            SessionToken = "0123456789abcdef0123456789abcdef",
            Revision = 1,
            Connected = true,
            ExpiresUtcTicks = timeProvider.UtcNow.AddSeconds(5).UtcTicks,
            Profile = reloaded
        });
        string rawCommandXml = File.ReadAllText(cmdXmlPath);
        if (!rawCommandXml.Contains("<NoSpread>true</NoSpread>", StringComparison.Ordinal) ||
            !rawCommandXml.Contains("<Protocol>3</Protocol>", StringComparison.Ordinal))
        {
            details = "Định dạng XML của SessionCommandEnvelope không khớp với giao thức runtime.";
            return false;
        }

        foreach (TabPage page in form._tabs.TabPages)
        {
            form._tabs.SelectedTab = page;
            Application.DoEvents();
            using var bitmap = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
            bitmap.Save(Path.Combine(workDirectory, $"tab-{page.TabIndex}-{form._tabs.SelectedIndex}.png"));
        }

        string reportPath = Path.Combine(workDirectory, "ui-smoke-test.txt");
        details = "PASS: real WinForms startup; 4 tabs rendered; settings/control/XML roundtrip; Apply rejected while disconnected. No game connection simulated.";
        File.WriteAllText(reportPath, details);
        form.Close();
        return true;
    }

    public string StatusText => _lblStatus.Text;
    public string ConnectionBannerText => _lblConnectionBanner.Text;

    public IReadOnlyList<(string Feature, string State, string Detail)> GetLiveModuleGridRows()
    {
        var list = new List<(string, string, string)>();
        foreach (DataGridViewRow row in _gridLiveModules.Rows)
        {
            if (row.IsNewRow) continue;
            string f = row.Cells[0].Value?.ToString() ?? string.Empty;
            string s = row.Cells[1].Value?.ToString() ?? string.Empty;
            string d = row.Cells[2].Value?.ToString() ?? string.Empty;
            list.Add((f, s, d));
        }
        return list;
    }

    public void RefreshProcessesFromUi() => RefreshProcessList();
    public void StartTestRuntimeFromUi() => StartAuthorizedTestRuntime();
    public void ConnectSelectedProcessFromUi() => ConnectToSelectedProcess();
    public void ApplyCurrentProfileFromUi() => ApplyCurrentToGame();
    public void DisconnectFromUi() => DisconnectFromGame();
    public void DisableAllFromUi() => DisableAllFeatures();

    private static bool _ComboMissingSelection(ComboBox combo) =>
        combo.SelectedItem is null || string.IsNullOrEmpty(combo.SelectedItem.ToString());

    private bool _disposed;
    protected override void Dispose(bool disposing)
    {
        if (disposing && !_disposed)
        {
            _disposed = true;
            _heartbeatTimer.Stop();
            _presenter.Dispose();
            _heartbeatTimer.Dispose();
            base.Dispose(disposing);
            _uiFont.Dispose();
            return;
        }
        base.Dispose(disposing);
    }
}
