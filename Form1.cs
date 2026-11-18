using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using PulseMirror.Instruments;

namespace PulseMirror
{
    public partial class Form1 : Form
    {
        private readonly TextBox _resourceTextBox;
        private readonly Button _connectButton;
        private readonly PictureBox _screenPictureBox;
        private readonly ComboBox _verticalScaleComboBox;
        private readonly ComboBox _timebaseComboBox;
        private readonly Label _statusLabel;
        private readonly Button _saveImageButton;
        private readonly CheckBox _channel2EnabledCheckBox;
        private readonly ComboBox _channel2ScaleComboBox;
        private readonly CheckBox _channel1EnabledCheckBox;
        private readonly GroupBox _measurementsGroupBox;
        private readonly ComboBox _measurementChannelComboBox;
        private readonly CheckBox _readAllMeasurementsCheckBox;
        private readonly ListView _measurementsListView;
        private readonly NumericUpDown _channel1OffsetNumeric;
        private readonly NumericUpDown _channel2OffsetNumeric;
        private readonly GroupBox _triggerGroupBox;
        private readonly ComboBox _triggerTypeComboBox;
        private readonly ComboBox _triggerSourceComboBox;
        private readonly ComboBox _triggerModeComboBox;
        private bool _isUpdatingMeasurementSelection;
        private readonly Button _autoChannel1Button;
        private readonly Button _autoChannel2Button;

        private const double TargetVerticalDivisions = 6;     // of 8 divisions
        private const double TargetPeriods = 3;               // periods shown on screen
        private const double HorizontalDivisions = 12;
        private const int MaxAutoAdjustPasses = 3;

        private IOscilloscope _oscilloscope;
        private CancellationTokenSource _captureLoopCts;
        private Task _captureLoopTask;
        private bool _isClosing;
        private bool _isSyncingControls;
        private TextBox _logTextBox;
        private static readonly double[] VerticalScales =
        {
            0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10
        };

        private static readonly double[] TimebaseScales =
        {
            0.000000002,
            0.000000005, 0.00000001, 0.00000002, 0.00000005,
            0.0000001, 0.0000002, 0.0000005,
            0.000001, 0.000002, 0.000005,
            0.00001, 0.00002, 0.00005,
            0.0001, 0.0002, 0.0005,
            0.001, 0.002, 0.005,
            0.01, 0.02, 0.05,
            0.1, 0.2, 0.5,
            1, 2, 5
        };

        private const int ScreenCaptureWidth = 320;
        private const int ScreenCaptureHeight = 234;

