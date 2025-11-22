using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Diagnostics;

namespace Hophesmoverlay
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
        [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_SMUDGE_START = 9001; private const int HOTKEY_SMUDGE_STOP = 9005;
        private const int HOTKEY_BPM_TAP = 9002; private const int HOTKEY_BPM_RESET = 9006;
        private const int HOTKEY_MINI = 9003; private const int HOTKEY_INTEL = 9004;
        private const int HOTKEY_BASE_EV = 9010; private const int WM_HOTKEY = 0x0312;

        public List<Ghost> AllGhosts { get; set; }
        private DispatcherTimer _smudgeTimer;
        private int _timerSecondsRemaining = 180;
        private List<DateTime> _tapHistory = new List<DateTime>();
        private double _speedMultiplier = 1.0;
        private int _currentView = 0;

        private readonly List<string> _fastGhosts = new List<string> { "Jinn", "Revenant", "Hantu", "The Twins", "Raiju", "Moroi", "Deogen", "Thaye", "The Mimic" };
        private readonly List<string> _slowGhosts = new List<string> { "Revenant", "Hantu", "Deogen", "Thaye", "The Mimic", "The Twins", "Moroi" };
        private string _currentSpeedCategory = "None";

        public MainWindow()
        {
            InitializeComponent();
            InitializeGhosts();
            GhostsListControl.ItemsSource = AllGhosts;
            GhostsIntelControl.ItemsSource = AllGhosts;
            _smudgeTimer = new DispatcherTimer(DispatcherPriority.Render);
            _smudgeTimer.Interval = TimeSpan.FromSeconds(1);
            _smudgeTimer.Tick += SmudgeTimer_Tick;
        }

        private void BtnDonate_Click(object sender, RoutedEventArgs e) { try { Process.Start(new ProcessStartInfo { FileName = "https://ko-fi.com/hopesan", UseShellExecute = true }); } catch { } }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            IntPtr handle = new WindowInteropHelper(this).Handle;
            HwndSource source = HwndSource.FromHwnd(handle);
            source.AddHook(HwndHook);
            RegisterHotKey(handle, HOTKEY_SMUDGE_START, 0, 0x78); RegisterHotKey(handle, HOTKEY_SMUDGE_STOP, 0, 0x77);
            RegisterHotKey(handle, HOTKEY_BPM_TAP, 0, 0x79); RegisterHotKey(handle, HOTKEY_BPM_RESET, 0, 0x7A);
            RegisterHotKey(handle, HOTKEY_MINI, 0, 0x2D); RegisterHotKey(handle, HOTKEY_INTEL, 0, 0x24);
            for (int i = 1; i <= 7; i++) RegisterHotKey(handle, HOTKEY_BASE_EV + i, 0x0001, (uint)(0x30 + i));
        }

        protected override void OnClosed(EventArgs e)
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            UnregisterHotKey(handle, HOTKEY_SMUDGE_START); UnregisterHotKey(handle, HOTKEY_SMUDGE_STOP);
            UnregisterHotKey(handle, HOTKEY_BPM_TAP); UnregisterHotKey(handle, HOTKEY_BPM_RESET);
            UnregisterHotKey(handle, HOTKEY_MINI); UnregisterHotKey(handle, HOTKEY_INTEL);
            for (int i = 1; i <= 7; i++) UnregisterHotKey(handle, HOTKEY_BASE_EV + i);
            base.OnClosed(e);
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY)
            {
                int id = wParam.ToInt32();
                if (id == HOTKEY_SMUDGE_START) ResetSmudgeTimer();
                else if (id == HOTKEY_SMUDGE_STOP) StopSmudgeTimer();
                else if (id == HOTKEY_BPM_TAP) CalculateBPM();
                else if (id == HOTKEY_BPM_RESET) ResetPace();
                else if (id == HOTKEY_MINI) SetViewMode(1);
                else if (id == HOTKEY_INTEL) SetViewMode(2);
                else if (id > HOTKEY_BASE_EV && id <= HOTKEY_BASE_EV + 7) ToggleEvidenceByIndex(id - HOTKEY_BASE_EV);
            }
            return IntPtr.Zero;
        }

        private void SetViewMode(int mode)
        {
            if (_currentView == mode) mode = 0; _currentView = mode;
            MainJournal.Visibility = Visibility.Collapsed; MiniHud.Visibility = Visibility.Collapsed; IntelHud.Visibility = Visibility.Collapsed;
            if (_currentView == 0) MainJournal.Visibility = Visibility.Visible;
            else if (_currentView == 1) MiniHud.Visibility = Visibility.Visible;
            else if (_currentView == 2) IntelHud.Visibility = Visibility.Visible;
        }

        private void ResetPace()
        {
            _tapHistory.Clear();
            string txt = "-- BPM"; TxtBPM.Text = txt; TxtIntelBPM.Text = txt;
            TxtGhostSpeedGuess.Text = "Select Speed & Tap"; TxtIntelSpeedGuess.Text = "Waiting";
            TxtPacerStatus.Text = "FILTER: NONE"; TxtPacerStatus.Foreground = Brushes.Gray;
            _currentSpeedCategory = "None"; UpdateGhostFiltering();
        }

        private void CalculateBPM()
        {
            DateTime now = DateTime.Now; _tapHistory.RemoveAll(d => (now - d).TotalSeconds > 3); _tapHistory.Add(now);
            if (_tapHistory.Count > 1)
            {
                double totalIntervals = 0; for (int i = 1; i < _tapHistory.Count; i++) totalIntervals += (_tapHistory[i] - _tapHistory[i - 1]).TotalSeconds;
                double avgInterval = totalIntervals / (_tapHistory.Count - 1);
                if (avgInterval > 0)
                {
                    int rawBpm = (int)(60 / avgInterval); int normBpm = (int)(rawBpm / _speedMultiplier);
                    string bpmText = $"{rawBpm} BPM"; TxtBPM.Text = bpmText; TxtIntelBPM.Text = bpmText; InterpretSpeed(normBpm);
                }
            }
            else { TxtBPM.Text = "Tap..."; TxtIntelBPM.Text = "Tap..."; }
        }

        private void InterpretSpeed(int normBpm)
        {
            string guess = ""; Brush c = Brushes.Gray; _currentSpeedCategory = "Normal";
            if (normBpm < 85) { guess = "Rev (Slow)/Deo"; c = Brushes.Cyan; _currentSpeedCategory = "Slow"; }
            else if (normBpm >= 85 && normBpm < 105) { guess = "Slightly Slow"; c = Brushes.LightGray; }
            else if (normBpm >= 105 && normBpm <= 125) { guess = "Normal Speed"; c = Brushes.White; }
            else if (normBpm > 125 && normBpm < 160) { guess = "Fast (Hantu/Moroi)"; c = Brushes.Orange; _currentSpeedCategory = "Fast"; }
            else if (normBpm >= 160) { guess = "RUN (Raiju/Rev)"; c = Brushes.Red; _currentSpeedCategory = "Fast"; }
            TxtGhostSpeedGuess.Text = guess; TxtGhostSpeedGuess.Foreground = c; TxtIntelSpeedGuess.Text = guess; TxtIntelSpeedGuess.Foreground = c;
            TxtPacerStatus.Text = $"FILTER: {_currentSpeedCategory.ToUpper()}"; TxtPacerStatus.Foreground = c; UpdateGhostFiltering();
        }

        private void UpdateGhostFiltering()
        {
            var checkBoxes = FindVisualChildren<CheckBox>(this);
            List<string> selectedEv = new List<string>(); bool fakeOrb = false;
            foreach (var box in checkBoxes) if (box.IsChecked == true) { if (box.Content.ToString().Contains("MIMIC")) fakeOrb = true; else selectedEv.Add(box.Content.ToString()); }
            string summary = string.Join(" + ", selectedEv.Select(x => x.Replace("Ghost ", "").Replace("Level ", "").Replace("Spirit ", "")));
            if (fakeOrb) summary += " (MIMIC ORB)"; if (string.IsNullOrEmpty(summary)) summary = "NONE"; TxtIntelEvidence.Text = summary;

            foreach (var ghost in AllGhosts)
            {
                bool elim = false;
                if (fakeOrb) elim = ghost.Name != "The Mimic";
                else
                {
                    if (selectedEv.Contains("Ghost Orb") && ghost.Name == "The Mimic") elim = true;
                    foreach (var ev in selectedEv) if (!ghost.Evidences.Contains(ev)) { elim = true; break; }
                }
                if (!elim)
                {
                    if (_currentSpeedCategory == "Fast") { if (!_fastGhosts.Contains(ghost.Name)) elim = true; }
                    else if (_currentSpeedCategory == "Slow") { if (!_slowGhosts.Contains(ghost.Name)) elim = true; }
                    else if (_currentSpeedCategory == "Normal") { if (ghost.Name == "Revenant") elim = true; }
                }
                ghost.IsEliminated = elim;
            }
        }

        private void BtnStopSmudge_Click(object sender, RoutedEventArgs e) => StopSmudgeTimer();
        private void BtnResetSmudge_Click(object sender, RoutedEventArgs e) => ResetSmudgeTimer();
        private void BtnResetPace_Click(object sender, RoutedEventArgs e) => ResetPace();
        private void CmbSpeed_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbSpeed.SelectedIndex == 0) _speedMultiplier = 0.5; else if (CmbSpeed.SelectedIndex == 1) _speedMultiplier = 0.75; else if (CmbSpeed.SelectedIndex == 2) _speedMultiplier = 1.0; else if (CmbSpeed.SelectedIndex == 3) _speedMultiplier = 1.25; else if (CmbSpeed.SelectedIndex == 4) _speedMultiplier = 1.5;
        }
        private void StartSmudgeTimer() { _timerSecondsRemaining = 180; ProgressTimer.Value = 180; _smudgeTimer.Start(); UpdateTimerDisplay(); }
        private void ResetSmudgeTimer() { StartSmudgeTimer(); }
        private void StopSmudgeTimer()
        {
            _smudgeTimer.Stop(); TxtTimer.Text = "READY"; TxtMiniTimer.Text = "READY"; TxtIntelTimer.Text = "READY"; TxtTimerStatus.Text = "WAITING..."; TxtMiniStatus.Text = "WAITING..."; TxtIntelStatus.Text = "WAITING...";
            TxtTimer.Foreground = Brushes.White; TxtMiniTimer.Foreground = Brushes.White; TxtIntelTimer.Foreground = Brushes.White;
            TxtTimerStatus.Foreground = Brushes.Gray; TxtMiniStatus.Foreground = Brushes.Gray; TxtIntelStatus.Foreground = Brushes.Gray; ProgressTimer.Value = 180;
        }
        private void SmudgeTimer_Tick(object sender, EventArgs e)
        {
            _timerSecondsRemaining--; ProgressTimer.Value = _timerSecondsRemaining; UpdateTimerDisplay();
            if (_timerSecondsRemaining == 120) PlayAudioCue("demon"); if (_timerSecondsRemaining == 90) PlayAudioCue("normal"); if (_timerSecondsRemaining <= 0) { _smudgeTimer.Stop(); UpdateTimerDisplay(); PlayAudioCue("spirit"); }
        }
        private void UpdateTimerDisplay()
        {
            if (!_smudgeTimer.IsEnabled && TxtTimer.Text == "READY") return;
            string timeText, statusText; Brush colorBrush, statusBrush;
            if (_timerSecondsRemaining <= 0) { timeText = "SPIRIT!"; statusText = "HUNT IMMINENT"; colorBrush = Brushes.Red; statusBrush = Brushes.Red; }
            else
            {
                TimeSpan t = TimeSpan.FromSeconds(_timerSecondsRemaining); timeText = string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds);
                if (_timerSecondsRemaining > 120) { colorBrush = Brushes.White; statusText = "SAFE (ALL)"; statusBrush = Brushes.LightGreen; }
                else if (_timerSecondsRemaining <= 120 && _timerSecondsRemaining > 90) { colorBrush = (Brush)new BrushConverter().ConvertFrom("#FF6666"); statusText = "DEMON UNSAFE"; statusBrush = Brushes.Orange; }
                else { colorBrush = Brushes.Red; statusText = "NORMAL UNSAFE"; statusBrush = Brushes.Red; }
            }
            TxtTimer.Text = timeText; TxtTimer.Foreground = colorBrush; TxtTimerStatus.Text = statusText; TxtTimerStatus.Foreground = statusBrush;
            TxtMiniTimer.Text = timeText; TxtMiniTimer.Foreground = colorBrush; TxtMiniStatus.Text = statusText; TxtMiniStatus.Foreground = statusBrush;
            TxtIntelTimer.Text = timeText; TxtIntelTimer.Foreground = colorBrush; TxtIntelStatus.Text = statusText; TxtIntelStatus.Foreground = statusBrush;
        }
        private void PlayAudioCue(string type) { Task.Run(() => { try { if (type == "demon") { Console.Beep(200, 400); Thread.Sleep(100); Console.Beep(200, 400); } else if (type == "normal") { Console.Beep(500, 200); Thread.Sleep(100); Console.Beep(500, 200); Thread.Sleep(100); Console.Beep(500, 200); } else if (type == "spirit") { Console.Beep(1000, 300); Thread.Sleep(50); Console.Beep(1000, 300); Thread.Sleep(50); Console.Beep(1000, 800); } } catch { } }); }
        private void ToggleEvidenceByIndex(int index) { CheckBox t = null; switch (index) { case 1: t = ChkEv1; break; case 2: t = ChkEv2; break; case 3: t = ChkEv3; break; case 4: t = ChkEv4; break; case 5: t = ChkEv5; break; case 6: t = ChkEv6; break; case 7: t = ChkEv7; break; } if (t != null) t.IsChecked = !t.IsChecked; }
        private void Evidence_Changed(object sender, RoutedEventArgs e) => UpdateGhostFiltering();
        private void Window_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); if (e.ChangedButton == MouseButton.Right) Application.Current.Shutdown(); }
        public static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject { if (depObj != null) for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++) { DependencyObject child = VisualTreeHelper.GetChild(depObj, i); if (child != null && child is T) yield return (T)child; foreach (T childOfChild in FindVisualChildren<T>(child)) yield return childOfChild; } }

        private void InitializeGhosts()
        {
            AllGhosts = new List<Ghost> {
                new Ghost("Spirit", "👻", "EMF Level 5", "Spirit Box", "Ghost Writing") { Tell = "Smudge stops hunt 180s" },
                new Ghost("Wraith", "👣", "EMF Level 5", "Spirit Box", "D.O.T.S.") { Tell = "No salt steps" },
                new Ghost("Phantom", "📷", "Spirit Box", "Fingerprints", "D.O.T.S.") { Tell = "Blinks slow / Disappears in photo" },
                new Ghost("Poltergeist", "💥", "Spirit Box", "Fingerprints", "Ghost Writing") { Tell = "Throw explosion / Pile throw" },
                new Ghost("Banshee", "😱", "Fingerprints", "Ghost Orb", "D.O.T.S.") { Tell = "Paramic Scream / Roams to target" },
                new Ghost("Jinn", "⚡", "EMF Level 5", "Fingerprints", "Freezing") { Tell = "Fast w/ power" },
                new Ghost("Mare", "💡", "Spirit Box", "Ghost Orb", "Ghost Writing") { Tell = "Hates light / Instant light switch" },
                new Ghost("Revenant", "😈", "Ghost Orb", "Ghost Writing", "Freezing") { Tell = "Slow hidden / Fast visible" },
                new Ghost("Shade", "🌑", "EMF Level 5", "Ghost Writing", "Freezing") { Tell = "Shy / No hunt if near" },
                new Ghost("Demon", "⸸", "Fingerprints", "Ghost Writing", "Freezing") { Tell = "Hunt @ 70% / 60s CD" },
                new Ghost("Yurei", "🚪", "Ghost Orb", "Freezing", "D.O.T.S.") { Tell = "Smudge traps in room" },
                new Ghost("Oni", "👹", "EMF Level 5", "Freezing", "D.O.T.S.") { Tell = "Visible longer / Can't airball" },
                new Ghost("Yokai", "🔇", "Spirit Box", "Ghost Orb", "D.O.T.S.") { Tell = "Deaf in hunt / Voice triggers" },
                new Ghost("Hantu", "❄", "Fingerprints", "Ghost Orb", "Freezing") { Tell = "Fast in cold / Breath" },
                new Ghost("Goryo", "👀", "EMF Level 5", "Fingerprints", "D.O.T.S.") { Tell = "DOTS on Cam Only" },
                new Ghost("Myling", "🤫", "EMF Level 5", "Fingerprints", "Ghost Writing") { Tell = "Silent footsteps" },
                new Ghost("Onryo", "🔥", "Spirit Box", "Ghost Orb", "Freezing") { Tell = "Fire prevents hunt" },
                new Ghost("The Twins", "♊", "EMF Level 5", "Spirit Box", "Freezing") { Tell = "Split speeds / Decoys" },
                new Ghost("Raiju", "🔋", "EMF Level 5", "Ghost Orb", "D.O.T.S.") { Tell = "Fast near electronics" },
                new Ghost("Obake", "🖐", "EMF Level 5", "Fingerprints", "Ghost Orb") { Tell = "Shapeshift / 6-Finger" },
                new Ghost("The Mimic", "🎭", "Spirit Box", "Fingerprints", "Freezing") { Tell = "Fake Orbs / Copies behaviors" },
                new Ghost("Moroi", "☠", "Spirit Box", "Ghost Writing", "Freezing") { Tell = "Curse / Low sanity speed" },
                new Ghost("Deogen", "👁", "Spirit Box", "Ghost Writing", "D.O.T.S.") { Tell = "Always finds you / Slow close" },
                new Ghost("Thaye", "⏳", "Ghost Orb", "Ghost Writing", "D.O.T.S.") { Tell = "Ages / Fast start -> Slow end" },
            };
        }
    }

    public class Ghost : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Symbol { get; set; }
        public List<string> Evidences { get; set; }
        public string Tell { get; set; }
        private bool _isEliminated;
        public bool IsEliminated { get => _isEliminated; set { _isEliminated = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsEliminated")); } }

        public Brush PrimaryColorBrush { get { return (Brush)new BrushConverter().ConvertFrom(PrimaryColor); } }
        public string PrimaryColor
        {
            get
            {
                if (Evidences.Contains("Freezing")) return "#00CED1";
                if (Evidences.Contains("EMF Level 5")) return "#DC143C";
                if (Evidences.Contains("D.O.T.S.")) return "#39FF14";
                if (Evidences.Contains("Fingerprints")) return "#8A2BE2";
                if (Evidences.Contains("Spirit Box")) return "#FF4500";
                if (Evidences.Contains("Ghost Writing")) return "#FFD700";
                return "#E0FFFF";
            }
        }

        public Brush EvidenceGradient
        {
            get
            {
                var stops = new GradientStopCollection();
                Color GetColor(string ev)
                {
                    if (ev.Contains("EMF")) return Color.FromRgb(220, 20, 60);
                    if (ev.Contains("Fingerprints")) return Color.FromRgb(138, 43, 226);
                    if (ev.Contains("Freezing")) return Color.FromRgb(0, 206, 209);
                    if (ev.Contains("Spirit Box")) return Color.FromRgb(255, 69, 0);
                    if (ev.Contains("Ghost Orb")) return Color.FromRgb(224, 255, 255);
                    if (ev.Contains("Ghost Writing")) return Color.FromRgb(255, 215, 0);
                    if (ev.Contains("D.O.T.S.")) return Color.FromRgb(57, 255, 20);
                    return Colors.White;
                }
                if (Evidences.Count >= 1) stops.Add(new GradientStop(GetColor(Evidences[0]), 0.0));
                if (Evidences.Count >= 2) stops.Add(new GradientStop(GetColor(Evidences[1]), 0.5));
                if (Evidences.Count >= 3) stops.Add(new GradientStop(GetColor(Evidences[2]), 1.0));
                return new LinearGradientBrush(stops, new Point(0, 0), new Point(1, 0)) { Opacity = 0.9 };
            }
        }

        public Ghost(string n, string s, params string[] e) { Name = n; Symbol = s; Evidences = e.ToList(); }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class BooleanToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object v, Type t, object p, System.Globalization.CultureInfo c) => (bool)v ? Visibility.Visible : Visibility.Collapsed;
        public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotImplementedException();
    }
    public class InverseBooleanToVisibilityConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object v, Type t, object p, System.Globalization.CultureInfo c) => (bool)v ? Visibility.Collapsed : Visibility.Visible;
        public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotImplementedException();
    }
}