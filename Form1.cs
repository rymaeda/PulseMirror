using System;
using System.Drawing;
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

        private IOscilloscope _oscilloscope;
        private CancellationTokenSource _captureLoopCts;
        private bool _isSyncingControls;
        private TextBox _logTextBox;
        private static readonly double[] VerticalScales =
        {
            0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.2, 0.5, 1, 2, 5, 10
        };

        private static readonly double[] TimebaseScales =
        {
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

        public Form1()
        {
            InitializeComponent();

            Text = "PulseMirror - Oscilloscope Viewer";
            ClientSize = new Size(820, 720);

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
                Location = new Point(110, 47),
                Width = 100,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = false
            };
            foreach (var scale in VerticalScales)
            {
                _verticalScaleComboBox.Items.Add(FormatScale(scale, "V"));
            }
            _verticalScaleComboBox.SelectedIndexChanged += async (s, e) => await VerticalScaleComboBox_SelectedIndexChangedAsync();

            var tScaleLabel = new Label { Text = "Timebase s/div:", Location = new Point(230, 50), AutoSize = true };
            _timebaseComboBox = new ComboBox
            {
                Location = new Point(340, 47),
                Width = 120,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Enabled = false
            };
            foreach (var scale in TimebaseScales)
            {
                _timebaseComboBox.Items.Add(FormatScale(scale, "s"));
            }
            _timebaseComboBox.SelectedIndexChanged += async (s, e) => await TimebaseComboBox_SelectedIndexChangedAsync();

            _screenPictureBox = new PictureBox
            {
                Location = new Point(12, 85),
                Size = new Size(796, 520),
                BorderStyle = BorderStyle.FixedSingle,
                SizeMode = PictureBoxSizeMode.Zoom,
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
            Controls.Add(_screenPictureBox);
            Controls.Add(logLabel);
            Controls.Add(_logTextBox);

            FormClosing += Form1_FormClosing;

            var resourceToolTip = new ToolTip();
            resourceToolTip.SetToolTip(_resourceTextBox,
                "USBTMC direto via WinUSB.\n" +
                "Formato: usbtmc://VIDxPID (ex.: usbtmc://1AB1x0588)\n" +
                "Ou apenas 'usbtmc' para detectar o DS1052E automaticamente.");
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

                _verticalScaleComboBox.Enabled = true;
                _timebaseComboBox.Enabled = true;

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
            StopCaptureLoop();

            if (_oscilloscope is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _oscilloscope = null;
            _verticalScaleComboBox.Enabled = false;
            _timebaseComboBox.Enabled = false;
            _connectButton.Text = "Connect";
            _statusLabel.Text = "Disconnected";
            _statusLabel.ForeColor = Color.Firebrick;
            _screenPictureBox.Image = null;
            AppendLog("Disconnected.");

            await Task.CompletedTask;
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

        private void StartCaptureLoop()
        {
            _captureLoopCts = new CancellationTokenSource();
            _ = RunCaptureLoopAsync(_captureLoopCts.Token);
        }

        private void StopCaptureLoop()
        {
            _captureLoopCts?.Cancel();
            _captureLoopCts?.Dispose();
            _captureLoopCts = null;
        }

        private async Task RunCaptureLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    byte[] bmpBytes = await _oscilloscope.GetScreenCaptureAsync();

                    using (var stream = new MemoryStream(bmpBytes))
                    {
                        var bitmap = new Bitmap(stream);

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
                catch (Exception ex)
                {
                    if (IsHandleCreated && !IsDisposed)
                    {
                        BeginInvoke(new Action(() =>
                        {
                            _statusLabel.Text = $"Capture error: {ex.Message}";
                            _statusLabel.ForeColor = Color.DarkOrange;
                        }));
                    }
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private static string FormatScale(double value, string unit)
        {
            if (value < 0.000001)
            {
                return $"{value * 1_000_000_000:0.##} n{unit}";
            }

            if (value < 0.001)
            {
                return $"{value * 1_000_000:0.##} µ{unit}";
            }

            if (value < 1)
            {
                return $"{value * 1000:0.##} m{unit}";
            }

            return $"{value:0.##} {unit}";
        }

        private async void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            await DisconnectAsync();
        }
    }
}
