using PulseMirror.Instruments;
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;
//using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace PulseMirror.Controls
{
    public sealed class TriggerPanel : GroupBox
    {
        private readonly Label _typeLabel;
        private readonly ComboBox _typeComboBox;

        private readonly Label _sourceLabel;
        private readonly ComboBox _sourceComboBox;

        private readonly Label _modeLabel;
        private readonly ComboBox _modeComboBox;

        private readonly Label _levelLabel;
        private readonly NumericUpDown _levelNumeric;

        private readonly Label _slopeLabel;
        private readonly ComboBox _slopeComboBox;

        private readonly Label _pulseWidthLabel;
        private readonly NumericUpDown _pulseWidthNumeric;

        private readonly Label _pulseConditionLabel;
        private readonly ComboBox _pulseConditionComboBox;

        private readonly Label _videoStandardLabel;
        private readonly ComboBox _videoStandardComboBox;

        private readonly Label _videoLineLabel;
        private readonly NumericUpDown _videoLineNumeric;

        private bool _isApplying;

        public event Func<TriggerType, Task> TriggerTypeChangedAsync;
        public event Func<TriggerSource, Task> TriggerSourceChangedAsync;
        public event Func<TriggerMode, Task> TriggerModeChangedAsync;
        public event Func<double, Task> TriggerLevelChangedAsync;
        public event Func<TriggerSlope, Task> TriggerSlopeChangedAsync;
        public event Func<double, Task> PulseWidthChangedAsync;
        public event Func<PulseCondition, Task> PulseConditionChangedAsync;
        public event Func<VideoStandard, Task> VideoStandardChangedAsync;
        public event Func<int, Task> VideoLineChangedAsync;

        public TriggerPanel()
        {
            Text = "Trigger";
            Size = new Size(348, 170);

            _typeLabel = CreateLabel("Type:", 10, 26);
            _typeComboBox = CreateEnumComboBox(95, 22, 230, typeof(TriggerType));
            _typeComboBox.SelectedIndexChanged += async (s, e) =>
            {
                if (_isApplying || _typeComboBox.SelectedItem == null)
                {
                    return;
                }

                await InvokeAsync(TriggerTypeChangedAsync, (TriggerType)_typeComboBox.SelectedItem);
                UpdateDynamicVisibility();
            };

            _sourceLabel = CreateLabel("Source:", 10, 54);
            _sourceComboBox = CreateEnumComboBox(95, 50, 230, typeof(TriggerSource));
            _sourceComboBox.SelectedIndexChanged += async (s, e) =>
            {
                if (_isApplying || _sourceComboBox.SelectedItem == null)
                {
                    return;
                }

                await InvokeAsync(TriggerSourceChangedAsync, (TriggerSource)_sourceComboBox.SelectedItem);
            };

            _modeLabel = CreateLabel("Mode:", 10, 82);
            _modeComboBox = CreateEnumComboBox(95, 78, 230, typeof(TriggerMode));
            _modeComboBox.SelectedIndexChanged += async (s, e) =>
            {
                if (_isApplying || _modeComboBox.SelectedItem == null)
                {
                    return;
                }

                await InvokeAsync(TriggerModeChangedAsync, (TriggerMode)_modeComboBox.SelectedItem);
            };

            _levelLabel = CreateLabel("Level (V):", 10, 110);
            _levelNumeric = new NumericUpDown
            {
                Location = new Point(95, 106),
                Width = 230,
                DecimalPlaces = 3,
                Increment = 0.01m,
                Minimum = -100m,
                Maximum = 100m
            };
            _levelNumeric.ValueChanged += async (s, e) =>
            {
                if (_isApplying)
                {
                    return;
                }

                await InvokeAsync(TriggerLevelChangedAsync, (double)_levelNumeric.Value);
            };

            _slopeLabel = CreateLabel("Slope:", 10, 138);
            _slopeComboBox = CreateEnumComboBox(95, 134, 230, typeof(TriggerSlope));
            _slopeComboBox.SelectedIndexChanged += async (s, e) =>
            {
                if (_isApplying || _slopeComboBox.SelectedItem == null)
                {
                    return;
                }

                await InvokeAsync(TriggerSlopeChangedAsync, (TriggerSlope)_slopeComboBox.SelectedItem);
            };

            _pulseWidthLabel = CreateLabel("Pulse Width (s):", 10, 138);
            _pulseWidthNumeric = new NumericUpDown
            {
                Location = new Point(95, 134),
                Width = 110,
                DecimalPlaces = 6,
                Increment = 0.000001m,
                Minimum = 0m,
                Maximum = 10m
            };
            _pulseWidthNumeric.ValueChanged += async (s, e) =>
            {
                if (_isApplying)
                {
                    return;
                }

                await InvokeAsync(PulseWidthChangedAsync, (double)_pulseWidthNumeric.Value);
            };

            _pulseConditionLabel = CreateLabel("Condition:", 210, 138);
            _pulseConditionComboBox = CreateEnumComboBox(270, 134, 55, typeof(PulseCondition));
            _pulseConditionComboBox.SelectedIndexChanged += async (s, e) =>
            {
                if (_isApplying || _pulseConditionComboBox.SelectedItem == null)
                {
                    return;
                }

                await InvokeAsync(PulseConditionChangedAsync, (PulseCondition)_pulseConditionComboBox.SelectedItem);
            };

            _videoStandardLabel = CreateLabel("Standard:", 10, 138);
            _videoStandardComboBox = CreateEnumComboBox(95, 134, 110, typeof(VideoStandard));
            _videoStandardComboBox.SelectedIndexChanged += async (s, e) =>
            {
                if (_isApplying || _videoStandardComboBox.SelectedItem == null)
                {
                    return;
                }

                await InvokeAsync(VideoStandardChangedAsync, (VideoStandard)_videoStandardComboBox.SelectedItem);
            };

            _videoLineLabel = CreateLabel("Line:", 210, 138);
            _videoLineNumeric = new NumericUpDown
            {
                Location = new Point(250, 134),
                Width = 75,
                DecimalPlaces = 0,
                Increment = 1,
                Minimum = 1,
                Maximum = 625
            };
            _videoLineNumeric.ValueChanged += async (s, e) =>
            {
                if (_isApplying)
                {
                    return;
                }

                await InvokeAsync(VideoLineChangedAsync, Decimal.ToInt32(_videoLineNumeric.Value));
            };

            Controls.Add(_typeLabel);
            Controls.Add(_typeComboBox);
            Controls.Add(_sourceLabel);
            Controls.Add(_sourceComboBox);
            Controls.Add(_modeLabel);
            Controls.Add(_modeComboBox);
            Controls.Add(_levelLabel);
            Controls.Add(_levelNumeric);
            Controls.Add(_slopeLabel);
            Controls.Add(_slopeComboBox);
            Controls.Add(_pulseWidthLabel);
            Controls.Add(_pulseWidthNumeric);
            Controls.Add(_pulseConditionLabel);
            Controls.Add(_pulseConditionComboBox);
            Controls.Add(_videoStandardLabel);
            Controls.Add(_videoStandardComboBox);
            Controls.Add(_videoLineLabel);
            Controls.Add(_videoLineNumeric);

            ApplySettings(new TriggerSettings
            {
                Type = TriggerType.Edge,
                Source = TriggerSource.Channel1,
                Mode = TriggerMode.Auto,
                Level = 0,
                Slope = TriggerSlope.Rising,
                PulseWidth = 0.001,
                PulseCondition = PulseCondition.Equal,
                VideoStandard = VideoStandard.Pal,
                VideoLine = 1
            });
        }

        public void ApplySettings(TriggerSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            _isApplying = true;
            try
            {
                SetComboSelection(_typeComboBox, settings.Type);
                SetComboSelection(_sourceComboBox, settings.Source);
                SetComboSelection(_modeComboBox, settings.Mode);
                SetComboSelection(_slopeComboBox, settings.Slope);
                SetComboSelection(_pulseConditionComboBox, settings.PulseCondition);
                SetComboSelection(_videoStandardComboBox, settings.VideoStandard);

                SetNumeric(_levelNumeric, settings.Level);
                SetNumeric(_pulseWidthNumeric, settings.PulseWidth);
                SetNumeric(_videoLineNumeric, settings.VideoLine);
            }
            finally
            {
                _isApplying = false;
            }

            UpdateDynamicVisibility();
        }

        private static Label CreateLabel(string text, int x, int y)
        {
            return new Label
            {
                Text = text,
                Location = new Point(x, y),
                AutoSize = true
            };
        }

        private static ComboBox CreateEnumComboBox(int x, int y, int width, Type enumType)
        {
            var combo = new ComboBox
            {
                Location = new Point(x, y),
                Width = width,
                DropDownStyle = ComboBoxStyle.DropDownList
            };

            foreach (object value in Enum.GetValues(enumType))
            {
                combo.Items.Add(value);
            }

            if (combo.Items.Count > 0)
            {
                combo.SelectedIndex = 0;
            }

            return combo;
        }

        private static void SetComboSelection(ComboBox comboBox, object value)
        {
            int index = comboBox.Items.IndexOf(value);
            if (index >= 0)
            {
                comboBox.SelectedIndex = index;
            }
        }

        private static void SetNumeric(NumericUpDown numeric, double value)
        {
            decimal d = (decimal)value;
            if (d < numeric.Minimum)
            {
                d = numeric.Minimum;
            }
            else if (d > numeric.Maximum)
            {
                d = numeric.Maximum;
            }

            numeric.Value = d;
        }

        private void UpdateDynamicVisibility()
        {
            if (!(_typeComboBox.SelectedItem is TriggerType))
            {
                return;
            }

            TriggerType type = (TriggerType)_typeComboBox.SelectedItem;

            bool isEdge = type == TriggerType.Edge;
            bool isPulse = type == TriggerType.Pulse;
            bool isVideo = type == TriggerType.Video;

            _slopeLabel.Visible = isEdge;
            _slopeComboBox.Visible = isEdge;

            _pulseWidthLabel.Visible = isPulse;
            _pulseWidthNumeric.Visible = isPulse;
            _pulseConditionLabel.Visible = isPulse;
            _pulseConditionComboBox.Visible = isPulse;

            _videoStandardLabel.Visible = isVideo;
            _videoStandardComboBox.Visible = isVideo;
            _videoLineLabel.Visible = isVideo;
            _videoLineNumeric.Visible = isVideo;
        }

        private static async Task InvokeAsync<T>(Func<T, Task> handler, T value)
        {
            if (handler != null)
            {
                await handler(value);
            }
        }
    }
}