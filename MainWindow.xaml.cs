using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace Hophesmoverlay
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

        // --- GLOBAL VARIABLES ---
        private AppSettings _config;
        private LangFile _currentLangData;

        // Smart Reset Variables
        private List<bool?> _undoState = new List<bool?>();
        private DateTime _lastResetTime = DateTime.MinValue;

        // Speed Variables
        private double _speedMultiplier = 1.0;
        private double _lastCalculatedSpeed = 0.0;
        private List<DateTime> _tapHistory = new List<DateTime>();
        private string _currentSpeedCategory = "None";

        // View State
        private int _currentView = 0;

        // Key Constants
        private const int VK_MENU = 0x12; // Alt Key
        private const int VK_PAUSE = 0x13;
        private const int VK_HOME = 0x24;

        private CancellationTokenSource _cancellationTokenSource;
        private Dictionary<int, bool> _keyStateTracker = new Dictionary<int, bool>();

        // Lists
        public List<Ghost> AllGhosts { get; set; } = new List<Ghost>();
        private List<CheckBox> _evidenceCheckBoxes = new List<CheckBox>();

        // These lists now use IDs (Strings) to match the JSON "ID" field
        private readonly List<string> _fastGhosts = new List<string> { "Jinn", "Revenant", "Hantu", "The Twins", "Raiju", "Moroi", "Deogen", "Thaye", "The Mimic", "Dayan", "Obambo" };
        private readonly List<string> _slowGhosts = new List<string> { "Revenant", "Hantu", "Deogen", "Thaye", "The Mimic", "The Twins", "Moroi", "Dayan", "Obambo" };

        // Timers
        private DispatcherTimer _smudgeTimer;
        private int _timerSecondsRemaining = 180;
        private DispatcherTimer _huntTimer;
        private int _huntTimerSeconds = 25;

        // NEW: Hunt Duration Timer
        private DispatcherTimer _huntDurationTimer;
        private int _huntDurationSeconds = 0;
        private int _huntDurationLimit = 30; 

        public MainWindow()
        {
            InitializeComponent();
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;
            // Trigger this once to set the default text "-- / 30s"
            CmbMapSize.SelectionChanged += (s, e) => { ToggleHuntDuration(); ToggleHuntDuration(); };
            CmbMapSize.SelectionChanged += (s, e) =>
            {
                if (CmbMapSize.SelectedIndex == 0) _huntDurationLimit = 30;
                else if (CmbMapSize.SelectedIndex == 1) _huntDurationLimit = 50;
                else _huntDurationLimit = 60;

                if (!_huntDurationTimer.IsEnabled) UpdateHuntDurationDisplay();
            };

            // 1. LOAD CONFIG
            _config = AppSettings.Load();
            this.Opacity = _config.Opacity;

            // 2. SETUP CHECKBOXES
            _evidenceCheckBoxes.Add(ChkEv1); _evidenceCheckBoxes.Add(ChkEv2);
            _evidenceCheckBoxes.Add(ChkEv3); _evidenceCheckBoxes.Add(ChkEv4);
            _evidenceCheckBoxes.Add(ChkEv5); _evidenceCheckBoxes.Add(ChkEv6);
            _evidenceCheckBoxes.Add(ChkEv7);

            // 3. LOAD LANGUAGE (From JSON)
            LoadLanguage(_config.Language);

            // 4. SETUP TIMERS
            _smudgeTimer = new DispatcherTimer(DispatcherPriority.Render);
            _smudgeTimer.Interval = TimeSpan.FromSeconds(1);
            _smudgeTimer.Tick += SmudgeTimer_Tick;

            _huntTimer = new DispatcherTimer(DispatcherPriority.Render);
            _huntTimer.Interval = TimeSpan.FromSeconds(1);
            _huntTimer.Tick += HuntTimer_Tick;

            // Setup Hunt Duration Timer
            _huntDurationTimer = new DispatcherTimer(DispatcherPriority.Render);
            _huntDurationTimer.Interval = TimeSpan.FromSeconds(1);
            _huntDurationTimer.Tick += HuntDuration_Tick;

            // 5. START INPUT LOOP
            _cancellationTokenSource = new CancellationTokenSource();
            Task.Factory.StartNew(() => InputLoop(_cancellationTokenSource.Token), TaskCreationOptions.LongRunning);
        }

        private void InputLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // Use Keys from Config
                CheckKey(_config.Keys.HuntStop, () => StopHuntTimer());
                CheckKey(_config.Keys.HuntStart, () => ResetHuntTimer());
                CheckKey(_config.Keys.SmudgeStop, () => StopSmudgeTimer());
                CheckKey(_config.Keys.SmudgeStart, () => ResetSmudgeTimer());
                CheckKey(_config.Keys.SpeedTap, () => CalculateBPM());
                CheckKey(_config.Keys.SpeedReset, () => ResetPace());
                CheckKey(_config.Keys.HuntDuration, () => ToggleHuntDuration());

                CheckKey(VK_PAUSE, () => SetViewMode(1));
                CheckKey(VK_HOME, () => SetViewMode(2));

                bool isAltDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

                // ALT + BACKSPACE (Smart Reset)
                if (isAltDown)
                {
                    CheckKey(_config.Keys.Reset, () => SmartReset());

                    // Alt + Numbers
                    for (int i = 1; i <= 7; i++)
                    {
                        int vKey = 0x30 + i;
                        int idx = i;
                        CheckKey(vKey, () => ToggleEvidenceByIndex(idx));
                    }
                }
                Thread.Sleep(10);
            }
        }

        // --- NEW: HUNT DURATION LOGIC ---
        private void ToggleHuntDuration()
        {
            if (_huntDurationTimer.IsEnabled)
            {
                // Stop
                _huntDurationTimer.Stop();
                // Reset text to default state showing the limit
                UpdateHuntDurationDisplay();
                TxtActiveHunt.Foreground = (Brush)new BrushConverter().ConvertFrom("#FF4444");
            }
            else
            {
                // Start
                // 1. Determine Limit based on ComboBox
                if (CmbMapSize.SelectedIndex == 0) _huntDurationLimit = 30;      // Small
                else if (CmbMapSize.SelectedIndex == 1) _huntDurationLimit = 50; // Medium
                else if (CmbMapSize.SelectedIndex == 2) _huntDurationLimit = 60; // Large

                _huntDurationSeconds = 0;
                _huntDurationTimer.Start();
                UpdateHuntDurationDisplay();
            }
        }

        private void HuntDuration_Tick(object sender, EventArgs e)
        {
            _huntDurationSeconds++;
            UpdateHuntDurationDisplay();
        }

        private void UpdateHuntDurationDisplay()
        {
            // Format: "05 / 30s"
            TxtActiveHunt.Text = $"{_huntDurationSeconds:D2} / {_huntDurationLimit}s";

            // Visual Logic
            if (_huntDurationTimer.IsEnabled)
            {
                if (_huntDurationSeconds < _huntDurationLimit)
                {
                    TxtActiveHunt.Foreground = Brushes.Red; // Normal Hunt
                }
                else
                {
                    // Overtime (Cursed Hunt?)
                    TxtActiveHunt.Foreground = Brushes.Magenta;
                }
            }
            else
            {
                // Idle state
                TxtActiveHunt.Text = $"-- / {_huntDurationLimit}s";
                TxtActiveHunt.Foreground = (Brush)new BrushConverter().ConvertFrom("#FF8888");
            }
        }

        // --- SMART RESET (UNDO) ---
        private void SmartReset()
        {
            TimeSpan timeSinceLast = DateTime.Now - _lastResetTime;

            if (timeSinceLast.TotalSeconds < 3)
            {
                // RESTORE MODE
                if (_undoState != null && _undoState.Count == _evidenceCheckBoxes.Count)
                {
                    for (int i = 0; i < _evidenceCheckBoxes.Count; i++)
                    {
                        _evidenceCheckBoxes[i].IsChecked = _undoState[i];
                    }
                    UpdateGhostFiltering();
                    _lastResetTime = DateTime.MinValue;

                    PlayAudioCue("normal");
                    TxtSystemMessage.Text = "RESTORED";
                    TxtSystemMessage.Foreground = Brushes.LightGreen;

                    Task.Delay(2000).ContinueWith(_ => Dispatcher.Invoke(() => TxtSystemMessage.Text = ""));
                }
            }
            else
            {
                // RESET MODE
                _undoState = new List<bool?>();
                foreach (var box in _evidenceCheckBoxes)
                {
                    _undoState.Add(box.IsChecked);
                }

                foreach (var box in _evidenceCheckBoxes)
                {
                    box.IsChecked = false;
                }

                UpdateGhostFiltering();
                _lastResetTime = DateTime.Now;

                PlayAudioCue("demon");
                TxtSystemMessage.Text = "UNDO? (<3s)";
                TxtSystemMessage.Foreground = Brushes.Orange;

                Task.Delay(3000).ContinueWith(_ => Dispatcher.Invoke(() =>
                {
                    if (TxtSystemMessage.Text.Contains("UNDO")) TxtSystemMessage.Text = "";
                }));
            }
        }

        // --- LANGUAGE LOADER ---
        private void LoadLanguage(string langCode)
        {
            string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Languages", $"{langCode}.json");

            if (!File.Exists(path))
            {
                MessageBox.Show($"Language file not found: {path}");
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                _currentLangData = JsonSerializer.Deserialize<LangFile>(json);

                // Apply UI Strings
                var ui = _currentLangData.UI;
                LblControls.Text = ui.GetValueOrDefault("Controls", "Missing String");
                LblEvidence.Text = ui.GetValueOrDefault("EvidenceHeader", "Evidence");
                LblSmudge.Text = ui.GetValueOrDefault("SmudgeHeader", "Smudge");
                LblHunt.Text = ui.GetValueOrDefault("HuntHeader", "Hunt");
                LblSpeed.Text = ui.GetValueOrDefault("SpeedHeader", "Speed");

                ChkEv1.Content = ui.GetValueOrDefault("Ev1", "EMF 5");
                ChkEv2.Content = ui.GetValueOrDefault("Ev2", "DOTS");
                ChkEv3.Content = ui.GetValueOrDefault("Ev3", "UV");
                ChkEv4.Content = ui.GetValueOrDefault("Ev4", "Freezing");
                ChkEv5.Content = ui.GetValueOrDefault("Ev5", "Orbs");
                ChkEv6.Content = ui.GetValueOrDefault("Ev6", "Writing");
                ChkEv7.Content = ui.GetValueOrDefault("Ev7", "Spirit Box");

                // Build Ghost List
                AllGhosts.Clear();
                foreach (var g in _currentLangData.Ghosts)
                {
                    // Map the Data from JSON (GhostData) to Logic Class (Ghost)
                    AllGhosts.Add(new Ghost(g.Name, g.Symbol, g.Evidences.ToArray())
                    {
                        ID = g.ID,
                        Tell = g.Tell,
                        HuntThreshold = g.HuntThreshold,
                        MinSpeed = g.MinSpeed,
                        MaxSpeed = g.MaxSpeed,
                        Guaranteed = g.Guaranteed, // Mapped Guaranteed Evidence
                        SpeedInfo = (g.MinSpeed == g.MaxSpeed) ? $"{g.MinSpeed:0.0} m/s" : $"{g.MinSpeed:0.0} - {g.MaxSpeed:0.0} m/s"
                    });
                }

                GhostsListControl.ItemsSource = null;
                GhostsListControl.ItemsSource = AllGhosts;
                GhostsIntelControl.ItemsSource = null;
                GhostsIntelControl.ItemsSource = AllGhosts;

                UpdateGhostFiltering();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading language: " + ex.Message);
            }
        }

        // --- SPEED CALCULATOR ---
        private void CalculateBPM()
        {
            DateTime now = DateTime.Now;
            _tapHistory.RemoveAll(d => (now - d).TotalSeconds > 3);
            _tapHistory.Add(now);

            if (_tapHistory.Count > 1)
            {
                double totalIntervals = 0;
                for (int i = 1; i < _tapHistory.Count; i++)
                    totalIntervals += (_tapHistory[i] - _tapHistory[i - 1]).TotalSeconds;

                double avgInterval = totalIntervals / (_tapHistory.Count - 1);

                if (avgInterval > 0)
                {
                    int rawBpm = (int)(60 / avgInterval);
                    double ms = (rawBpm * 0.0148) / _speedMultiplier;
                    string speedText = $"{ms:F1} m/s";
                    TxtBPM.Text = speedText;
                    TxtIntelBPM.Text = speedText;
                    InterpretSpeed(ms);
                }
            }
            else { TxtBPM.Text = "Tap..."; TxtIntelBPM.Text = "Tap..."; }
        }

        private void InterpretSpeed(double ms)
        {
            _lastCalculatedSpeed = ms;
            string key = "";
            Brush c = Brushes.Gray;

            // Default to Normal, but the logic below will almost always overwrite it
            _currentSpeedCategory = "Normal";

            // --- GAP-FREE LOGIC ---
            // We check from Lowest to Highest. The first match wins.

            if (ms < 0.65)      // 0.0 to 0.65
            {
                key = "DeogenStill"; c = Brushes.Cyan; _currentSpeedCategory = "Slow";
            }
            else if (ms < 1.25) // 0.65 to 1.25 (Covers Rev Passive 1.0)
            {
                key = "RevPassive"; c = Brushes.Cyan; _currentSpeedCategory = "Slow";
            }
            else if (ms < 1.58) // 1.25 to 1.58 (Covers Twin Slow 1.5)
            {
                key = "Slow"; c = Brushes.LightGray; _currentSpeedCategory = "Slow";
            }
            else if (ms < 1.85) // 1.58 to 1.85 (Covers Normal 1.7)
            {
                key = "Normal"; c = Brushes.White; _currentSpeedCategory = "Normal";
            }
            else if (ms < 2.15) // 1.85 to 2.15 (Covers Twin Fast 1.9)
            {
                key = "Fast"; c = Brushes.Orange; _currentSpeedCategory = "Fast";
            }
            else if (ms < 2.65) // 2.15 to 2.65 (Covers Jinn/Raiju 2.5) -> FIXES 2.2!
            {
                key = "VeryFast"; c = Brushes.Red; _currentSpeedCategory = "Fast";
            }
            else if (ms < 3.25) // 2.65 to 3.25 (Covers Rev Chase 3.0)
            {
                key = "SuperFast"; c = Brushes.Red; _currentSpeedCategory = "Fast";
            }
            else if (ms < 4.0)  // 3.25 to 4.0 (Moroi Max Speed)
            {
                key = "MaxLOS"; c = Brushes.Magenta; _currentSpeedCategory = "Fast";
            }
            else // > 4.0
            {
                key = "Impossible"; c = Brushes.Gray; _currentSpeedCategory = "None";
            }

            // Get Translation safely
            string guessText = key;
            if (_currentLangData != null &&
                _currentLangData.SpeedSys != null &&
                _currentLangData.SpeedSys.ContainsKey(key))
            {
                guessText = _currentLangData.SpeedSys[key];
            }

            // Update UI
            TxtGhostSpeedGuess.Text = guessText;
            TxtGhostSpeedGuess.Foreground = c;

            // Update Intel Window
            if (TxtIntelSpeedGuess != null)
            {
                TxtIntelSpeedGuess.Text = guessText;
                TxtIntelSpeedGuess.Foreground = c;
            }

            // Update Status Filter Text
            TxtPacerStatus.Text = $"FILTER: {_currentSpeedCategory.ToUpper()}";
            TxtPacerStatus.Foreground = c;

            // Trigger the Filtering
            UpdateGhostFiltering();
        }

        private void ResetPace()
        {
            _tapHistory.Clear();
            _lastCalculatedSpeed = 0.0;
            string txt = "-- m/s"; TxtBPM.Text = txt; TxtIntelBPM.Text = txt;
            TxtPacerStatus.Text = "FILTER: NONE"; TxtPacerStatus.Foreground = Brushes.Gray;
            _currentSpeedCategory = "None"; UpdateGhostFiltering();
        }

        private void UpdateGhostFiltering()
        {
            List<string> foundEv = new List<string>();
            List<string> ruledOutEv = new List<string>();

            foreach (var box in _evidenceCheckBoxes)
            {
                if (box.IsChecked == true) foundEv.Add(box.Content.ToString());
                if (box.IsChecked == null) ruledOutEv.Add(box.Content.ToString());
            }

            if (foundEv.Count > 0)
            {
                string foundStr = string.Join(" + ", foundEv);
                TxtIntelFound.Text = foundStr; TxtIntelFound.Visibility = Visibility.Visible;
            }
            else { TxtIntelFound.Visibility = Visibility.Collapsed; }

            foreach (var ghost in AllGhosts)
            {
                bool elim = false;

                // 1. Evidence Check
                foreach (var ev in foundEv)
                {
                    if (ghost.Name == "The Mimic" && (ev.Contains("Orb") || ev.Contains("Orbe") || ev.Contains("靈球"))) continue;
                    if (!ghost.Evidences.Contains(ev)) { elim = true; break; }
                }

                if (!elim)
                {
                    foreach (var ev in ruledOutEv)
                    {
                        if (ghost.Evidences.Contains(ev)) { elim = true; break; }
                        if (ghost.Name == "The Mimic" && (ev.Contains("Orb") || ev.Contains("Orbe"))) { elim = true; break; }
                    }
                }

                // 2. Speed Check (Advanced Tolerance)
                if (!elim)
                {
                    if (_currentSpeedCategory == "Fast")
                    {
                        if (!_fastGhosts.Contains(ghost.ID)) elim = true;
                    }
                    else if (_currentSpeedCategory == "Slow")
                    {
                        if (!_slowGhosts.Contains(ghost.ID)) elim = true;
                    }
                    else if (_currentSpeedCategory == "Normal")
                    {
                        if (ghost.ID == "Revenant") elim = true;
                    }

                    // Tolerance Check: If speed calculated is wildly outside the ghost's capabilities
                    if (!elim && _lastCalculatedSpeed > 0 && _currentSpeedCategory != "None")
                    {
                        double margin = 0.15;
                        if (_lastCalculatedSpeed < (ghost.MinSpeed - margin) || _lastCalculatedSpeed > (ghost.MaxSpeed + margin))
                        {
                            elim = true;
                        }
                    }
                }
                ghost.IsEliminated = elim;
            }
        }

        // --- MISC LOGIC ---
        private void ToggleEvidenceByIndex(int index)
        {
            if (index < 1 || index > _evidenceCheckBoxes.Count) return;
            var t = _evidenceCheckBoxes[index - 1];
            if (t.IsChecked == false) t.IsChecked = true;
            else if (t.IsChecked == true) t.IsChecked = null;
            else t.IsChecked = false;
            UpdateGhostFiltering();
        }

        private void Evidence_Changed(object sender, RoutedEventArgs e) => UpdateGhostFiltering();
        private void BtnStopSmudge_Click(object sender, RoutedEventArgs e) => StopSmudgeTimer();
        private void BtnResetSmudge_Click(object sender, RoutedEventArgs e) => ResetSmudgeTimer();
        private void BtnResetPace_Click(object sender, RoutedEventArgs e) => ResetPace();
        private void CmbSpeed_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbSpeed.SelectedIndex == 0) _speedMultiplier = 0.5; else if (CmbSpeed.SelectedIndex == 1) _speedMultiplier = 0.75; else if (CmbSpeed.SelectedIndex == 2) _speedMultiplier = 1.0; else if (CmbSpeed.SelectedIndex == 3) _speedMultiplier = 1.25; else if (CmbSpeed.SelectedIndex == 4) _speedMultiplier = 1.5;
        }

        // --- TIMER LOGIC ---
        private void StartSmudgeTimer() { _timerSecondsRemaining = 180; ProgressTimer.Value = 180; _smudgeTimer.Start(); UpdateTimerDisplay(); }
        private void ResetSmudgeTimer() { StartSmudgeTimer(); }
        private void StopSmudgeTimer()
        {
            _smudgeTimer.Stop();
            string ready = _currentLangData?.UI.GetValueOrDefault("Ready", "READY") ?? "READY";
            string wait = _currentLangData?.UI.GetValueOrDefault("Waiting", "WAITING") ?? "WAITING";
            TxtTimer.Text = ready; TxtMiniTimer.Text = ready; TxtIntelTimer.Text = ready;
            TxtTimerStatus.Text = wait; TxtMiniStatus.Text = wait; TxtIntelStatus.Text = wait;
            TxtTimer.Foreground = Brushes.White; TxtTimerStatus.Foreground = Brushes.Gray;
            ProgressTimer.Value = 180;
        }

        private void SmudgeTimer_Tick(object sender, EventArgs e)
        {
            _timerSecondsRemaining--; ProgressTimer.Value = _timerSecondsRemaining; UpdateTimerDisplay();
            if (_timerSecondsRemaining == 120) PlayAudioCue("demon"); if (_timerSecondsRemaining == 90) PlayAudioCue("normal"); if (_timerSecondsRemaining <= 0) { _smudgeTimer.Stop(); UpdateTimerDisplay(); PlayAudioCue("spirit"); }
        }

        private void UpdateTimerDisplay()
        {
            if (!_smudgeTimer.IsEnabled && (TxtTimer.Text.Contains("READY") || TxtTimer.Text.Contains("PRONTO"))) return;
            string timeText, statusText; Brush colorBrush, statusBrush;
            string safeTxt = _currentLangData?.UI.GetValueOrDefault("Safe", "SAFE") ?? "SAFE";
            string demonTxt = _currentLangData?.UI.GetValueOrDefault("Demon", "DEMON") ?? "DEMON";

            if (_timerSecondsRemaining <= 0) { timeText = "READY"; statusText = "HUNT!"; colorBrush = Brushes.Red; statusBrush = Brushes.Red; }
            else
            {
                TimeSpan t = TimeSpan.FromSeconds(_timerSecondsRemaining); timeText = string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds);
                if (_timerSecondsRemaining > 120) { colorBrush = Brushes.White; statusText = safeTxt; statusBrush = Brushes.LightGreen; }
                else if (_timerSecondsRemaining <= 120 && _timerSecondsRemaining > 90) { colorBrush = (Brush)new BrushConverter().ConvertFrom("#FF6666"); statusText = demonTxt; statusBrush = Brushes.Orange; }
                else { colorBrush = Brushes.Red; statusText = "UNSAFE"; statusBrush = Brushes.Red; }
            }
            TxtTimer.Text = timeText; TxtTimer.Foreground = colorBrush; TxtTimerStatus.Text = statusText; TxtTimerStatus.Foreground = statusBrush;
            TxtMiniTimer.Text = timeText; TxtMiniTimer.Foreground = colorBrush; TxtMiniStatus.Text = statusText; TxtMiniStatus.Foreground = statusBrush;
            TxtIntelTimer.Text = timeText; TxtIntelTimer.Foreground = colorBrush; TxtIntelStatus.Text = statusText; TxtIntelStatus.Foreground = statusBrush;
        }

        private void ResetHuntTimer() { _huntTimerSeconds = 25; ProgressHuntTimer.Value = 25; _huntTimer.Start(); UpdateHuntTimerDisplay(); }
        private void StopHuntTimer()
        {
            _huntTimer.Stop();
            string ready = _currentLangData?.UI.GetValueOrDefault("Ready", "READY") ?? "READY";
            TxtHuntTimer.Text = ready; TxtMiniHuntTimer.Text = ready; TxtIntelHuntTimer.Text = ready;
            TxtHuntTimer.Foreground = Brushes.White;
        }

        private void HuntTimer_Tick(object sender, EventArgs e)
        {
            _huntTimerSeconds--; ProgressHuntTimer.Value = _huntTimerSeconds; UpdateHuntTimerDisplay();
            if (_huntTimerSeconds <= 0) { _huntTimer.Stop(); PlayAudioCue("spirit"); TxtHuntTimer.Foreground = Brushes.Red; }
        }

        private void UpdateHuntTimerDisplay()
        {
            TimeSpan t = TimeSpan.FromSeconds(_huntTimerSeconds); string txt = string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds); Brush c = Brushes.White;
            if (_huntTimerSeconds > 5) c = Brushes.LightGreen; else if (_huntTimerSeconds > 0) c = Brushes.Orange; else c = Brushes.Red;
            TxtHuntTimer.Text = txt; TxtHuntTimer.Foreground = c; TxtMiniHuntTimer.Text = txt; TxtMiniHuntTimer.Foreground = c; TxtIntelHuntTimer.Text = txt; TxtIntelHuntTimer.Foreground = c;
        }

        private void PlayAudioCue(string type) { Task.Run(() => { try { if (type == "demon") { Console.Beep(200, 400); Thread.Sleep(100); Console.Beep(200, 400); } else if (type == "normal") { Console.Beep(500, 200); Thread.Sleep(100); Console.Beep(500, 200); Thread.Sleep(100); Console.Beep(500, 200); } else if (type == "spirit") { Console.Beep(1000, 300); Thread.Sleep(50); Console.Beep(1000, 300); Thread.Sleep(50); Console.Beep(1000, 800); } } catch { } }); }

        // --- HELPER METHODS ---
        private void CheckKey(int key, Action action)
        {
            bool isDown = (GetAsyncKeyState(key) & 0x8000) != 0;
            if (!_keyStateTracker.ContainsKey(key)) _keyStateTracker[key] = false;
            if (isDown && !_keyStateTracker[key]) { _keyStateTracker[key] = true; Dispatcher.InvokeAsync(action, DispatcherPriority.Send); }
            else if (!isDown && _keyStateTracker[key]) { _keyStateTracker[key] = false; }
        }
        protected override void OnClosed(EventArgs e) { _cancellationTokenSource.Cancel(); base.OnClosed(e); }
        private void BtnDonate_Click(object sender, RoutedEventArgs e) { try { Process.Start(new ProcessStartInfo { FileName = "https://ko-fi.com/hopesan", UseShellExecute = true }); } catch { } }
        private void Window_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
        private void MenuEn_Click(object sender, RoutedEventArgs e) => ChangeLanguage("en");
        private void MenuPt_Click(object sender, RoutedEventArgs e) => ChangeLanguage("pt");
        private void MenuJp_Click(object sender, RoutedEventArgs e) => ChangeLanguage("jp");
        private void MenuCht_Click(object sender, RoutedEventArgs e) => ChangeLanguage("cht");
        private void ChangeLanguage(string langCode) { _config.Language = langCode; _config.Save(); LoadLanguage(langCode); }
        private void MenuExit_Click(object sender, RoutedEventArgs e) { Application.Current.Shutdown(); }
        private void SetViewMode(int mode)
        {
            if (_currentView == mode) mode = 0; _currentView = mode;
            MainJournal.Visibility = Visibility.Collapsed; MiniHud.Visibility = Visibility.Collapsed; IntelHud.Visibility = Visibility.Collapsed;
            if (_currentView == 0) MainJournal.Visibility = Visibility.Visible;
            else if (_currentView == 1) MiniHud.Visibility = Visibility.Visible;
            else if (_currentView == 2) IntelHud.Visibility = Visibility.Visible;
        }
    }

    // ==========================================
    // LOGIC CLASSES ONLY (DataModels are in the other file!)
    // ==========================================

    public class Ghost : INotifyPropertyChanged
    {
        public string ID { get; set; }
        public string Name { get; set; }
        public string Symbol { get; set; }
        public List<string> Evidences { get; set; }
        public string Tell { get; set; }
        public string HuntThreshold { get; set; }

        // SPEED DATA
        public double MinSpeed { get; set; }
        public double MaxSpeed { get; set; }
        public string SpeedInfo { get; set; }

        // NEW: GUARANTEED EVIDENCE
        public string Guaranteed { get; set; }
        public string GuaranteedText => !string.IsNullOrEmpty(Guaranteed) ? $"⚠ Guaranteed: {Guaranteed}" : "";
        public bool HasGuaranteed => !string.IsNullOrEmpty(Guaranteed);

        private bool _isEliminated;
        public bool IsEliminated { get => _isEliminated; set { _isEliminated = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsEliminated")); } }

        public Brush PrimaryColorBrush { get { return (Brush)new BrushConverter().ConvertFrom(PrimaryColor); } }
        public string PrimaryColor
        {
            get
            {
                foreach (var ev in Evidences)
                {
                    if (IsType(ev, "Freezing")) return "#00CED1";
                    if (IsType(ev, "EMF")) return "#DC143C";
                    if (IsType(ev, "DOTS")) return "#39FF14";
                    if (IsType(ev, "UV")) return "#8A2BE2";
                    if (IsType(ev, "SpiritBox")) return "#FF4500";
                    if (IsType(ev, "Writing")) return "#FFD700";
                    if (IsType(ev, "Orb")) return "#E0FFFF";
                }
                return "#FFFFFF";
            }
        }

        public Brush EvidenceGradient
        {
            get
            {
                var stops = new GradientStopCollection();
                Color GetColor(string ev)
                {
                    if (IsType(ev, "EMF")) return Color.FromRgb(220, 20, 60);
                    if (IsType(ev, "UV")) return Color.FromRgb(138, 43, 226);
                    if (IsType(ev, "Freezing")) return Color.FromRgb(0, 206, 209);
                    if (IsType(ev, "SpiritBox")) return Color.FromRgb(255, 69, 0);
                    if (IsType(ev, "Orb")) return Color.FromRgb(224, 255, 255);
                    if (IsType(ev, "Writing")) return Color.FromRgb(255, 215, 0);
                    if (IsType(ev, "DOTS")) return Color.FromRgb(57, 255, 20);
                    return Colors.White;
                }
                if (Evidences.Count >= 1) stops.Add(new GradientStop(GetColor(Evidences[0]), 0.0));
                if (Evidences.Count >= 2) stops.Add(new GradientStop(GetColor(Evidences[1]), 0.5));
                if (Evidences.Count >= 3) stops.Add(new GradientStop(GetColor(Evidences[2]), 1.0));
                return new LinearGradientBrush(stops, new Point(0, 0), new Point(1, 0)) { Opacity = 0.9 };
            }
        }

        private bool IsType(string ev, string type)
        {
            if (type == "EMF") return ev.Contains("EMF");
            if (type == "UV") return ev.Contains("Ultraviolet") || ev.Contains("Violet") || ev.Contains("Finger") || ev.Contains("Digital") || ev.Contains("指") || ev.Contains("紫") || ev.Contains("紫外線");
            if (type == "Freezing") return ev.Contains("Freezing") || ev.Contains("Gelado") || ev.Contains("Baixa") || ev.Contains("氷") || ev.Contains("寒") || ev.Contains("冷") || ev.Contains("低");
            if (type == "SpiritBox") return ev.Contains("Spirit") || ev.Contains("Box") || ev.Contains("BOX") || ev.Contains("通灵") || ev.Contains("通靈") || ev.Contains("盒") || ev.Contains("スピリット");
            if (type == "Orb") return ev.Contains("Orb") || ev.Contains("Orbe") || ev.Contains("灵球") || ev.Contains("靈球") || ev.Contains("オーブ") || ev.Contains("玉");
            if (type == "Writing") return ev.Contains("Writing") || ev.Contains("Escrita") || ev.Contains("笔") || ev.Contains("筆") || ev.Contains("本") || ev.Contains("ライティング");
            if (type == "DOTS") return ev.Contains("D.O.T.S.") || ev.Contains("DOTS") || ev.Contains("點陣");
            return false;
        }

        public Ghost(string n, string s, params string[] e) { Name = n; Symbol = s; Evidences = e.ToList(); }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    // Keep Converters HERE because XAML looks for "local:BooleanToVisibilityConverter"
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