        public Form1()
        {
            InitializeComponent();

            Text = "PulseMirror - Oscilloscope Viewer";
//            ClientSize = new Size(1190, 690);
            ClientSize = new Size(1390, 690);

            // Fixed-size window: prevents accidental resizing/maximizing that
            // would otherwise require reflowing all the manually-positioned controls.
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            var resourceLabel = new Label { Text = "USBTMC Resource:", Location = new Point(12, 15), AutoSize = true };
            _resourceTextBox = new TextBox
            {
                Location = new Point(110, 12),
                Width = 420,
                Text = "usbtmc://1AB1x0588"
            };

            _connectButton = new Button
            {
                Text = "Connect",
                Location = new Point(540, 10),
                Width = 100
            };
            _connectButton.Click += async (s, e) => await ConnectButton_ClickAsync();

            _statusLabel = new Label
            {
                Text = "Disconnected",
                Location = new Point(650, 15),
                AutoSize = true,
                ForeColor = Color.Firebrick
            };

            var vScaleLabel = new Label { Text = "CH1 V/div:", Location = new Point(12, 50), AutoSize = true };
            _verticalScaleComboBox = new ComboBox
            {
                Location = new Point(90, 47),
                Width = 90,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = false
            };
            foreach (var scale in VerticalScales)
            {
                _verticalScaleComboBox.Items.Add(FormatScale(scale, "V"));
            }
            _verticalScaleComboBox.SelectedIndexChanged += async (s, e) => await VerticalScaleComboBox_SelectedIndexChangedAsync();

            var tScaleLabel = new Label { Text = "Timebase s/div:", Location = new Point(195, 50), AutoSize = true };
            _timebaseComboBox = new ComboBox
            {
                Location = new Point(290, 47),
                Width = 110,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = false
            };
            foreach (var scale in TimebaseScales)
            {
                _timebaseComboBox.Items.Add(FormatScale(scale, "s"));
            }
            _timebaseComboBox.SelectedIndexChanged += async (s, e) => await TimebaseComboBox_SelectedIndexChangedAsync();

            _channel2EnabledCheckBox = new CheckBox
            {
                Text = "Enable CH2",
                Location = new Point(410, 49),
                AutoSize = true,
                Enabled = false
            };
            _channel2EnabledCheckBox.CheckedChanged += async (s, e) => await Channel2EnabledCheckBox_CheckedChangedAsync();

            var vScale2Label = new Label { Text = "CH2 V/div:", Location = new Point(505, 50), AutoSize = true };
            _channel2ScaleComboBox = new ComboBox
            {
                Location = new Point(580, 47),
                Width = 90,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = false
            };
            foreach (var scale in VerticalScales)
            {
                _channel2ScaleComboBox.Items.Add(FormatScale(scale, "V"));
            }
            _channel2ScaleComboBox.SelectedIndexChanged += async (s, e) => await Channel2ScaleComboBox_SelectedIndexChangedAsync();

            _saveImageButton = new Button
            {
                Text = "Save Image...",
                //Location = new Point(680, 47),
                Location = new Point(1040, 665),
                Width = 128,
                Enabled = false
            };
            _saveImageButton.Click += (s, e) => SaveImageButton_Click();

            _channel1EnabledCheckBox = new CheckBox
            {
                Text = "Enable CH1",
                Location = new Point(12, 82),
                AutoSize = true,
                Enabled = false
            };
            _channel1EnabledCheckBox.CheckedChanged += async (s, e) => await Channel1EnabledCheckBox_CheckedChangedAsync();

            var measurementChannelLabel = new Label { Text = "Channel:", Location = new Point(10, 24), AutoSize = true };
            _measurementChannelComboBox = new ComboBox
            {
                Location = new Point(70, 21),
                Width = 80,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            _measurementChannelComboBox.Items.Add("CH1");
            _measurementChannelComboBox.Items.Add("CH2");
            _measurementChannelComboBox.SelectedIndex = 0;

            _readAllMeasurementsCheckBox = new CheckBox
            {
                Text = "Read all",
                Location = new Point(10, 24),
                AutoSize = true
            };

            _measurementsListView = new ListView
            {
                Location = new Point(10, 50),
                Size = new Size(328, 350),
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                MultiSelect = false,
                HeaderStyle = ColumnHeaderStyle.Nonclickable
            };
            _measurementsListView.Columns.Add("Measurement", 120);
            _measurementsListView.Columns.Add("CH1", 95, HorizontalAlignment.Right);
            _measurementsListView.Columns.Add("CH2", 95, HorizontalAlignment.Right);

            foreach (MeasurementQuantity quantity in Enum.GetValues(typeof(MeasurementQuantity)))
            {
                var item = new ListViewItem(quantity.ToString()) { Tag = quantity };
                item.SubItems.Add("-");
                item.SubItems.Add("-");
                _measurementsListView.Items.Add(item);
            }

            _readAllMeasurementsCheckBox.CheckedChanged += (s, e) => ReadAllMeasurementsCheckBox_CheckedChanged();
            _measurementsListView.ItemChecked += (s, e) => MeasurementsListView_ItemChecked(e);

            _measurementsGroupBox = new GroupBox
            {
                Text = "Measurements (SCPI)",
                Location = new Point(820, 85),
                Size = new Size(348, 410),
                Enabled = false
            };
            _measurementsGroupBox.Controls.Add(_readAllMeasurementsCheckBox);
            _measurementsGroupBox.Controls.Add(_measurementsListView);

            _screenPictureBox = new PictureBox
            {
                Location = new Point(12, 150),
                Size = new Size(ScreenCaptureWidth, ScreenCaptureHeight),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Normal,
                BackColor = Color.Black
            };

            var logLabel = new Label { Text = "Messages:", Location = new Point(12, 613), AutoSize = true };

            // Read-only, multiline, selectable/copyable log footer. Using a
            // TextBox (instead of a Label) enables text selection and Ctrl+C,
            // which a Label does not support.
            _logTextBox = new TextBox   
            {
                Location = new Point(12, 633),
                Size = new Size(796, 70),
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = SystemColors.Window,
                Font = new Font(FontFamily.GenericMonospace, 8.25f)
            };

            Controls.Add(resourceLabel);
            Controls.Add(_resourceTextBox);
            Controls.Add(_connectButton);
            Controls.Add(_statusLabel);
            Controls.Add(vScaleLabel);
            Controls.Add(_verticalScaleComboBox);
            Controls.Add(tScaleLabel);
            Controls.Add(_timebaseComboBox);
            Controls.Add(_channel2EnabledCheckBox);
            Controls.Add(vScale2Label);
            Controls.Add(_channel2ScaleComboBox);
            Controls.Add(_saveImageButton);
            Controls.Add(_screenPictureBox);
            Controls.Add(logLabel);
            Controls.Add(_logTextBox);
            Controls.Add(_channel1EnabledCheckBox);
            Controls.Add(_measurementsGroupBox);

            var offset1Label = new Label { Text = "Offset (V):", AutoSize = true };
_channel1OffsetNumeric = CreateOffsetNumeric(Point.Empty);
_channel1OffsetNumeric.ValueChanged += async (s, e) => await ChannelOffsetNumeric_ValueChangedAsync(1, _channel1OffsetNumeric);

var offset2Label = new Label { Text = "CH2 Offset (V):", AutoSize = true, Visible = false };
_channel2OffsetNumeric = CreateOffsetNumeric(Point.Empty);
_channel2OffsetNumeric.ValueChanged += async (s, e) => await ChannelOffsetNumeric_ValueChangedAsync(2, _channel2OffsetNumeric);

            // ---- Trigger group (created before any layout code uses it) ----
            _triggerGroupBox = new GroupBox
            {
                Text = "Trigger",
                Size = new Size(210, 135),
                Enabled = false
            };

            var triggerTypeLabel = new Label { Text = "Type:", Location = new Point(10, 28), AutoSize = true };
            _triggerTypeComboBox = new ComboBox
            {
                Location = new Point(75, 25),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (TriggerType type in Enum.GetValues(typeof(TriggerType)))
            {
                _triggerTypeComboBox.Items.Add(type);
            }
            _triggerTypeComboBox.SelectedIndexChanged += async (s, e) => await TriggerTypeComboBox_SelectedIndexChangedAsync();

            var triggerSourceLabel = new Label { Text = "Source:", Location = new Point(10, 63), AutoSize = true };
            _triggerSourceComboBox = new ComboBox
            {
                Location = new Point(75, 60),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (TriggerSource source in Enum.GetValues(typeof(TriggerSource)))
            {
                _triggerSourceComboBox.Items.Add(source);
            }
            _triggerSourceComboBox.SelectedIndexChanged += async (s, e) => await TriggerSourceComboBox_SelectedIndexChangedAsync();

            var triggerModeLabel = new Label { Text = "Mode:", Location = new Point(10, 98), AutoSize = true };
            _triggerModeComboBox = new ComboBox
            {
                Location = new Point(75, 95),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            foreach (TriggerMode mode in Enum.GetValues(typeof(TriggerMode)))
            {
                _triggerModeComboBox.Items.Add(mode);
            }
            _triggerModeComboBox.SelectedIndexChanged += async (s, e) => await TriggerModeComboBox_SelectedIndexChangedAsync();

            _triggerGroupBox.Controls.Add(triggerTypeLabel);
            _triggerGroupBox.Controls.Add(_triggerTypeComboBox);
            _triggerGroupBox.Controls.Add(triggerSourceLabel);
            _triggerGroupBox.Controls.Add(_triggerSourceComboBox);
            _triggerGroupBox.Controls.Add(triggerModeLabel);
            _triggerGroupBox.Controls.Add(_triggerModeComboBox);
            var autoLabel = new Label { Text = "Auto-adjust:", AutoSize = true };

            _autoChannel1Button = new Button { Text = "Auto CH1", Enabled = false };
            _autoChannel1Button.Click += async (s, e) => await AutoAdjustChannelAsync(1);

            _autoChannel2Button = new Button { Text = "Auto CH2", Enabled = false };
            _autoChannel2Button.Click += async (s, e) => await AutoAdjustChannelAsync(2);

            Controls.Add(autoLabel);
            Controls.Add(_autoChannel1Button);
            Controls.Add(_autoChannel2Button);
            Controls.Add(offset1Label);
            Controls.Add(_channel1OffsetNumeric);
            Controls.Add(offset2Label);
            Controls.Add(_channel2OffsetNumeric);
            Controls.Add(_triggerGroupBox);

            FormClosing += Form1_FormClosing;

            var resourceToolTip = new ToolTip();
            resourceToolTip.SetToolTip(_resourceTextBox,
                "USBTMC direto via WinUSB.\n" +
                "Formato: usbtmc://VIDxPID (ex.: usbtmc://1AB1x0588)\n" +
                "Ou apenas 'usbtmc' para detectar o DS1052E automaticamente.");

            // ================= Consolidated layout (single pass) =================
            const int colWidth = 90;

            // Left: instrument screen, then the message log aligned with it.
            _screenPictureBox.Location = new Point(12, 12);
            _screenPictureBox.ClientSize = new Size(ScreenCaptureWidth * 2, ScreenCaptureHeight * 2);
            _screenPictureBox.SizeMode = PictureBoxSizeMode.Zoom;

            logLabel.Location = new Point(_screenPictureBox.Left, _screenPictureBox.Bottom + 8);
            _logTextBox.Location = new Point(_screenPictureBox.Left, logLabel.Bottom + 4);
            _logTextBox.Size = new Size(_screenPictureBox.Width, 110);

            // Middle column: channel controls, measurements, connection.
            int panelX = _screenPictureBox.Right + 20;
            int labelX = panelX + 10;
            int channel1X = panelX + 130;   // aligns with the CH1 column of the ListView
            int channel2X = panelX + 225;   // aligns with the CH2 column of the ListView

            vScaleLabel.Text = "V/div:";
            vScaleLabel.Location = new Point(labelX, 35);
            offset1Label.Location = new Point(labelX, 62);
            tScaleLabel.Text = "Timebase s/div:";
            tScaleLabel.Location = new Point(labelX, 95);

            // Per-channel labels are redundant: the column identifies the channel.
            vScale2Label.Visible = false;

            _channel1EnabledCheckBox.Location = new Point(channel1X, 7);
            _channel2EnabledCheckBox.Location = new Point(channel2X, 7);

            _verticalScaleComboBox.Location = new Point(channel1X, 32);
            _verticalScaleComboBox.Width = colWidth;
            _channel2ScaleComboBox.Location = new Point(channel2X, 32);
            _channel2ScaleComboBox.Width = colWidth;

            _channel1OffsetNumeric.Location = new Point(channel1X, 59);
            _channel1OffsetNumeric.Width = colWidth;
            _channel2OffsetNumeric.Location = new Point(channel2X, 59);
            _channel2OffsetNumeric.Width = colWidth;

            // Timebase is shared: spans both channel columns.
            _timebaseComboBox.Location = new Point(channel1X, 92);
            _timebaseComboBox.Width = (channel2X + colWidth) - channel1X;

            //var autoLabel = new Label { Text = "Auto-adjust:", AutoSize = true };
            autoLabel.Location = new Point(labelX, 125);
            _autoChannel1Button.Location = new Point(channel1X, 120);
            _autoChannel1Button.Width = colWidth;
            _autoChannel2Button.Location = new Point(channel2X, 120);
            _autoChannel2Button.Width = colWidth;

            _measurementsGroupBox.Location = new Point(panelX, 150);
            _measurementsGroupBox.Size = new Size(348, 410);
            _measurementsListView.Size = new Size(328, 350);

            resourceLabel.Location = new Point(labelX, 570);
            _resourceTextBox.Location = new Point(channel1X, 570);
            _resourceTextBox.Width = 218;

            _connectButton.Location = new Point(labelX, 620);
            _connectButton.Width = 100;
            _saveImageButton.Location = new Point(panelX + 220, 620);
            _saveImageButton.Width = 128;

            _statusLabel.MaximumSize = new Size(338, 0);
            _statusLabel.Location = new Point(labelX, 655);

            // Right column: trigger, placed after the measurements panel.
            _triggerGroupBox.Location = new Point(_measurementsGroupBox.Right + 20, 12);
            ClientSize = new Size(_triggerGroupBox.Right + 12, 720);


            //_autoChannel1Button = new Button { Text = "Auto CH1", Enabled = false };
            //_autoChannel1Button.Click += async (s, e) => await AutoAdjustChannelAsync(1);

            //_autoChannel2Button = new Button { Text = "Auto CH2", Enabled = false };
            //_autoChannel2Button.Click += async (s, e) => await AutoAdjustChannelAsync(2);

            Controls.Add(autoLabel);
            Controls.Add(_autoChannel1Button);
            Controls.Add(_autoChannel2Button);
        }

        /// <summary>
        /// Appends a timestamped line to the read-only message log at the
        /// bottom of the window. Thread-safe: marshals to the UI thread if
        /// called from a background task.
        /// </summary>
        private void AppendLog(string message)
        {
            void DoAppend()
            {
                string line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
                _logTextBox.AppendText(line);
            }

            if (_logTextBox.InvokeRequired)
            {
                _logTextBox.BeginInvoke(new Action(DoAppend));
            }
            else
            {
                DoAppend();
            }
        }

        private async Task ConnectButton_ClickAsync()
        {
            if (_oscilloscope != null && _oscilloscope.IsConnected)
            {
                await DisconnectAsync();
                return;
            }

            _connectButton.Enabled = false;
            _statusLabel.Text = "Connecting...";
            _statusLabel.ForeColor = Color.DarkOrange;
            AppendLog($"Connecting to '{_resourceTextBox.Text.Trim()}'...");

            try
            {
                string resourceName = _resourceTextBox.Text.Trim();
                _oscilloscope = await OscilloscopeFactory.CreateAsync(resourceName);

                _statusLabel.Text = $"Connected: {_oscilloscope.ModelName} (SN {_oscilloscope.SerialNumber})";
                _statusLabel.ForeColor = Color.ForestGreen;
                _connectButton.Text = "Disconnect";
                AppendLog($"Connected: {_oscilloscope.ModelName} (SN {_oscilloscope.SerialNumber})");

                await SyncControlsWithInstrumentAsync();
                await SyncTriggerControlsAsync();
                _triggerGroupBox.Enabled = true;
                _verticalScaleComboBox.Enabled = true;
                _timebaseComboBox.Enabled = true;
                _channel2EnabledCheckBox.Enabled = true;
                _saveImageButton.Enabled = true;
                _channel1EnabledCheckBox.Enabled = true;
                _channel1OffsetNumeric.Enabled = _channel1EnabledCheckBox.Checked;
                _channel2OffsetNumeric.Enabled = _channel2EnabledCheckBox.Checked;
                _measurementsGroupBox.Enabled = true;
                _timebaseComboBox.Enabled = true;
                _channel2EnabledCheckBox.Enabled = true;
                _saveImageButton.Enabled = true;
                _autoChannel1Button.Enabled = true;
                _autoChannel2Button.Enabled = true;

                await _oscilloscope.ReturnToLocalAsync();

                StartCaptureLoop();
            }
            catch (Exception ex)
            {
                _statusLabel.Text = "Connection failed";
                _statusLabel.ForeColor = Color.Firebrick;
                AppendLog($"ERROR connecting: {ex.Message}");
                MessageBox.Show(this, ex.Message, "Connection Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                _oscilloscope = null;
            }
            finally
            {
                _connectButton.Enabled = true;
            }
        }

        private async Task DisconnectAsync()
        {
            await StopCaptureLoopAsync();

            if (_oscilloscope is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _oscilloscope = null;
            _verticalScaleComboBox.Enabled = false;
            _timebaseComboBox.Enabled = false;
            _channel2EnabledCheckBox.Enabled = false;
            _channel2ScaleComboBox.Enabled = false;
            _channel1OffsetNumeric.Enabled = false;
            _channel2OffsetNumeric.Enabled = false;
            _saveImageButton.Enabled = false;
            _autoChannel1Button.Enabled = false;
            _autoChannel2Button.Enabled = false;
            _measurementsGroupBox.Enabled = false;
            foreach (ListViewItem item in _measurementsListView.Items)
            {
                item.SubItems[1].Text = "-";
            }
            _connectButton.Text = "Connect";
            _statusLabel.Text = "Disconnected";
            _statusLabel.ForeColor = Color.Firebrick;
            _screenPictureBox.Image = null;
            _triggerGroupBox.Enabled = false;
            AppendLog("Disconnected.");
        }
        // Add these methods inside Form1 class
        private async Task SyncTriggerControlsAsync()
        {
            if (_oscilloscope == null || !_oscilloscope.IsConnected)
            {
                return;
            }

            _isSyncingControls = true;
            try
            {
                TriggerSettings settings = await _oscilloscope.GetTriggerSettingsAsync();
                _triggerTypeComboBox.SelectedItem = settings.Type;
                _triggerSourceComboBox.SelectedItem = settings.Source;
                _triggerModeComboBox.SelectedItem = settings.Mode;
            }
            finally
            {
                _isSyncingControls = false;
            }
        }

        private async Task TriggerTypeComboBox_SelectedIndexChangedAsync()
        {
            if (_isSyncingControls || _oscilloscope == null || !_oscilloscope.IsConnected || _triggerTypeComboBox.SelectedItem == null)
            {
                return;
            }

            try
            {
                await _oscilloscope.SetTriggerTypeAsync((TriggerType)_triggerTypeComboBox.SelectedItem);
                await SyncTriggerControlsAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"ERROR setting trigger type: {ex.Message}");
                MessageBox.Show(this, ex.Message, "Communication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task TriggerSourceComboBox_SelectedIndexChangedAsync()
        {
            if (_isSyncingControls || _oscilloscope == null || !_oscilloscope.IsConnected || _triggerSourceComboBox.SelectedItem == null)
            {
                return;
            }

            try
            {
                await _oscilloscope.SetTriggerSourceAsync((TriggerSource)_triggerSourceComboBox.SelectedItem);
            }
            catch (Exception ex)
            {
                AppendLog($"ERROR setting trigger source: {ex.Message}");
                MessageBox.Show(this, ex.Message, "Communication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task TriggerModeComboBox_SelectedIndexChangedAsync()
        {
            if (_isSyncingControls || _oscilloscope == null || !_oscilloscope.IsConnected || _triggerModeComboBox.SelectedItem == null)
            {
                return;
            }

            try
            {
                await _oscilloscope.SetTriggerModeAsync((TriggerMode)_triggerModeComboBox.SelectedItem);
            }
            catch (Exception ex)
            {
                AppendLog($"ERROR setting trigger mode: {ex.Message}");
                MessageBox.Show(this, ex.Message, "Communication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
        private async Task SyncControlsWithInstrumentAsync()
        {
            _isSyncingControls = true;
            try
            {
                double vScale = await _oscilloscope.GetChannelScaleAsync(1);
                SelectClosest(_verticalScaleComboBox, VerticalScales, vScale, "V");

                double tScale = await _oscilloscope.GetTimebaseAsync();
                SelectClosest(_timebaseComboBox, TimebaseScales, tScale, "s");

                bool ch2Enabled = await _oscilloscope.GetChannelEnabledAsync(2);
                _channel2EnabledCheckBox.Checked = ch2Enabled;
                _channel2ScaleComboBox.Enabled = ch2Enabled;

                if (ch2Enabled)
                {
                    double ch2Scale = await _oscilloscope.GetChannelScaleAsync(2);
                    SelectClosest(_channel2ScaleComboBox, VerticalScales, ch2Scale, "V");
                }

                bool ch1Enabled = await _oscilloscope.GetChannelEnabledAsync(1);
                _channel1EnabledCheckBox.Checked = ch1Enabled;
                _verticalScaleComboBox.Enabled = ch1Enabled;
                SetNumericValueClamped(_channel1OffsetNumeric, await _oscilloscope.GetChannelOffsetAsync(1));

                if (ch2Enabled)
                {
                    SetNumericValueClamped(_channel2OffsetNumeric, await _oscilloscope.GetChannelOffsetAsync(2));
                }
                AppendLog($"CH2 is {(ch2Enabled ? "enabled" : "disabled")} on the instrument.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to read current instrument settings: {ex.Message}",
                    "Synchronization Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                _isSyncingControls = false;
            }
        }

        private static void SelectClosest(ComboBox comboBox, double[] scales, double value, string unit)
        {
            int closestIndex = 0;
            double closestDiff = double.MaxValue;

            for (int i = 0; i < scales.Length; i++)
            {
                double diff = Math.Abs(scales[i] - value);
                if (diff < closestDiff)
                {
                    closestDiff = diff;
                    closestIndex = i;
                }
            }

            comboBox.SelectedIndex = closestIndex;
        }

        private async Task VerticalScaleComboBox_SelectedIndexChangedAsync()
        {
            if (_isSyncingControls || _oscilloscope == null || !_oscilloscope.IsConnected)
            {
                return;
            }

            try
            {
                double scale = VerticalScales[_verticalScaleComboBox.SelectedIndex];
                await _oscilloscope.SetChannelScaleAsync(1, scale);

                await Task.Delay(300);
                double confirmed = await _oscilloscope.GetChannelScaleAsync(1);
                AppendLog($"Sent {scale:0.######} V/div, instrument reports {confirmed:0.######} V/div");
            }
            catch (Exception ex)
            {
                AppendLog($"ERROR setting CH1 vertical scale: {ex.Message}");
                MessageBox.Show(this, ex.Message, "Communication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task TimebaseComboBox_SelectedIndexChangedAsync()
        {
            if (_isSyncingControls || _oscilloscope == null || !_oscilloscope.IsConnected)
            {
                return;
            }

            try
            {
                double scale = TimebaseScales[_timebaseComboBox.SelectedIndex];
                await _oscilloscope.SetTimebaseAsync(scale);

                await Task.Delay(300);
                double confirmed = await _oscilloscope.GetTimebaseAsync();
                AppendLog($"Sent {scale:0.######} s/div, instrument reports {confirmed:0.######} s/div");
            }
            catch (Exception ex)
            {
                AppendLog($"ERROR setting timebase: {ex.Message}");
                MessageBox.Show(this, ex.Message, "Communication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task Channel2EnabledCheckBox_CheckedChangedAsync()
        {
            if (_isSyncingControls || _oscilloscope == null || !_oscilloscope.IsConnected)
            {
                return;
            }

            try
            {
                bool enabled = _channel2EnabledCheckBox.Checked;
                await _oscilloscope.SetChannelEnabledAsync(2, enabled);
                _channel2ScaleComboBox.Enabled = enabled;
                _channel2OffsetNumeric.Enabled = enabled;

                AppendLog(enabled ? "CH2 enabled." : "CH2 disabled.");

                if (enabled)
                {
                    double scale = await _oscilloscope.GetChannelScaleAsync(2);
                    _isSyncingControls = true;
                    try
                    {
                        SelectClosest(_channel2ScaleComboBox, VerticalScales, scale, "V");
                    }
                    finally
                    {
                        _isSyncingControls = false;
                    }
                }
            }
            catch (Exception ex)
            {
                AppendLog($"ERROR toggling CH2: {ex.Message}");
                MessageBox.Show(this, ex.Message, "Communication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task Channel2ScaleComboBox_SelectedIndexChangedAsync()
        {
            if (_isSyncingControls || _oscilloscope == null || !_oscilloscope.IsConnected)
            {
                return;
            }

            try
            {
                double scale = VerticalScales[_channel2ScaleComboBox.SelectedIndex];
                await _oscilloscope.SetChannelScaleAsync(2, scale);

                await Task.Delay(300);
                double confirmed = await _oscilloscope.GetChannelScaleAsync(2);
                AppendLog($"Sent {scale:0.######} V/div (CH2), instrument reports {confirmed:0.######} V/div");
            }
            catch (Exception ex)
            {
                AppendLog($"ERROR setting CH2 vertical scale: {ex.Message}");
                MessageBox.Show(this, ex.Message, "Communication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private async Task Channel1EnabledCheckBox_CheckedChangedAsync()
        {
            if (_isSyncingControls || _oscilloscope == null || !_oscilloscope.IsConnected)
            {
                return;
            }

            bool enabled = _channel1EnabledCheckBox.Checked;

            try
            {
                await _oscilloscope.SetChannelEnabledAsync(1, enabled);
                _verticalScaleComboBox.Enabled = enabled;
                _channel1OffsetNumeric.Enabled = enabled;
                AppendLog(enabled ? "CH1 enabled." : "CH1 disabled.");
            }
            catch (Exception ex)
            {
                AppendLog($"ERROR toggling CH1: {ex.Message}");
                MessageBox.Show(this, ex.Message, "Communication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);

                // Revert the checkbox to keep the UI consistent with the instrument.
                SetCheckedSilently(_channel1EnabledCheckBox, !enabled);
            }
        }

        private async Task MeasurementCheckBox_CheckedChangedAsync(MeasurementType measurement, CheckBox checkBox)
        {
            if (_isSyncingControls || _oscilloscope == null || !_oscilloscope.IsConnected)
            {
                return;
            }

            bool enabled = checkBox.Checked;

            try
            {
                await _oscilloscope.SetMeasurementEnabledAsync(measurement, enabled);
                AppendLog($"Measurement '{measurement}' {(enabled ? "shown" : "hidden")}.");
            }
            catch (Exception ex)
            {
                AppendLog($"ERROR toggling measurement '{measurement}': {ex.Message}");
                MessageBox.Show(this, ex.Message, "Communication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                SetCheckedSilently(checkBox, !enabled);
            }
        }

        private void SetCheckedSilently(CheckBox checkBox, bool value)
        {
            bool previous = _isSyncingControls;
            _isSyncingControls = true;
            try
            {
                checkBox.Checked = value;
            }
            finally
            {
                _isSyncingControls = previous;
            }
        }

        private void ReadAllMeasurementsCheckBox_CheckedChanged()
        {
            if (_isUpdatingMeasurementSelection)
            {
                return;
            }

            _isUpdatingMeasurementSelection = true;
            try
            {
                foreach (ListViewItem item in _measurementsListView.Items)
                {
                    item.Checked = _readAllMeasurementsCheckBox.Checked;

                    if (!item.Checked)
                    {
                        ResetMeasurementItem(item);
                    }
                }
            }
            finally
            {
                _isUpdatingMeasurementSelection = false;
            }
        }

        private void MeasurementsListView_ItemChecked(ItemCheckedEventArgs e)
        {
            if (_isUpdatingMeasurementSelection)
            {
                return;
            }

            _isUpdatingMeasurementSelection = true;
            try
            {
                if (!e.Item.Checked)
                {
                    ResetMeasurementItem(e.Item);
                }

                bool allChecked = true;
                foreach (ListViewItem item in _measurementsListView.Items)
                {
                    if (!item.Checked)
                    {
                        allChecked = false;
                        break;
                    }
                }

                _readAllMeasurementsCheckBox.Checked = allChecked;
            }
            finally
            {
                _isUpdatingMeasurementSelection = false;
            }
        }

        /// <summary>
        /// Reads the checked measurements one by one. Runs on the UI thread
        /// (the awaits resume on the captured synchronization context).
        /// </summary>
        private async Task UpdateMeasurementsAsync(IOscilloscope oscilloscope, CancellationToken token)
        {
            foreach (ListViewItem item in _measurementsListView.Items)
            {
                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (!item.Checked)
                {
                    continue;
                }

                var quantity = (MeasurementQuantity)item.Tag;

                // Column 1 = CH1, column 2 = CH2.
                for (int channel = 1; channel <= 2; channel++)
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    var subItem = item.SubItems[channel];

                    // Skip channels that are disabled on the instrument.
                    bool channelEnabled = channel == 1
                        ? _channel1EnabledCheckBox.Checked
                        : _channel2EnabledCheckBox.Checked;

                    if (!channelEnabled)
                    {
                        subItem.Text = "-";
                        continue;
                    }

                    try
                    {
                        double value = await oscilloscope.GetMeasurementAsync(channel, quantity);
                        subItem.Text = FormatMeasurement(quantity, value);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        // Log only the first failure of each item to avoid flooding the log every cycle.
                        if (subItem.Text != "error")
                        {
                            AppendLog($"ERROR reading {item.Text} (CH{channel}): {ex.Message}");
                        }

                        subItem.Text = "error";
                    }
                }
            }
        }

        private static string GetMeasurementUnit(MeasurementQuantity quantity)
        {
            switch (quantity)
            {
                case MeasurementQuantity.Vpp:
                case MeasurementQuantity.Vmax:
                case MeasurementQuantity.Vmin:
                case MeasurementQuantity.Vamplitude:
                case MeasurementQuantity.Vtop:
                case MeasurementQuantity.Vbase:
                case MeasurementQuantity.Vaverage:
                case MeasurementQuantity.Vrms:
                    return "V";
                case MeasurementQuantity.Frequency:
                    return "Hz";
                case MeasurementQuantity.Period:
                case MeasurementQuantity.RiseTime:
                case MeasurementQuantity.FallTime:
                case MeasurementQuantity.PositiveWidth:
                case MeasurementQuantity.NegativeWidth:
                    return "s";
                default:
                    return string.Empty; // Overshoot, preshoot and duty cycle: raw instrument value.
            }
        }

        private static string FormatMeasurement(MeasurementQuantity quantity, double value)
        {
            if (double.IsNaN(value))
            {
                return "---";
            }

            string unit = GetMeasurementUnit(quantity);

            if (unit.Length == 0)
            {
                return $"{value:G4}";
            }

            if (value == 0)
            {
                return $"0 {unit}";
            }

            string[] prefixes = { "n", "µ", "m", "", "k", "M", "G" };
            int index = 3;
            double scaled = value;

            while (Math.Abs(scaled) >= 1000 && index < prefixes.Length - 1)
            {
                scaled /= 1000;
                index++;
            }

            while (Math.Abs(scaled) < 1 && index > 0)
            {
                scaled *= 1000;
                index--;
            }

            return $"{scaled:0.###} {prefixes[index]}{unit}";
        }

        private void StartCaptureLoop()
        {
            // Pass the instance explicitly: the loop must never read the
            // mutable _oscilloscope field, which is nulled on disconnect.
            var oscilloscope = _oscilloscope;
            _captureLoopCts = new CancellationTokenSource();
            _captureLoopTask = RunCaptureLoopAsync(oscilloscope, _captureLoopCts.Token);
        }

        private async Task StopCaptureLoopAsync()
        {
            var cts = _captureLoopCts;
            var task = _captureLoopTask;
            _captureLoopCts = null;
            _captureLoopTask = null;

            if (cts == null)
            {
                return;
            }

            cts.Cancel();

            try
            {
                if (task != null)
                {
                    await task; // Wait for any in-flight capture to finish before disposing.
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                cts.Dispose();
            }
        }

        private async Task RunCaptureLoopAsync(IOscilloscope oscilloscope, CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    byte[] bmpBytes = await oscilloscope.GetScreenCaptureAsync();

                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    using (var stream = new MemoryStream(bmpBytes))
                    using (var decoded = new Bitmap(stream))
                    {
                        // Clone into a Bitmap that owns its pixel data, detached from the stream.
                        var bitmap = new Bitmap(decoded);

                        if (IsHandleCreated && !IsDisposed)
                        {
                            BeginInvoke(new Action(() =>
                            {
                                var old = _screenPictureBox.Image;
                                _screenPictureBox.Image = bitmap;
                                old?.Dispose();
                            }));
                        }
                        else
                        {
                            bitmap.Dispose();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Errors caused by shutting down are expected and must not reach the UI.
                    if (token.IsCancellationRequested)
                    {
                        break;
                    }

                    if (IsHandleCreated && !IsDisposed)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            _statusLabel.Text = $"Capture error: {ex.Message}";
                            _statusLabel.ForeColor = Color.DarkOrange;
                        }));
                    }
                }

                await UpdateMeasurementsAsync(oscilloscope, token);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Saves the currently displayed oscilloscope screen capture to disk
        /// in a format chosen via the file dialog's extension (PNG, JPEG or BMP).
        /// </summary>
        private void SaveImageButton_Click()
        {
            if (_screenPictureBox.Image == null)
            {
                AppendLog("No image to save.");
                return;
            }

            using (var dialog = new SaveFileDialog
            {
                Title = "Save Screen Capture",
                Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|Bitmap Image (*.bmp)|*.bmp",
                FileName = $"PulseMirror_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                DefaultExt = "png",
                AddExtension = true
            })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                try
                {
                    ImageFormat format = GetImageFormatFromExtension(dialog.FileName);

                    // Clone before saving: the PictureBox's Image may be swapped
                    // out by the capture loop on a background thread at any time.
                    using (var snapshot = (Image)_screenPictureBox.Image.Clone())
                    {
                        snapshot.Save(dialog.FileName, format);
                    }

                    AppendLog($"Image saved to '{dialog.FileName}'.");
                }
                catch (Exception ex)
                {
                    AppendLog($"ERROR saving image: {ex.Message}");
                    MessageBox.Show(this, ex.Message, "Save Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        private static ImageFormat GetImageFormatFromExtension(string fileName)
        {
            string extension = Path.GetExtension(fileName)?.ToLowerInvariant();
            switch (extension)
            {
                case ".jpg":
                case ".jpeg":
                    return ImageFormat.Jpeg;
                case ".bmp":
                    return ImageFormat.Bmp;
                case ".png":
                default:
                    return ImageFormat.Png;
            }
        }

        private async void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_isClosing)
            {
                return; // Second pass, triggered by Close() below.
            }

            if (_oscilloscope == null && _captureLoopCts == null)
            {
                return;
            }

            // Hold the form open until the capture loop stops and the transport is released.
            e.Cancel = true;
            _isClosing = true;

            try
            {
                if (_oscilloscope != null && _oscilloscope.IsConnected)
                {
                    try
                    {
                        _oscilloscope.ReturnToLocalAsync().Wait(1000);
                    }
                    catch (Exception)
                    {
                        // Ignore: the instrument may already be gone while closing.
                    }
                }

                await DisconnectAsync();
            }
            finally
            {
                Close();
            }
        }

        private static string FormatScale(double scale, string unit)
        {
            if (scale < 1e-6)
                return $"{scale * 1e9:0.##} n{unit}/div";
            if (scale < 1e-3)
                return $"{scale * 1e6:0.##} µ{unit}/div";
            if (scale < 1)
                return $"{scale * 1e3:0.##} m{unit}/div";
            return $"{scale:0.##} {unit}/div";
        }

private static NumericUpDown CreateOffsetNumeric(Point location)
{
    return new NumericUpDown
    {
        Location = location,
        Width = 90,
        DecimalPlaces = 3,
        Increment = 0.01m,
        Minimum = -100m,
        Maximum = 100m,
        Enabled = false
    };
}

private async Task ChannelOffsetNumeric_ValueChangedAsync(int channel, NumericUpDown numeric)
{
    if (_isSyncingControls || _oscilloscope == null || !_oscilloscope.IsConnected)
    {
        return;
    }

    try
    {
        double offset = (double)numeric.Value;
        await _oscilloscope.SetChannelOffsetAsync(channel, offset);

        await Task.Delay(300);
        double confirmed = await _oscilloscope.GetChannelOffsetAsync(channel);
        AppendLog($"Sent CH{channel} offset {offset:0.######} V, instrument reports {confirmed:0.######} V");
    }
    catch (Exception ex)
    {
        AppendLog($"ERROR setting CH{channel} offset: {ex.Message}");
        MessageBox.Show(this, ex.Message, "Communication Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

private static void SetNumericValueClamped(NumericUpDown numeric, double value)
{
    decimal v = (decimal)value;
    numeric.Value = Math.Max(numeric.Minimum, Math.Min(numeric.Maximum, v));
}

private static void ResetMeasurementItem(ListViewItem item)
{
    for (int i = 1; i < item.SubItems.Count; i++)
    {
        item.SubItems[i].Text = "-";
    }
}

//private readonly Button _autoChannel1Button;
//private readonly Button _autoChannel2Button;

//private const double TargetVerticalDivisions = 6;     // of 8 divisions
//private const double TargetPeriods = 3;               // periods shown on screen
//private const double HorizontalDivisions = 12;
//private const int MaxAutoAdjustPasses = 3;

private async Task AutoAdjustChannelAsync(int channel)
{
    if (_oscilloscope == null || !_oscilloscope.IsConnected)
    {
        return;
    }

    try
    {
        AppendLog($"Auto-adjusting CH{channel}...");

        // Reset to default settings before measurement.
        await _oscilloscope.SetTriggerTypeAsync(TriggerType.Edge);
        await _oscilloscope.SetTriggerSourceAsync(TriggerSource.Channel1);

        // Enable the channel and set a default scale and position.
        await _oscilloscope.SetChannelEnabledAsync(channel, true);
        await _oscilloscope.SetChannelScaleAsync(channel, 0.01);
        await _oscilloscope.SetChannelOffsetAsync(channel, 0);

        // Use a faster timebase to find the signal more reliably.
        await _oscilloscope.SetTimebaseAsync(0.00001);

        // Wait briefly for the settings to take effect.
        await Task.Delay(500);

        // Measure the frequency and amplitude (Vpp) to determine the best settings.
        double freq = await _oscilloscope.GetMeasurementAsync(channel, MeasurementQuantity.Frequency);
        double vpp = await _oscilloscope.GetMeasurementAsync(channel, MeasurementQuantity.Vpp);

        AppendLog($"Measured Freq: {freq} Hz, Vpp: {vpp} V");

        // Calculate appropriate V/div and timebase settings based on the measurements.
        double idealVScale = vpp / TargetVerticalDivisions;
        double idealTScale = 1.0 / (freq * TargetPeriods);

        // Apply the new settings, allowing some tolerance.
        await _oscilloscope.SetChannelScaleAsync(channel, idealVScale);
        await _oscilloscope.SetTimebaseAsync(idealTScale);

        // Optionally, adjust the channel offset to center the waveform vertically.
        double offset = -await _oscilloscope.GetMeasurementAsync(channel, MeasurementQuantity.Vmin) + 0.1;
        await _oscilloscope.SetChannelOffsetAsync(channel, offset);

        // Set a more sensible trigger level based on the signal.
        double triggerLevel = await _oscilloscope.GetMeasurementAsync(channel, MeasurementQuantity.Vaverage);
        await _oscilloscope.SetTriggerLevelAsync(triggerLevel);

        AppendLog($"Auto-adjusted CH{channel}:");
        AppendLog($"  V/div: {idealVScale:0.#####} V, Timebase: {idealTScale:0.#####} s/div, Offset: {offset:0.###} V");
    }
    catch (Exception ex)
    {
        AppendLog($"ERROR during auto-adjust: {ex.Message}");
        MessageBox.Show(this, ex.Message, "Adjustment Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
    }
}
