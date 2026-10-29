using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace DeskMonitor
{
    public class SettingsForm : Form
    {
        private readonly MainForm _mainForm;
        private readonly AppSettings _settings;
        private readonly double _initialOpacity;

        private CheckBox _chkTopMost;
        private CheckBox _chkSnapToEdges;
        private NumericUpDown _numSnapDistance;
        private TrackBar _trackOpacity;
        private Label _lblOpacityVal;

        private CheckBox _chkShowGraph;
        private CheckBox _chkShowCoreBars;
        private ComboBox _cmbGraphPeriod;
        private ComboBox _cmbUpdateInterval;

        private CheckBox _chkAutoStart;

        // Colors
        private Color _cpuColor;
        private Color _gpuColor;
        private Color _ramColor;
        private Color _pingColor;
        private Color _netDownColor;
        private Color _netUpColor;
        private Color _textColor;

        private Panel _pnlCpuColor;
        private Panel _pnlGpuColor;
        private Panel _pnlRamColor;
        private Panel _pnlPingColor;
        private Panel _pnlNetDownColor;
        private Panel _pnlNetUpColor;
        private Panel _pnlTextColor;

        // Ping targets
        private ListBox _lstPingTargets;
        private TextBox _txtNewPingTarget;
        private Button _btnAddPingTarget;
        private Button _btnRemovePingTarget;

        // Top CPU Processes
        private CheckBox _chkShowTopProcesses;
        private NumericUpDown _numTopCount;
        private ComboBox _cmbTopWindow;
        private ComboBox _cmbTopPollInterval;
        private CheckBox _chkTopIncludeExited;

        private Button _btnSave;
        private Button _btnCancel;
        private Button _btnResetDefaults;

        public SettingsForm(MainForm mainForm, AppSettings settings)
        {
            _mainForm = mainForm;
            _settings = settings;
            _initialOpacity = settings.Opacity;

            _cpuColor = settings.GetCpuColor();
            _gpuColor = settings.GetGpuColor();
            _ramColor = settings.GetRamColor();
            _pingColor = settings.GetPingColor();
            _netDownColor = settings.GetNetDownColor();
            _netUpColor = settings.GetNetUpColor();
            _textColor = settings.GetTextColor();

            InitializeComponent();
            LoadCurrentValues();
        }

        private void InitializeComponent()
        {
            this.Text = "Настройки монитора";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(420, 510);
            this.Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
            this.BackColor = Color.FromArgb(245, 245, 247);
            this.ShowInTaskbar = false;
            if (_mainForm != null && _mainForm.Icon != null)
            {
                this.Icon = _mainForm.Icon;
            }

            TabControl tabs = new TabControl
            {
                Location = new Point(12, 12),
                Size = new Size(396, 440)
            };

            // TAB 1: General & Window
            TabPage tabGeneral = new TabPage("Общие");
            tabGeneral.BackColor = Color.FromArgb(250, 250, 252);
            tabGeneral.Padding = new Padding(10);

            GroupBox grpWindow = new GroupBox
            {
                Text = "Окно и отображение",
                Location = new Point(10, 10),
                Size = new Size(368, 140)
            };

            _chkTopMost = new CheckBox
            {
                Text = "Поверх всех окон (Always on Top)",
                Location = new Point(14, 22),
                AutoSize = true
            };
            grpWindow.Controls.Add(_chkTopMost);

            _chkSnapToEdges = new CheckBox
            {
                Text = "Прилипание к краям экрана",
                Location = new Point(14, 48),
                AutoSize = true
            };
            _chkSnapToEdges.CheckedChanged += (s, e) => { _numSnapDistance.Enabled = _chkSnapToEdges.Checked; };
            grpWindow.Controls.Add(_chkSnapToEdges);

            Label lblSnapDist = new Label
            {
                Text = "Дистанция:",
                Location = new Point(230, 50),
                AutoSize = true
            };
            grpWindow.Controls.Add(lblSnapDist);

            _numSnapDistance = new NumericUpDown
            {
                Location = new Point(304, 48),
                Width = 46,
                Minimum = 5,
                Maximum = 40,
                Value = 16
            };
            grpWindow.Controls.Add(_numSnapDistance);

            Label lblOpacity = new Label
            {
                Text = "Прозрачность:",
                Location = new Point(14, 78),
                AutoSize = true
            };
            grpWindow.Controls.Add(lblOpacity);

            _lblOpacityVal = new Label
            {
                Text = "95%",
                Location = new Point(110, 78),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            grpWindow.Controls.Add(_lblOpacityVal);

            _trackOpacity = new TrackBar
            {
                Location = new Point(10, 98),
                Width = 344,
                Minimum = 30,
                Maximum = 100,
                Value = 95,
                TickFrequency = 10
            };
            _trackOpacity.ValueChanged += OnOpacitySliderChanged;
            grpWindow.Controls.Add(_trackOpacity);

            tabGeneral.Controls.Add(grpWindow);

            GroupBox grpGraph = new GroupBox
            {
                Text = "Мониторинг ресурсов",
                Location = new Point(10, 158),
                Size = new Size(368, 150)
            };

            _chkShowCoreBars = new CheckBox
            {
                Text = "Показывать полоски ядер CPU (в один столбец)",
                Location = new Point(14, 22),
                AutoSize = true
            };
            grpGraph.Controls.Add(_chkShowCoreBars);

            _chkShowGraph = new CheckBox
            {
                Text = "Показывать график нагрузки CPU и RAM",
                Location = new Point(14, 46),
                AutoSize = true
            };
            _chkShowGraph.CheckedChanged += (s, e) => { _cmbGraphPeriod.Enabled = _chkShowGraph.Checked; };
            grpGraph.Controls.Add(_chkShowGraph);

            Label lblPeriod = new Label
            {
                Text = "Период графика:",
                Location = new Point(14, 76),
                AutoSize = true
            };
            grpGraph.Controls.Add(lblPeriod);

            _cmbGraphPeriod = new ComboBox
            {
                Location = new Point(170, 73),
                Width = 180,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbGraphPeriod.Items.Add(new PeriodItem("30 секунд", 30));
            _cmbGraphPeriod.Items.Add(new PeriodItem("60 секунд (1 мин)", 60));
            _cmbGraphPeriod.Items.Add(new PeriodItem("120 секунд (2 мин)", 120));
            _cmbGraphPeriod.Items.Add(new PeriodItem("300 секунд (5 мин)", 300));
            grpGraph.Controls.Add(_cmbGraphPeriod);

            Label lblInterval = new Label
            {
                Text = "Частота обновления:",
                Location = new Point(14, 108),
                AutoSize = true
            };
            grpGraph.Controls.Add(lblInterval);

            _cmbUpdateInterval = new ComboBox
            {
                Location = new Point(170, 105),
                Width = 180,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbUpdateInterval.Items.Add(new IntervalItem("0.5 секунды (быстро)", 500));
            _cmbUpdateInterval.Items.Add(new IntervalItem("1.0 секунда (по умолчанию)", 1000));
            _cmbUpdateInterval.Items.Add(new IntervalItem("2.0 секунды (экономно)", 2000));
            grpGraph.Controls.Add(_cmbUpdateInterval);

            tabGeneral.Controls.Add(grpGraph);

            GroupBox grpSystem = new GroupBox
            {
                Text = "Система",
                Location = new Point(10, 316),
                Size = new Size(368, 55)
            };
            _chkAutoStart = new CheckBox
            {
                Text = "Запускать вместе с Windows (Автозагрузка)",
                Location = new Point(14, 22),
                AutoSize = true
            };
            grpSystem.Controls.Add(_chkAutoStart);
            tabGeneral.Controls.Add(grpSystem);

            tabs.TabPages.Add(tabGeneral);

            // TAB 2: Colors
            TabPage tabColors = new TabPage("Цвета");
            tabColors.BackColor = Color.FromArgb(250, 250, 252);
            tabColors.Padding = new Padding(15);

            GroupBox grpColors = new GroupBox
            {
                Text = "Цветовая схема оформления",
                Location = new Point(10, 10),
                Size = new Size(368, 380)
            };

            int colY = 24;
            int colStep = 38;

            // CPU Color
            AddColorRow(grpColors, "Цвет CPU (график и ядра):", ref _cpuColor, out _pnlCpuColor, colY);

            // GPU Color
            colY += colStep;
            AddColorRow(grpColors, "Цвет GPU (шкала):", ref _gpuColor, out _pnlGpuColor, colY);

            // RAM Color
            colY += colStep;
            AddColorRow(grpColors, "Цвет RAM (график и шкала):", ref _ramColor, out _pnlRamColor, colY);

            // Ping Color
            colY += colStep;
            AddColorRow(grpColors, "Цвет Ping (график):", ref _pingColor, out _pnlPingColor, colY);

            // Network Download Color
            colY += colStep;
            AddColorRow(grpColors, "Цвет Сети ↓ (Загрузка):", ref _netDownColor, out _pnlNetDownColor, colY);

            // Network Upload Color
            colY += colStep;
            AddColorRow(grpColors, "Цвет Сети ↑ (Отдача):", ref _netUpColor, out _pnlNetUpColor, colY);

            // Text Color
            colY += colStep;
            AddColorRow(grpColors, "Цвет текста и подписей:", ref _textColor, out _pnlTextColor, colY);

            tabColors.Controls.Add(grpColors);
            tabs.TabPages.Add(tabColors);

            // TAB 3: Top CPU Processes
            TabPage tabTopProc = new TabPage("Топ CPU ⚡");
            tabTopProc.BackColor = Color.FromArgb(250, 250, 252);
            tabTopProc.Padding = new Padding(15);

            GroupBox grpTop = new GroupBox
            {
                Text = "Окно топ-процессов по загрузке CPU",
                Location = new Point(10, 10),
                Size = new Size(368, 380)
            };

            _chkShowTopProcesses = new CheckBox
            {
                Text = "Отображать значок молнии ⚡ возле надписи CPU",
                Location = new Point(14, 25),
                AutoSize = true,
                Font = new Font("Segoe UI", 9F, FontStyle.Bold)
            };
            grpTop.Controls.Add(_chkShowTopProcesses);

            Label lblTopDesc = new Label
            {
                Text = "При нажатии на значок ⚡ открывается окно со списком процессов, вызвавших наибольшую нагрузку на процессор.",
                Location = new Point(14, 50),
                Size = new Size(345, 34)
            };
            grpTop.Controls.Add(lblTopDesc);

            Label lblCount = new Label
            {
                Text = "Количество процессов в топе:",
                Location = new Point(14, 94),
                AutoSize = true
            };
            grpTop.Controls.Add(lblCount);

            _numTopCount = new NumericUpDown
            {
                Location = new Point(230, 92),
                Width = 120,
                Minimum = 3,
                Maximum = 15,
                Value = 5
            };
            grpTop.Controls.Add(_numTopCount);

            Label lblWindow = new Label
            {
                Text = "Период времени для анализа:",
                Location = new Point(14, 128),
                AutoSize = true
            };
            grpTop.Controls.Add(lblWindow);

            _cmbTopWindow = new ComboBox
            {
                Location = new Point(230, 125),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbTopWindow.Items.Add(new PeriodItem("1 минута", 1));
            _cmbTopWindow.Items.Add(new PeriodItem("3 минуты", 3));
            _cmbTopWindow.Items.Add(new PeriodItem("5 минут", 5));
            _cmbTopWindow.Items.Add(new PeriodItem("10 минут", 10));
            _cmbTopWindow.Items.Add(new PeriodItem("15 минут", 15));
            _cmbTopWindow.Items.Add(new PeriodItem("30 минут", 30));
            _cmbTopWindow.Items.Add(new PeriodItem("60 минут (1ч)", 60));
            grpTop.Controls.Add(_cmbTopWindow);

            Label lblPoll = new Label
            {
                Text = "Частота опроса процессов:",
                Location = new Point(14, 162),
                AutoSize = true
            };
            grpTop.Controls.Add(lblPoll);

            _cmbTopPollInterval = new ComboBox
            {
                Location = new Point(230, 159),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _cmbTopPollInterval.Items.Add(new IntervalItem("1 секунда", 1));
            _cmbTopPollInterval.Items.Add(new IntervalItem("2 секунды", 2));
            _cmbTopPollInterval.Items.Add(new IntervalItem("3 секунды", 3));
            _cmbTopPollInterval.Items.Add(new IntervalItem("5 секунд", 5));
            _cmbTopPollInterval.Items.Add(new IntervalItem("10 секунд", 10));
            grpTop.Controls.Add(_cmbTopPollInterval);

            _chkTopIncludeExited = new CheckBox
            {
                Text = "Учитывать процессы, завершившиеся в этот период (позволяет увидеть тяжелые сборки и задачи, которые уже закрылись)",
                Location = new Point(14, 196),
                Size = new Size(340, 52)
            };
            grpTop.Controls.Add(_chkTopIncludeExited);

            Panel pnlInfoBox = new Panel
            {
                Location = new Point(14, 258),
                Size = new Size(340, 105),
                BackColor = Color.FromArgb(240, 243, 246),
                BorderStyle = BorderStyle.FixedSingle
            };
            Label lblInfoBox = new Label
            {
                Text = "Энергоэффективность:\r\nСбор телеметрии выполняется в системном пуле потоков с квантом менее 0.02% CPU. При отключении опции фоновый сбор полностью останавливается.",
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                ForeColor = Color.FromArgb(70, 75, 90)
            };
            pnlInfoBox.Controls.Add(lblInfoBox);
            grpTop.Controls.Add(pnlInfoBox);

            tabTopProc.Controls.Add(grpTop);
            tabs.TabPages.Add(tabTopProc);

            // TAB 4: Ping Targets
            TabPage tabPing = new TabPage("Пинг (Ping)");
            tabPing.BackColor = Color.FromArgb(250, 250, 252);
            tabPing.Padding = new Padding(15);

            GroupBox grpPing = new GroupBox
            {
                Text = "Целевые адреса для пинга",
                Location = new Point(10, 10),
                Size = new Size(368, 380)
            };

            Label lblPingInfo = new Label
            {
                Text = "Каждый добавленный адрес отображается отдельным графиком на панели с текущим, минимальным, средним и максимальным пингом.",
                Location = new Point(14, 22),
                Size = new Size(340, 42)
            };
            grpPing.Controls.Add(lblPingInfo);

            _lstPingTargets = new ListBox
            {
                Location = new Point(14, 70),
                Size = new Size(240, 230)
            };
            grpPing.Controls.Add(_lstPingTargets);

            _btnRemovePingTarget = new Button
            {
                Text = "Удалить",
                Location = new Point(262, 70),
                Size = new Size(92, 28)
            };
            _btnRemovePingTarget.Click += OnRemovePingTargetClick;
            grpPing.Controls.Add(_btnRemovePingTarget);

            Label lblAddHost = new Label
            {
                Text = "Новый адрес (IP или домен):",
                Location = new Point(14, 310),
                AutoSize = true
            };
            grpPing.Controls.Add(lblAddHost);

            _txtNewPingTarget = new TextBox
            {
                Location = new Point(14, 332),
                Size = new Size(240, 24)
            };
            grpPing.Controls.Add(_txtNewPingTarget);

            _btnAddPingTarget = new Button
            {
                Text = "Добавить",
                Location = new Point(262, 330),
                Size = new Size(92, 28)
            };
            _btnAddPingTarget.Click += OnAddPingTargetClick;
            grpPing.Controls.Add(_btnAddPingTarget);

            tabPing.Controls.Add(grpPing);
            tabs.TabPages.Add(tabPing);

            this.Controls.Add(tabs);

            // Bottom Buttons
            _btnResetDefaults = new Button
            {
                Text = "По умолчанию",
                Location = new Point(12, 464),
                Size = new Size(110, 30),
                BackColor = Color.White
            };
            _btnResetDefaults.Click += OnResetDefaultsClick;
            this.Controls.Add(_btnResetDefaults);

            _btnSave = new Button
            {
                Text = "Сохранить",
                Location = new Point(222, 464),
                Size = new Size(90, 30),
                BackColor = Color.FromArgb(37, 99, 235),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat
            };
            _btnSave.FlatAppearance.BorderSize = 0;
            _btnSave.Click += OnSaveClick;
            this.Controls.Add(_btnSave);

            _btnCancel = new Button
            {
                Text = "Отмена",
                Location = new Point(318, 464),
                Size = new Size(90, 30),
                BackColor = Color.White
            };
            _btnCancel.Click += OnCancelClick;
            this.Controls.Add(_btnCancel);

            this.AcceptButton = _btnSave;
            this.CancelButton = _btnCancel;
        }

        private void AddColorRow(GroupBox parent, string labelText, ref Color targetColor, out Panel outPanel, int y)
        {
            Label lbl = new Label { Text = labelText, Location = new Point(16, y + 3), AutoSize = true };
            Panel pnl = new Panel { Location = new Point(220, y), Size = new Size(40, 24), BorderStyle = BorderStyle.FixedSingle, BackColor = targetColor };
            Button btn = new Button { Text = "Выбрать...", Location = new Point(270, y - 1), Size = new Size(80, 26) };

            btn.Click += (s, e) =>
            {
                using (ColorDialog cd = new ColorDialog())
                {
                    cd.Color = pnl.BackColor;
                    cd.FullOpen = true;
                    if (cd.ShowDialog(this) == DialogResult.OK)
                    {
                        pnl.BackColor = cd.Color;
                    }
                }
            };

            parent.Controls.Add(lbl);
            parent.Controls.Add(pnl);
            parent.Controls.Add(btn);

            outPanel = pnl;
        }

        private void LoadCurrentValues()
        {
            _chkTopMost.Checked = _settings.TopMost;
            _chkSnapToEdges.Checked = _settings.SnapToEdges;
            _numSnapDistance.Value = Math.Max(_numSnapDistance.Minimum, Math.Min(_numSnapDistance.Maximum, _settings.SnapDistance));
            _numSnapDistance.Enabled = _chkSnapToEdges.Checked;

            int opacityPct = (int)Math.Round(_settings.Opacity * 100);
            if (opacityPct < 30) opacityPct = 30;
            if (opacityPct > 100) opacityPct = 100;
            _trackOpacity.Value = opacityPct;
            _lblOpacityVal.Text = opacityPct + "%";

            _chkShowCoreBars.Checked = _settings.ShowCoreBars;
            _chkShowGraph.Checked = _settings.ShowGraph;

            // Select period
            int selectedPeriodIdx = 1;
            for (int i = 0; i < _cmbGraphPeriod.Items.Count; i++)
            {
                PeriodItem item = _cmbGraphPeriod.Items[i] as PeriodItem;
                if (item != null && item.Seconds == _settings.GraphPeriodSeconds)
                {
                    selectedPeriodIdx = i;
                    break;
                }
            }
            _cmbGraphPeriod.SelectedIndex = selectedPeriodIdx;
            _cmbGraphPeriod.Enabled = _chkShowGraph.Checked;

            // Select interval
            int selectedIntervalIdx = 1;
            for (int i = 0; i < _cmbUpdateInterval.Items.Count; i++)
            {
                IntervalItem item = _cmbUpdateInterval.Items[i] as IntervalItem;
                if (item != null && item.Milliseconds == _settings.UpdateIntervalMs)
                {
                    selectedIntervalIdx = i;
                    break;
                }
            }
            _cmbUpdateInterval.SelectedIndex = selectedIntervalIdx;

            _chkAutoStart.Checked = _settings.AutoStart;

            // Colors
            _pnlCpuColor.BackColor = _cpuColor;
            _pnlGpuColor.BackColor = _gpuColor;
            _pnlRamColor.BackColor = _ramColor;
            _pnlPingColor.BackColor = _pingColor;
            _pnlNetDownColor.BackColor = _netDownColor;
            _pnlNetUpColor.BackColor = _netUpColor;
            _pnlTextColor.BackColor = _textColor;

            // Top CPU Processes
            _chkShowTopProcesses.Checked = _settings.ShowTopProcessesButton;
            _numTopCount.Value = Math.Max(_numTopCount.Minimum, Math.Min(_numTopCount.Maximum, _settings.TopProcessesCount));

            int selTopWin = 3; // 10 min
            for (int i = 0; i < _cmbTopWindow.Items.Count; i++)
            {
                PeriodItem item = _cmbTopWindow.Items[i] as PeriodItem;
                if (item != null && item.Seconds == _settings.TopProcessesWindowMinutes)
                {
                    selTopWin = i;
                    break;
                }
            }
            _cmbTopWindow.SelectedIndex = selTopWin;

            int selTopPoll = 2; // 3 sec
            for (int i = 0; i < _cmbTopPollInterval.Items.Count; i++)
            {
                IntervalItem item = _cmbTopPollInterval.Items[i] as IntervalItem;
                if (item != null && item.Milliseconds == _settings.TopProcessesPollIntervalSec)
                {
                    selTopPoll = i;
                    break;
                }
            }
            _cmbTopPollInterval.SelectedIndex = selTopPoll;

            _chkTopIncludeExited.Checked = _settings.TopProcessesIncludeExited;

            // Ping targets
            _lstPingTargets.Items.Clear();
            foreach (string t in _settings.PingTargets)
            {
                _lstPingTargets.Items.Add(t);
            }
            if (_lstPingTargets.Items.Count > 0)
            {
                _lstPingTargets.SelectedIndex = 0;
            }
        }

        private void OnAddPingTargetClick(object sender, EventArgs e)
        {
            string host = _txtNewPingTarget.Text.Trim();
            if (string.IsNullOrEmpty(host)) return;

            if (!_lstPingTargets.Items.Contains(host))
            {
                _lstPingTargets.Items.Add(host);
                _txtNewPingTarget.Clear();
            }
        }

        private void OnRemovePingTargetClick(object sender, EventArgs e)
        {
            if (_lstPingTargets.SelectedIndex >= 0)
            {
                if (_lstPingTargets.Items.Count <= 1)
                {
                    MessageBox.Show("Должен оставаться хотя бы один адрес для мониторинга.", "Внимание", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                _lstPingTargets.Items.RemoveAt(_lstPingTargets.SelectedIndex);
            }
        }

        private void OnOpacitySliderChanged(object sender, EventArgs e)
        {
            int val = _trackOpacity.Value;
            _lblOpacityVal.Text = val + "%";
            if (_mainForm != null) _mainForm.Opacity = val / 100.0;
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            _settings.TopMost = _chkTopMost.Checked;
            _settings.SnapToEdges = _chkSnapToEdges.Checked;
            _settings.SnapDistance = (int)_numSnapDistance.Value;
            _settings.Opacity = _trackOpacity.Value / 100.0;
            _settings.ShowCoreBars = _chkShowCoreBars.Checked;
            _settings.ShowGraph = _chkShowGraph.Checked;

            PeriodItem periodItem = _cmbGraphPeriod.SelectedItem as PeriodItem;
            if (periodItem != null) _settings.GraphPeriodSeconds = periodItem.Seconds;

            IntervalItem intervalItem = _cmbUpdateInterval.SelectedItem as IntervalItem;
            if (intervalItem != null) _settings.UpdateIntervalMs = intervalItem.Milliseconds;

            _settings.AutoStart = _chkAutoStart.Checked;

            // Colors - convert using ColorToHex to guarantee reliable #RRGGBB format
            _settings.CpuColorHex = AppSettings.ColorToHex(_pnlCpuColor.BackColor);
            _settings.GpuColorHex = AppSettings.ColorToHex(_pnlGpuColor.BackColor);
            _settings.RamColorHex = AppSettings.ColorToHex(_pnlRamColor.BackColor);
            _settings.PingColorHex = AppSettings.ColorToHex(_pnlPingColor.BackColor);
            _settings.NetDownColorHex = AppSettings.ColorToHex(_pnlNetDownColor.BackColor);
            _settings.NetUpColorHex = AppSettings.ColorToHex(_pnlNetUpColor.BackColor);
            _settings.TextColorHex = AppSettings.ColorToHex(_pnlTextColor.BackColor);

            // Top CPU Processes
            _settings.ShowTopProcessesButton = _chkShowTopProcesses.Checked;
            _settings.TopProcessesCount = (int)_numTopCount.Value;

            PeriodItem topWinItem = _cmbTopWindow.SelectedItem as PeriodItem;
            if (topWinItem != null) _settings.TopProcessesWindowMinutes = topWinItem.Seconds;

            IntervalItem topPollItem = _cmbTopPollInterval.SelectedItem as IntervalItem;
            if (topPollItem != null) _settings.TopProcessesPollIntervalSec = topPollItem.Milliseconds;

            _settings.TopProcessesIncludeExited = _chkTopIncludeExited.Checked;

            // Ping targets
            _settings.PingTargets.Clear();
            foreach (object item in _lstPingTargets.Items)
            {
                _settings.PingTargets.Add(item.ToString());
            }

            if (_mainForm != null) _mainForm.ApplySettings(_settings);
            _settings.Save();

            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void OnCancelClick(object sender, EventArgs e)
        {
            if (_mainForm != null) _mainForm.Opacity = _initialOpacity;
            this.DialogResult = DialogResult.Cancel;
            this.Close();
        }

        private void OnResetDefaultsClick(object sender, EventArgs e)
        {
            if (MessageBox.Show(this, "Сбросить все настройки к значениям по умолчанию?", "Подтверждение", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                AppSettings temp = new AppSettings();
                temp.SetDefaults();

                _chkTopMost.Checked = temp.TopMost;
                _chkSnapToEdges.Checked = temp.SnapToEdges;
                _numSnapDistance.Value = temp.SnapDistance;
                _trackOpacity.Value = (int)(temp.Opacity * 100);
                _chkShowCoreBars.Checked = temp.ShowCoreBars;
                _chkShowGraph.Checked = temp.ShowGraph;
                _chkAutoStart.Checked = temp.AutoStart;

                _pnlCpuColor.BackColor = temp.GetCpuColor();
                _pnlGpuColor.BackColor = temp.GetGpuColor();
                _pnlRamColor.BackColor = temp.GetRamColor();
                _pnlPingColor.BackColor = temp.GetPingColor();
                _pnlNetDownColor.BackColor = temp.GetNetDownColor();
                _pnlNetUpColor.BackColor = temp.GetNetUpColor();
                _pnlTextColor.BackColor = temp.GetTextColor();

                _chkShowTopProcesses.Checked = temp.ShowTopProcessesButton;
                _numTopCount.Value = temp.TopProcessesCount;
                _chkTopIncludeExited.Checked = temp.TopProcessesIncludeExited;

                _lstPingTargets.Items.Clear();
                foreach (string t in temp.PingTargets) _lstPingTargets.Items.Add(t);
                if (_lstPingTargets.Items.Count > 0) _lstPingTargets.SelectedIndex = 0;

                if (_mainForm != null) _mainForm.Opacity = temp.Opacity;
            }
        }

        private class PeriodItem
        {
            public string Name { get; private set; }
            public int Seconds { get; private set; }
            public PeriodItem(string name, int seconds) { Name = name; Seconds = seconds; }
            public override string ToString() { return Name; }
        }

        private class IntervalItem
        {
            public string Name { get; private set; }
            public int Milliseconds { get; private set; }
            public IntervalItem(string name, int milliseconds) { Name = name; Milliseconds = milliseconds; }
            public override string ToString() { return Name; }
        }
    }
}
