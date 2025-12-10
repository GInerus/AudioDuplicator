using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System.Windows.Threading;

namespace AudioDuplicator
{
    public class AudioDeviceItem
    {
        public MMDevice Device { get; set; }
        public string Name { get; set; }
        public override string ToString() => Name;
    }

    public class TargetControl
    {
        public ComboBox DeviceComboBox;
        public Button UpButton;
        public Button DownButton;
        public Button RemoveButton;
        public Label VolumeLabel;
        public VolumeSampleProvider VolumeProvider;
        public BufferedWaveProvider Buffer;
        public WasapiOut Output;
        public float Volume;
    }

    public partial class MainWindow : Window
    {
        private MMDeviceEnumerator enumerator = new MMDeviceEnumerator();
        private WasapiLoopbackCapture capture;
        private List<TargetControl> targets = new List<TargetControl>();
        private const int maxTargets = 5;
        private const float volumeStep = 1.0f;
        private const float maxVolume = 100f;
        private List<AudioDeviceItem> devices = new List<AudioDeviceItem>();
        private bool isRecording = false;

        // Для зажатия кнопок
        private DispatcherTimer repeatTimer;
        private TargetControl activeTarget;
        private bool increase; // true = +, false = -

        public MainWindow()
        {
            InitializeComponent();
            LoadDevices();
        }

        private void LoadDevices()
        {
            try
            {
                var devs = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
                devices = devs.Select(d => new AudioDeviceItem { Device = d, Name = d.FriendlyName }).ToList();
                SourceComboBox.ItemsSource = devices;
                if (devices.Count > 0) SourceComboBox.SelectedIndex = 0;
            }
            catch (System.Exception ex)
            {
                MessageBox.Show("Ошибка при загрузке устройств: " + ex.Message);
            }
        }

        private List<AudioDeviceItem> GetAvailableTargets()
        {
            var source = SourceComboBox.SelectedItem as AudioDeviceItem;
            var used = targets.Select(t => t.DeviceComboBox.SelectedItem as AudioDeviceItem).Where(d => d != null).ToList();
            return devices.Where(d => d != source && !used.Contains(d)).ToList();
        }

        private void SourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (var t in targets)
            {
                var current = t.DeviceComboBox.SelectedItem as AudioDeviceItem;
                t.DeviceComboBox.ItemsSource = GetAvailableTargets().Append(current).Where(x => x != null).ToList();
            }

            if (isRecording)
            {
                StopButton_Click(null, null);
                StartButton_Click(null, null);
            }
        }

        private void AddTargetButton_Click(object sender, RoutedEventArgs e)
        {
            if (targets.Count >= maxTargets)
                return;

            var available = GetAvailableTargets();
            if (available.Count == 0)
            {
                MessageBox.Show("Нет доступных устройств для дублирования.");
                return;
            }

            var panel = new Grid { Margin = new Thickness(0, 0, 0, 5) };
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // ComboBox
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // +
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // -
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Volume
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); // Remove

            var comboBox = new ComboBox { Margin = new Thickness(0, 0, 5, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            comboBox.ItemsSource = available;
            comboBox.SelectedIndex = 0;
            comboBox.SelectionChanged += TargetComboBox_SelectionChanged;

            var upBtn = new Button { Content = "+", Width = 30, Margin = new Thickness(5, 0, 0, 0) };
            var downBtn = new Button { Content = "-", Width = 30, Margin = new Thickness(5, 0, 0, 0) };
            var volLabel = new Label { Content = "1.0", Width = 40, Margin = new Thickness(5, 0, 0, 0) };
            var removeBtn = new Button { Content = "X", Width = 30, Margin = new Thickness(5, 0, 0, 0) };

            panel.Children.Add(comboBox);
            Grid.SetColumn(comboBox, 0);
            panel.Children.Add(upBtn);
            Grid.SetColumn(upBtn, 1);
            panel.Children.Add(downBtn);
            Grid.SetColumn(downBtn, 2);
            panel.Children.Add(volLabel);
            Grid.SetColumn(volLabel, 3);
            panel.Children.Add(removeBtn);
            Grid.SetColumn(removeBtn, 4);

            TargetsPanel.Children.Add(panel);

            var target = new TargetControl
            {
                DeviceComboBox = comboBox,
                UpButton = upBtn,
                DownButton = downBtn,
                RemoveButton = removeBtn,
                VolumeLabel = volLabel,
                Volume = 1f
            };

            // Настройка зажатия кнопок +/-
            SetupHoldButton(upBtn, target, true);
            SetupHoldButton(downBtn, target, false);

            removeBtn.Click += (s, ev) =>
            {
                TargetsPanel.Children.Remove(panel);
                targets.Remove(target);
                if (isRecording)
                {
                    StopButton_Click(null, null);
                    StartButton_Click(null, null);
                }
            };

            targets.Add(target);

            if (isRecording)
            {
                StopButton_Click(null, null);
                StartButton_Click(null, null);
            }
        }

        private void TargetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            foreach (var t in targets)
            {
                var current = t.DeviceComboBox.SelectedItem as AudioDeviceItem;
                t.DeviceComboBox.ItemsSource = GetAvailableTargets().Append(current).Where(x => x != null).ToList();
            }

            if (isRecording)
            {
                StopButton_Click(null, null);
                StartButton_Click(null, null);
            }
        }

        private void SetupHoldButton(Button button, TargetControl target, bool up)
        {
            button.PreviewMouseLeftButtonDown += (s, e) =>
            {
                activeTarget = target;
                increase = up;
                ChangeVolume(activeTarget, increase);

                repeatTimer = new DispatcherTimer();
                repeatTimer.Interval = TimeSpan.FromMilliseconds(100);
                repeatTimer.Tick += RepeatTimer_Tick;
                repeatTimer.Start();
            };

            button.PreviewMouseLeftButtonUp += (s, e) =>
            {
                repeatTimer?.Stop();
                repeatTimer = null;
                activeTarget = null;
            };

            button.MouseLeave += (s, e) =>
            {
                repeatTimer?.Stop();
                repeatTimer = null;
                activeTarget = null;
            };
        }

        private void RepeatTimer_Tick(object sender, System.EventArgs e)
        {
            if (activeTarget != null)
                ChangeVolume(activeTarget, increase);
        }

        private void ChangeVolume(TargetControl target, bool up)
        {
            if (up)
                target.Volume += volumeStep;
            else
                target.Volume -= volumeStep;

            if (target.Volume > maxVolume) target.Volume = maxVolume;
            if (target.Volume < 0) target.Volume = 0;

            target.VolumeLabel.Content = target.Volume.ToString("0.0");
            if (target.VolumeProvider != null)
                target.VolumeProvider.Volume = target.Volume;
        }

        private void AddTargetAudio(TargetControl target)
        {
            var buffer = new BufferedWaveProvider(capture.WaveFormat)
            {
                DiscardOnBufferOverflow = true
            };
            var volumeProvider = new VolumeSampleProvider(buffer.ToSampleProvider())
            {
                Volume = target.Volume
            };
            var targetDevice = target.DeviceComboBox.SelectedItem as AudioDeviceItem;
            if (targetDevice == null) return; // дополнительная защита

            var output = new WasapiOut(targetDevice.Device, AudioClientShareMode.Shared, false, 10);
            output.Init(volumeProvider);
            output.Play();

            target.Buffer = buffer;
            target.VolumeProvider = volumeProvider;
            target.Output = output;
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            var sourceItem = SourceComboBox.SelectedItem as AudioDeviceItem;
            if (sourceItem == null || targets.Count == 0)
            {
                MessageBox.Show("Выберите источник и хотя бы один приёмник");
                return;
            }

            capture = new WasapiLoopbackCapture(sourceItem.Device);

            foreach (var t in targets)
                AddTargetAudio(t);

            capture.DataAvailable += (s, a) =>
            {
                foreach (var t in targets)
                    t.Buffer?.AddSamples(a.Buffer, 0, a.BytesRecorded);
            };

            capture.StartRecording();
            isRecording = true;

            StartButton.IsEnabled = false;
            StopButton.IsEnabled = true;
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (capture != null)
            {
                capture.StopRecording();
                capture.Dispose();
                capture = null;
            }

            foreach (var t in targets)
            {
                t.Output?.Stop();
                t.Output?.Dispose();
                t.Output = null;
                t.Buffer = null;
                t.VolumeProvider = null;
            }

            isRecording = false;
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }
    }
}
