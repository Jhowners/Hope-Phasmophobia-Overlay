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
        private int _activeTabMode = 0; // 0 = Evidence Mode, 1 = 0-Evidence Mode

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
        private readonly List<string> _fastGhosts = new List<string> { "Jinn", "Revenant", "Hantu", "The Twins", "Raiju", "Moroi", "Deogen", "Thaye", "The Mimic", "Dayan", "Obambo", "Gallu", "Aswang", "Deildegast", "Kormos" };
        private readonly List<string> _slowGhosts = new List<string> { "Revenant", "Hantu", "Deogen", "Thaye", "The Mimic", "The Twins", "Moroi", "Dayan", "Obambo", "Gallu", "Aswang", "Deildegast" };

        // Timers
        private DispatcherTimer _smudgeTimer;
        private int _timerSecondsRemaining = 180;
        private DispatcherTimer _huntTimer;
        private int _huntTimerSeconds = 25;

        // Hunt Duration Timer
        private DispatcherTimer _huntDurationTimer;
        private int _huntDurationSeconds = 0;
        private int _huntDurationLimit = 30;

        public MainWindow()
        {
            InitializeComponent();
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;

            try
            {
                System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome
                {
                    CaptionHeight = 0,
                    ResizeBorderThickness = new Thickness(6),
                    CornerRadius = new CornerRadius(12),
                    GlassFrameThickness = new Thickness(0),
                    UseAeroCaptionButtons = false
                });
            }
            catch { }

            // 1. LOAD CONFIG
            _config = AppSettings.Load();
            if (!File.Exists("settings.json"))
            {
                _config.Save();
            }
            this.Opacity = _config.Opacity;

            MenuDiscord.IsChecked = _config.EnableDiscord;
            UpdateVolumeMenuCheckedState();

            if (_config.EnableDiscord)
            {
                App.DiscordRpc?.Initialize();
            }

            // 2. SETUP CHECKBOXES
            _evidenceCheckBoxes.Add(ChkEv1); _evidenceCheckBoxes.Add(ChkEv2);
            _evidenceCheckBoxes.Add(ChkEv3); _evidenceCheckBoxes.Add(ChkEv4);
            _evidenceCheckBoxes.Add(ChkEv5); _evidenceCheckBoxes.Add(ChkEv6);
            _evidenceCheckBoxes.Add(ChkEv7);

            // 3. SETUP TIMERS
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

            // 4. RESTORE CONTROLS & SELECTIONS
            if (CmbMapList != null && _config.SelectedMapIndex >= 0 && _config.SelectedMapIndex < CmbMapList.Items.Count)
                CmbMapList.SelectedIndex = _config.SelectedMapIndex;
            if (CmbDifficulty != null && _config.SelectedDifficultyIndex >= 0 && _config.SelectedDifficultyIndex < CmbDifficulty.Items.Count)
                CmbDifficulty.SelectedIndex = _config.SelectedDifficultyIndex;
            if (ChkCursedHunt != null)
                ChkCursedHunt.IsChecked = _config.IsCursedHunt;

            RecalculateHuntLimit();

            // 5. LOAD LANGUAGE (From JSON)
            LoadLanguage(_config.Language);

            // 6. RESTORE TAB MODE
            SetTabMode(_config.ActiveTabMode);

            // 7. START INPUT LOOP
            _cancellationTokenSource = new CancellationTokenSource();
            Task.Factory.StartNew(() => InputLoop(_cancellationTokenSource.Token), TaskCreationOptions.LongRunning);
        }

        private void InputLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                // 1. Global Timers (No Modifier needed)
                CheckKey(_config.Keys.HuntStop, () => StopHuntTimer());
                CheckKey(_config.Keys.HuntStart, () => ResetHuntTimer());
                CheckKey(_config.Keys.SmudgeStop, () => StopSmudgeTimer());
                CheckKey(_config.Keys.SmudgeStart, () => ResetSmudgeTimer());
                CheckKey(_config.Keys.SpeedTap, () => CalculateBPM());
                CheckKey(_config.Keys.SpeedReset, () => ResetPace());
                CheckKey(_config.Keys.HuntDuration, () => ToggleHuntDuration());

                // 2. Views
                CheckKey(VK_PAUSE, () => SetViewMode(1));
                CheckKey(VK_HOME, () => SetViewMode(2));

                // 3. ALT MODIFIER ACTIONS
                bool isAltDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;

                if (isAltDown)
                {
                    // Smart Reset
                    CheckKey(_config.Keys.Reset, () => SmartReset());

                    // Evidence Toggles (Using the editable keys!)
                    CheckKey(_config.Keys.Evidence1, () => ToggleEvidenceByIndex(1));
                    CheckKey(_config.Keys.Evidence2, () => ToggleEvidenceByIndex(2));
                    CheckKey(_config.Keys.Evidence3, () => ToggleEvidenceByIndex(3));
                    CheckKey(_config.Keys.Evidence4, () => ToggleEvidenceByIndex(4));
                    CheckKey(_config.Keys.Evidence5, () => ToggleEvidenceByIndex(5));
                    CheckKey(_config.Keys.Evidence6, () => ToggleEvidenceByIndex(6));
                    CheckKey(_config.Keys.Evidence7, () => ToggleEvidenceByIndex(7));
                }

                Thread.Sleep(20);
            }
        }

        // --- MAP & DIFFICULTY HUNT DURATION LOGIC ---
        private void RecalculateHuntLimit()
        {
            int mapIndex = CmbMapList?.SelectedIndex ?? 0;
            int diffIndex = CmbDifficulty?.SelectedIndex ?? 2;
            bool isCursed = ChkCursedHunt?.IsChecked == true;

            int mapCategory = 0; // 0 = Small, 1 = Medium, 2 = Large
            if (mapIndex >= 9 && mapIndex <= 10) mapCategory = 1;
            else if (mapIndex >= 11) mapCategory = 2;

            int baseDuration = 30;
            if (diffIndex == 0) // Amateur
            {
                baseDuration = mapCategory == 0 ? 15 : (mapCategory == 1 ? 30 : 40);
            }
            else if (diffIndex == 1) // Intermediate
            {
                baseDuration = mapCategory == 0 ? 20 : (mapCategory == 1 ? 40 : 50);
            }
            else // Professional / Nightmare / 0-Ev
            {
                baseDuration = mapCategory == 0 ? 30 : (mapCategory == 1 ? 50 : 60);
            }

            if (isCursed) baseDuration += 20;

            _huntDurationLimit = baseDuration;

            if (_config != null)
            {
                _config.SelectedMapIndex = mapIndex;
                _config.SelectedDifficultyIndex = diffIndex;
                _config.IsCursedHunt = isCursed;
                _config.Save();
            }

            UpdateHuntDurationDisplay();
        }

        private void CmbMapList_SelectionChanged(object sender, SelectionChangedEventArgs e) => RecalculateHuntLimit();
        private void CmbDifficulty_SelectionChanged(object sender, SelectionChangedEventArgs e) => RecalculateHuntLimit();
        private void ChkCursedHunt_Click(object sender, RoutedEventArgs e) => RecalculateHuntLimit();

        private void ToggleHuntDuration()
        {
            if (_huntDurationTimer.IsEnabled)
            {
                _huntDurationTimer.Stop();
            }
            else
            {
                RecalculateHuntLimit();
                _huntDurationSeconds = 0;
                _huntDurationTimer.Start();
            }
            UpdateHuntDurationDisplay();
        }

        private void HuntDuration_Tick(object sender, EventArgs e)
        {
            _huntDurationSeconds++;
            if (_huntDurationSeconds >= _huntDurationLimit)
            {
                _huntDurationTimer.Stop();
                PlayAudioCue("hunt_end");
            }
            UpdateHuntDurationDisplay();
        }

        private void UpdateHuntDurationDisplay()
        {
            if (TxtActiveHunt == null) return;

            if (_huntDurationTimer.IsEnabled)
            {
                TxtActiveHunt.Text = $"{_huntDurationSeconds:D2} / {_huntDurationLimit}s";
                TxtActiveHunt.Foreground = Brushes.Red;
            }
            else
            {
                if (_huntDurationSeconds >= _huntDurationLimit && _huntDurationLimit > 0)
                {
                    TxtActiveHunt.Text = $"{_huntDurationLimit:D2} / {_huntDurationLimit}s [DONE]";
                    TxtActiveHunt.Foreground = Brushes.LightGreen;
                }
                else
                {
                    TxtActiveHunt.Text = $"-- / {_huntDurationLimit}s";
                    TxtActiveHunt.Foreground = (Brush)new BrushConverter().ConvertFrom("#FF8888")!;
                }
            }
        }

        // --- TAB MODE SWITCHING (EVIDENCE vs 0-EVIDENCE) ---
        private void BtnModeEvidence_Click(object sender, RoutedEventArgs e) => SetTabMode(0);
        private void BtnModeNoEvidence_Click(object sender, RoutedEventArgs e) => SetTabMode(1);

        private void SetTabMode(int mode)
        {
            _activeTabMode = mode;
            if (EvidenceGhostsContainer == null || NoEvidenceGhostsContainer == null) return;

            if (mode == 0)
            {
                EvidenceGhostsContainer.Visibility = Visibility.Visible;
                NoEvidenceGhostsContainer.Visibility = Visibility.Collapsed;
                if (EvidenceModeControls != null) EvidenceModeControls.Visibility = Visibility.Visible;
                if (NoEvidenceModeControls != null) NoEvidenceModeControls.Visibility = Visibility.Collapsed;

                if (BtnModeEvidence != null)
                {
                    BtnModeEvidence.Background = (Brush)new BrushConverter().ConvertFrom("#889d00ff");
                    BtnModeEvidence.Foreground = Brushes.White;
                }
                if (BtnModeNoEvidence != null)
                {
                    BtnModeNoEvidence.Background = (Brush)new BrushConverter().ConvertFrom("#22000000");
                    BtnModeNoEvidence.Foreground = (Brush)new BrushConverter().ConvertFrom("#AAA");
                }
            }
            else
            {
                EvidenceGhostsContainer.Visibility = Visibility.Collapsed;
                NoEvidenceGhostsContainer.Visibility = Visibility.Visible;
                if (EvidenceModeControls != null) EvidenceModeControls.Visibility = Visibility.Collapsed;
                if (NoEvidenceModeControls != null) NoEvidenceModeControls.Visibility = Visibility.Visible;

                if (BtnModeEvidence != null)
                {
                    BtnModeEvidence.Background = (Brush)new BrushConverter().ConvertFrom("#22000000");
                    BtnModeEvidence.Foreground = (Brush)new BrushConverter().ConvertFrom("#AAA");
                }
                if (BtnModeNoEvidence != null)
                {
                    BtnModeNoEvidence.Background = (Brush)new BrushConverter().ConvertFrom("#889d00ff");
                    BtnModeNoEvidence.Foreground = Brushes.White;
                }
            }

            if (_config != null)
            {
                _config.ActiveTabMode = mode;
                _config.Save();
            }

            UpdateGhostFiltering();
        }

        private void ZeroEvidenceFilter_Changed(object sender, RoutedEventArgs e) => UpdateGhostFiltering();
        private void ZeroEvidenceFilter_Changed(object sender, SelectionChangedEventArgs e) => UpdateGhostFiltering();

        private void BtnResetZeroFilters_Click(object sender, RoutedEventArgs e)
        {
            if (CmbZeroSanity != null) CmbZeroSanity.SelectedIndex = 0;
            if (CmbZeroBlink != null) CmbZeroBlink.SelectedIndex = 0;
            if (CmbZeroSmudge != null) CmbZeroSmudge.SelectedIndex = 0;

            if (ChkZeroSalt != null) ChkZeroSalt.IsChecked = false;
            if (ChkZeroCandle != null) ChkZeroCandle.IsChecked = false;
            if (ChkZeroOrbs != null) ChkZeroOrbs.IsChecked = false;
            if (ChkZeroFootsteps != null) ChkZeroFootsteps.IsChecked = false;
            if (ChkZeroPhoto != null) ChkZeroPhoto.IsChecked = false;
            if (ChkZeroBreaker != null) ChkZeroBreaker.IsChecked = false;
            if (ChkZeroLight != null) ChkZeroLight.IsChecked = false;
            if (ChkZeroDeogen != null) ChkZeroDeogen.IsChecked = false;
            if (ChkZeroFreezingBreath != null) ChkZeroFreezingBreath.IsChecked = false;

            if (AllGhosts != null)
            {
                foreach (var g in AllGhosts)
                {
                    g.IsManuallyEliminated = false;
                }
            }

            UpdateGhostFiltering();
        }

        private void GhostCard_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is Ghost ghost)
            {
                ghost.IsManuallyEliminated = !ghost.IsManuallyEliminated;
                UpdateGhostFiltering();
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

                if (_currentLangData?.UI == null) return;

                // Apply UI Strings
                var ui = _currentLangData.UI;
                if (BtnModeEvidence != null) BtnModeEvidence.Content = ui.GetValueOrDefault("ModeEvidence", "📋 EVIDENCE MODE");
                if (BtnModeNoEvidence != null) BtnModeNoEvidence.Content = ui.GetValueOrDefault("ModeNoEvidence", "👁️ 0-EVIDENCE IDENTIFIER");
                if (LblControls != null) LblControls.Text = ui.GetValueOrDefault("Controls", "Missing String");
                if (LblEvidence != null) LblEvidence.Text = ui.GetValueOrDefault("EvidenceHeader", "Evidence");
                if (LblSmudge != null) LblSmudge.Text = ui.GetValueOrDefault("SmudgeHeader", "Smudge");
                if (LblHunt != null) LblHunt.Text = ui.GetValueOrDefault("HuntHeader", "Hunt");
                if (LblSpeed != null) LblSpeed.Text = ui.GetValueOrDefault("SpeedHeader", "Speed");

                if (ChkEv1 != null) ChkEv1.Content = ui.GetValueOrDefault("Ev1", "EMF 5");
                if (ChkEv2 != null) ChkEv2.Content = ui.GetValueOrDefault("Ev2", "DOTS");
                if (ChkEv3 != null) ChkEv3.Content = ui.GetValueOrDefault("Ev3", "UV");
                if (ChkEv4 != null) ChkEv4.Content = ui.GetValueOrDefault("Ev4", "Freezing");
                if (ChkEv5 != null) ChkEv5.Content = ui.GetValueOrDefault("Ev5", "Orbs");
                if (ChkEv6 != null) ChkEv6.Content = ui.GetValueOrDefault("Ev6", "Writing");
                if (ChkEv7 != null) ChkEv7.Content = ui.GetValueOrDefault("Ev7", "Spirit Box");

                // Mini HUD and Intel HUD labels
                if (LblMiniSmudge != null) LblMiniSmudge.Text = ui.GetValueOrDefault("SmudgeHeader", "SMUDGE").Split('[')[0].Trim();
                if (LblMiniHunt != null) LblMiniHunt.Text = ui.GetValueOrDefault("HuntHeader", "HUNT CD").Split('[')[0].Trim();
                if (LblIntelSmudge != null) LblIntelSmudge.Text = ui.GetValueOrDefault("SmudgeHeader", "SMUDGE").Split('[')[0].Trim();
                if (LblIntelHunt != null) LblIntelHunt.Text = ui.GetValueOrDefault("HuntHeader", "HUNT CD").Split('[')[0].Trim();
                if (LblIntelSpeed != null) LblIntelSpeed.Text = ui.GetValueOrDefault("SpeedHeader", "SPEED").Split('(')[0].Trim();
                if (LblIntelEvidence != null) LblIntelEvidence.Text = ui.GetValueOrDefault("EvidenceHeader", "EVIDENCE").Split('(')[0].Trim();

                // 0-Evidence Filters Headers & Checkboxes
                if (LblZeroFilters != null) LblZeroFilters.Text = ui.GetValueOrDefault("ZeroFilters", "0-EVIDENCE FILTERS");
                if (LblZeroSanity != null) LblZeroSanity.Text = ui.GetValueOrDefault("ZeroSanity", "HUNT SANITY");
                if (LblZeroBlink != null) LblZeroBlink.Text = ui.GetValueOrDefault("ZeroBlink", "BLINK PATTERN");
                if (LblZeroSmudge != null) LblZeroSmudge.Text = ui.GetValueOrDefault("ZeroSmudge", "SMUDGE HUNT CD");
                if (LblZeroTests != null) LblZeroTests.Text = ui.GetValueOrDefault("ZeroTests", "BEHAVIORAL TESTS");
                if (BtnResetZeroFilters != null) BtnResetZeroFilters.Content = ui.GetValueOrDefault("BtnResetZero", "🔄 Reset 0-Ev Filters");

                if (ChkZeroSalt != null) ChkZeroSalt.Content = ui.GetValueOrDefault("ChkZeroSalt", "🧂 Stepped in Salt (No Wraith)");
                if (ChkZeroCandle != null) ChkZeroCandle.Content = ui.GetValueOrDefault("ChkZeroCandle", "🕯️ 3rd Candle Blow = Hunt (Onryo)");
                if (ChkZeroOrbs != null) ChkZeroOrbs.Content = ui.GetValueOrDefault("ChkZeroOrbs", "🔮 Orbs Seen in 0-Ev (The Mimic)");
                if (ChkZeroFootsteps != null) ChkZeroFootsteps.Content = ui.GetValueOrDefault("ChkZeroFootsteps", "🔇 Footsteps Quiet >10m (Myling)");
                if (ChkZeroPhoto != null) ChkZeroPhoto.Content = ui.GetValueOrDefault("ChkZeroPhoto", "📷 Disappears in Photo (Phantom)");
                if (ChkZeroBreaker != null) ChkZeroBreaker.Content = ui.GetValueOrDefault("ChkZeroBreaker", "⚡ Breaker Turned Off (No Jinn)");
                if (ChkZeroLight != null) ChkZeroLight.Content = ui.GetValueOrDefault("ChkZeroLight", "💡 Light Turned On (No Mare)");
                if (ChkZeroDeogen != null) ChkZeroDeogen.Content = ui.GetValueOrDefault("ChkZeroDeogen", "👁️ Always Knows / Slow Close (Deogen)");
                if (ChkZeroFreezingBreath != null) ChkZeroFreezingBreath.Content = ui.GetValueOrDefault("ChkZeroFreezingBreath", "💨 Freezing Breath in Hunt (Hantu)");

                // Active Hunt & Map/Difficulty
                if (LblActiveHuntTitle != null) LblActiveHuntTitle.Text = ui.GetValueOrDefault("ActiveHuntTitle", "ACTIVE HUNT [F1]");
                if (ChkCursedHunt != null) ChkCursedHunt.Content = ui.GetValueOrDefault("CursedHunt", "Cursed (+20s)");
                if (LblMapList != null) LblMapList.Text = ui.GetValueOrDefault("MapListHeader", "MAP / AREA");
                if (LblDifficulty != null) LblDifficulty.Text = ui.GetValueOrDefault("DifficultyHeader", "DIFFICULTY");
                if (BtnResetPace != null) BtnResetPace.Content = ui.GetValueOrDefault("BtnResetPace", "RST (F11)");

                Ghost.GuaranteedPrefix = ui.GetValueOrDefault("GuaranteedPrefix", "Guaranteed");

                // Dropdowns from JSON
                PopulateDropdownsFromLanguage(_currentLangData);

                // Update Timers text if stopped
                if (_smudgeTimer != null)
                {
                    if (!_smudgeTimer.IsEnabled) StopSmudgeTimer();
                    else UpdateTimerDisplay();
                }
                if (_huntTimer != null)
                {
                    if (!_huntTimer.IsEnabled) StopHuntTimer();
                    else UpdateHuntTimerDisplay();
                }

                // Refresh Speed Labels in both modes
                if (_lastCalculatedSpeed > 0)
                {
                    InterpretSpeed(_lastCalculatedSpeed);
                }
                else
                {
                    string waitText = ui.GetValueOrDefault("Waiting", "WAITING...");
                    if (TxtGhostSpeedGuess != null)
                    {
                        TxtGhostSpeedGuess.Text = waitText;
                        TxtGhostSpeedGuess.Foreground = Brushes.Gray;
                    }
                    if (TxtIntelSpeedGuess != null)
                    {
                        TxtIntelSpeedGuess.Text = waitText;
                        TxtIntelSpeedGuess.Foreground = Brushes.Gray;
                    }
                }

                // Build Ghost List directly from JSON
                AllGhosts.Clear();
                if (_currentLangData.Ghosts != null)
                {
                    foreach (var g in _currentLangData.Ghosts)
                    {
                        // Map the Data from JSON (GhostData) to Logic Class (Ghost)
                        var ghost = new Ghost(g.Name, g.Symbol, g.Evidences?.ToArray() ?? Array.Empty<string>())
                        {
                            ID = g.ID,
                            Tell = g.Tell,
                            HuntThreshold = g.HuntThreshold,
                            MinSpeed = g.MinSpeed,
                            MaxSpeed = g.MaxSpeed,
                            Guaranteed = g.Guaranteed,
                            ZeroEvidenceTest = !string.IsNullOrEmpty(g.ZeroEvidenceTest) ? g.ZeroEvidenceTest : (g.Tell ?? ""),
                            BehavioralTags = (g.BehavioralTags != null && g.BehavioralTags.Count > 0) ? g.BehavioralTags : new List<string> { "Standard Speed" },
                            SpeedInfo = (g.MinSpeed == g.MaxSpeed) ? $"{g.MinSpeed:0.0} m/s" : $"{g.MinSpeed:0.0} - {g.MaxSpeed:0.0} m/s"
                        };
                        AssignGhostTraits(ghost);
                        AllGhosts.Add(ghost);
                    }
                }

                if (GhostsListControl != null)
                {
                    GhostsListControl.ItemsSource = null;
                    GhostsListControl.ItemsSource = AllGhosts;
                }
                if (NoEvidenceGhostsListControl != null)
                {
                    NoEvidenceGhostsListControl.ItemsSource = null;
                    NoEvidenceGhostsListControl.ItemsSource = AllGhosts;
                }
                if (GhostsIntelControl != null)
                {
                    GhostsIntelControl.ItemsSource = null;
                    GhostsIntelControl.ItemsSource = AllGhosts;
                }

                UpdateGhostFiltering();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading language: " + ex.Message);
            }
        }

        private void PopulateDropdownsFromLanguage(LangFile langData)
        {
            if (langData == null) return;

            // 1. Sanity
            if (CmbZeroSanity != null && langData.SanityOptions != null && langData.SanityOptions.Count > 0)
            {
                int cur = CmbZeroSanity.SelectedIndex;
                CmbZeroSanity.Items.Clear();
                foreach (var opt in langData.SanityOptions)
                    CmbZeroSanity.Items.Add(new ComboBoxItem { Content = opt });
                CmbZeroSanity.SelectedIndex = (cur >= 0 && cur < langData.SanityOptions.Count) ? cur : 0;
            }

            // 2. Blink
            if (CmbZeroBlink != null && langData.BlinkOptions != null && langData.BlinkOptions.Count > 0)
            {
                int cur = CmbZeroBlink.SelectedIndex;
                CmbZeroBlink.Items.Clear();
                foreach (var opt in langData.BlinkOptions)
                    CmbZeroBlink.Items.Add(new ComboBoxItem { Content = opt });
                CmbZeroBlink.SelectedIndex = (cur >= 0 && cur < langData.BlinkOptions.Count) ? cur : 0;
            }

            // 3. Smudge
            if (CmbZeroSmudge != null && langData.SmudgeOptions != null && langData.SmudgeOptions.Count > 0)
            {
                int cur = CmbZeroSmudge.SelectedIndex;
                CmbZeroSmudge.Items.Clear();
                foreach (var opt in langData.SmudgeOptions)
                    CmbZeroSmudge.Items.Add(new ComboBoxItem { Content = opt });
                CmbZeroSmudge.SelectedIndex = (cur >= 0 && cur < langData.SmudgeOptions.Count) ? cur : 0;
            }

            // 4. Difficulty
            if (CmbDifficulty != null && langData.DifficultyOptions != null && langData.DifficultyOptions.Count > 0)
            {
                int cur = CmbDifficulty.SelectedIndex;
                CmbDifficulty.Items.Clear();
                foreach (var opt in langData.DifficultyOptions)
                    CmbDifficulty.Items.Add(new ComboBoxItem { Content = opt });
                CmbDifficulty.SelectedIndex = (cur >= 0 && cur < langData.DifficultyOptions.Count) ? cur : 2;
            }

            // 5. Map List
            if (CmbMapList != null && langData.UI != null)
            {
                int cur = CmbMapList.SelectedIndex;
                CmbMapList.Items.Clear();
                string sm = langData.UI.GetValueOrDefault("SizeSmall", "(Small)");
                string md = langData.UI.GetValueOrDefault("SizeMedium", "(Medium)");
                string lg = langData.UI.GetValueOrDefault("SizeLarge", "(Large)");

                string[] maps = new[]
                {
                    $"6 Tanglewood Drive {sm}",
                    $"42 Edgefield Road {sm}",
                    $"10 Ridgeview Court {sm}",
                    $"13 Grafton Farmhouse {sm}",
                    $"13 Willow Street {sm}",
                    $"Bleasdale Farmhouse {sm}",
                    $"Camp Woodwind {sm}",
                    $"Point Hope {sm}",
                    $"Sunny Meadows - Restricted {sm}",
                    $"Maple Lodge Campsite {md}",
                    $"Prison {md}",
                    $"Brownstone High School {lg}",
                    $"Sunny Meadows - Full {lg}"
                };
                foreach (var m in maps) CmbMapList.Items.Add(new ComboBoxItem { Content = m });
                CmbMapList.SelectedIndex = (cur >= 0 && cur < maps.Length) ? cur : 0;
            }
        }

        private void CalculateBPM(bool isNewTap = true)
        {
            DateTime now = DateTime.Now;

            // Always clear old taps (older than 3s) so we don't calculate stale data
            _tapHistory.RemoveAll(d => (now - d).TotalSeconds > 3);

            // ONLY add to history if this was a real key press
            if (isNewTap)
            {
                _tapHistory.Add(now);
            }

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
            // Only reset text if we have no history AND it was a real tap attempt
            else if (isNewTap)
            {
                TxtBPM.Text = "Tap...";
                TxtIntelBPM.Text = "Tap...";
            }
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
            else if (ms <= 4.0)  // 3.25 to 4.0 (Moroi Max Speed)
            {
                key = "MaxLOS"; c = Brushes.Magenta; _currentSpeedCategory = "Fast";
            }
            else // > 4.0 m/s (Impossible human/ghost speed)
            {
                key = "Impossible"; c = Brushes.Crimson; _currentSpeedCategory = "Impossible";
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
            string filterText = _currentSpeedCategory == "Impossible" ? "IMPOSSIBLE (>4.0 m/s)" : _currentSpeedCategory.ToUpper();
            TxtPacerStatus.Text = $"FILTER: {filterText}";
            TxtPacerStatus.Foreground = c;

            // Trigger the Filtering
            UpdateGhostFiltering();
            UpdateDiscordStatus();
        }

        private void ResetPace()
        {
            _tapHistory.Clear();
            _lastCalculatedSpeed = 0.0;
            string txt = "-- m/s";
            if (TxtBPM != null) TxtBPM.Text = txt;
            if (TxtIntelBPM != null) TxtIntelBPM.Text = txt;

            string waitText = _currentLangData?.UI.GetValueOrDefault("Waiting", "WAITING...") ?? "WAITING...";
            if (TxtGhostSpeedGuess != null)
            {
                TxtGhostSpeedGuess.Text = waitText;
                TxtGhostSpeedGuess.Foreground = Brushes.Gray;
            }
            if (TxtIntelSpeedGuess != null)
            {
                TxtIntelSpeedGuess.Text = waitText;
                TxtIntelSpeedGuess.Foreground = Brushes.Gray;
            }

            if (TxtPacerStatus != null)
            {
                TxtPacerStatus.Text = "FILTER: NONE";
                TxtPacerStatus.Foreground = Brushes.Gray;
            }
            _currentSpeedCategory = "None";
            UpdateGhostFiltering();
            UpdateDiscordStatus();
        }

        private void UpdateGhostFiltering()
        {
            if (AllGhosts == null || AllGhosts.Count == 0) return;

            if (_activeTabMode == 0)
            {
                // === EVIDENCE MODE ===
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
                    if (TxtIntelFound != null) { TxtIntelFound.Text = foundStr; TxtIntelFound.Visibility = Visibility.Visible; }
                }
                else
                {
                    if (TxtIntelFound != null) { TxtIntelFound.Visibility = Visibility.Collapsed; }
                }

                foreach (var ghost in AllGhosts)
                {
                    bool elim = false;

                    // 1. Evidence Check (Fuzzy Logic)
                    foreach (var ev in foundEv)
                    {
                        if (ghost.ID == "The Mimic" && (ev.Contains("Orb") || ev.Contains("Orbe") || ev.Contains("靈球") || ev.Contains("Geestbal") || ev.Contains("огонёк") || ev.Contains("вогник") || ev.Contains("玉")))
                            continue;

                        bool match = ghost.Evidences.Any(gEv =>
                            ev.IndexOf(gEv, StringComparison.OrdinalIgnoreCase) >= 0 ||
                            gEv.IndexOf(ev, StringComparison.OrdinalIgnoreCase) >= 0);

                        if (!match)
                        {
                            elim = true;
                            break;
                        }
                    }

                    if (!elim)
                    {
                        foreach (var ev in ruledOutEv)
                        {
                            bool match = ghost.Evidences.Any(gEv =>
                                ev.IndexOf(gEv, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                gEv.IndexOf(ev, StringComparison.OrdinalIgnoreCase) >= 0);

                            if (match)
                            {
                                if (ghost.ID == "The Mimic" && (ev.Contains("Orb") || ev.Contains("Orbe") || ev.Contains("Geestbal") || ev.Contains("огонёк") || ev.Contains("вогник") || ev.Contains("玉")))
                                    continue;

                                elim = true;
                                break;
                            }
                        }
                    }

                    // 2. Speed Check
                    if (!elim && _lastCalculatedSpeed > 0 && _currentSpeedCategory != "None")
                    {
                        if (_currentSpeedCategory == "Impossible")
                        {
                            elim = true; // Speed > 4.0 m/s is impossible for all ghosts
                        }
                        else
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

                            if (!elim)
                            {
                                double margin = 0.20;
                                if (_lastCalculatedSpeed < (ghost.MinSpeed - margin) || _lastCalculatedSpeed > (ghost.MaxSpeed + margin))
                                {
                                    elim = true;
                                }
                            }
                        }
                    }

                    ghost.IsEliminated = elim;
                }
            }
            else
            {
                // === 0-EVIDENCE MODE ===
                int sanityFilter = CmbZeroSanity?.SelectedIndex ?? 0;
                int blinkFilter = CmbZeroBlink?.SelectedIndex ?? 0;
                int smudgeFilter = CmbZeroSmudge?.SelectedIndex ?? 0;

                bool traitSalt = ChkZeroSalt?.IsChecked == true;
                bool traitCandle = ChkZeroCandle?.IsChecked == true;
                bool traitOrbs = ChkZeroOrbs?.IsChecked == true;
                bool traitFootsteps = ChkZeroFootsteps?.IsChecked == true;
                bool traitPhoto = ChkZeroPhoto?.IsChecked == true;
                bool traitBreaker = ChkZeroBreaker?.IsChecked == true;
                bool traitLight = ChkZeroLight?.IsChecked == true;
                bool traitDeogen = ChkZeroDeogen?.IsChecked == true;
                bool traitFreezingBreath = ChkZeroFreezingBreath?.IsChecked == true;

                foreach (var ghost in AllGhosts)
                {
                    if (ghost.IsManuallyEliminated)
                    {
                        ghost.IsEliminated = true;
                        continue;
                    }

                    bool elim = false;

                    // 1. Sanity threshold filter
                    if (sanityFilter == 1) // Early (>60%)
                    {
                        if (ghost.SanityThresholdCategory != "Early" && ghost.SanityThresholdCategory != "Any" &&
                            ghost.ID != "Demon" && ghost.ID != "Yokai" && ghost.ID != "Thaye" && ghost.ID != "Mare" &&
                            ghost.ID != "Raiju" && ghost.ID != "Onryo" && ghost.ID != "Kormos")
                        {
                            elim = true;
                        }
                    }
                    else if (sanityFilter == 2) // Normal (50%)
                    {
                        if (ghost.SanityThresholdCategory != "Normal" && ghost.SanityThresholdCategory != "Any")
                        {
                            elim = true;
                        }
                    }
                    else if (sanityFilter == 3) // Late (<40%)
                    {
                        if (ghost.SanityThresholdCategory != "Late" && ghost.SanityThresholdCategory != "Any" &&
                            ghost.ID != "Shade" && ghost.ID != "Deogen")
                        {
                            elim = true;
                        }
                    }

                    // 2. Blink pattern filter
                    if (!elim && blinkFilter > 0)
                    {
                        if (blinkFilter == 1 && ghost.BlinkRate != "Slow" && ghost.ID != "The Mimic") elim = true; // Phantom
                        else if (blinkFilter == 2 && ghost.BlinkRate != "Fast" && ghost.ID != "The Mimic") elim = true; // Oni
                        else if (blinkFilter == 3 && ghost.BlinkRate != "Shapeshift" && ghost.ID != "The Mimic") elim = true; // Obake
                        else if (blinkFilter == 4 && ghost.BlinkRate != "Normal" && ghost.ID != "The Mimic") elim = true; // Normal
                    }

                    // 3. Smudge filter
                    if (!elim && smudgeFilter > 0)
                    {
                        if (smudgeFilter == 1 && ghost.SmudgeCooldown != "60s" && ghost.ID != "The Mimic") elim = true; // Demon
                        else if (smudgeFilter == 2 && ghost.SmudgeCooldown != "90s" && ghost.ID != "The Mimic") elim = true; // Standard
                        else if (smudgeFilter == 3 && ghost.SmudgeCooldown != "180s" && ghost.ID != "The Mimic") elim = true; // Spirit
                    }

                    // 4. Behavioral confirmation & elimination traits
                    if (!elim)
                    {
                        if (traitSalt && !ghost.StepsInSalt) elim = true; // Wraith never steps in salt
                        if (traitCandle && ghost.ID != "Onryo" && ghost.ID != "The Mimic") elim = true; // 3 candles blow = Onryo
                        if (traitOrbs && ghost.ID != "The Mimic") elim = true; // Orbs in 0-ev = Mimic
                        if (traitFootsteps && ghost.ID != "Myling" && ghost.ID != "The Mimic") elim = true; // Silent footsteps = Myling
                        if (traitPhoto && ghost.ID != "Phantom" && ghost.ID != "The Mimic") elim = true; // Disappears in photo = Phantom
                        if (traitBreaker && ghost.ID == "Jinn") elim = true; // Jinn never turns off breaker
                        if (traitLight && ghost.ID == "Mare") elim = true; // Mare never turns on light
                        if (traitDeogen && ghost.ID != "Deogen" && ghost.ID != "The Mimic") elim = true; // Deogen always knows & slow loop
                        if (traitFreezingBreath && ghost.ID != "Hantu" && ghost.ID != "The Mimic") elim = true; // Freezing breath = Hantu
                    }

                    // 5. Speed Check
                    if (!elim && _lastCalculatedSpeed > 0 && _currentSpeedCategory != "None")
                    {
                        if (_currentSpeedCategory == "Impossible")
                        {
                            elim = true; // Speed > 4.0 m/s is impossible for all ghosts
                        }
                        else
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

                            if (!elim)
                            {
                                double margin = 0.20;
                                if (_lastCalculatedSpeed < (ghost.MinSpeed - margin) || _lastCalculatedSpeed > (ghost.MaxSpeed + margin))
                                {
                                    elim = true;
                                }
                            }
                        }
                    }

                    ghost.IsEliminated = elim;
                }
            }

            UpdateDiscordStatus();
        }

        private static void AssignGhostTraits(Ghost g)
        {

            switch (g.ID)
            {
                case "Spirit":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "180s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Normal";
                    break;
                case "Wraith":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = false; g.SanityThresholdCategory = "Normal";
                    break;
                case "Phantom":
                    g.BlinkRate = "Slow"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Normal";
                    break;
                case "Poltergeist":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Normal";
                    break;
                case "Banshee":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Normal";
                    break;
                case "Jinn":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Normal";
                    break;
                case "Mare":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Early";
                    break;
                case "Revenant":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Normal";
                    break;
                case "Shade":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Late";
                    break;
                case "Demon":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "60s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Early";
                    break;
                case "Yurei":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Normal";
                    break;
                case "Oni":
                    g.BlinkRate = "Fast"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Normal";
                    break;
                case "Yokai":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Early";
                    break;
                case "Hantu":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Normal";
                    break;
                case "Goryo":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Normal";
                    break;
                case "Myling":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Normal";
                    break;
                case "Onryo":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Early";
                    break;
                case "TheTwins":
                case "The Twins":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Normal";
                    break;
                case "Raiju":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Early";
                    break;
                case "Obake":
                    g.BlinkRate = "Shapeshift"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Normal";
                    break;
                case "TheMimic":
                case "The Mimic":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Any";
                    break;
                case "Moroi":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Normal";
                    break;
                case "Deogen":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Late";
                    break;
                case "Thaye":
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Early";
                    break;
                default:
                    g.BlinkRate = "Normal"; g.SmudgeCooldown = "90s"; g.StepsInSalt = true; g.SanityThresholdCategory = "Normal";
                    break;
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

        // 1. Handles the Discord Toggle in the Right-Click Menu
        private void MenuDiscord_Click(object sender, RoutedEventArgs e)
        {
            _config.EnableDiscord = MenuDiscord.IsChecked;
            _config.Save();

            // If checked, turn on. If unchecked, turn off.
            if (MenuDiscord.IsChecked)
            {
                App.DiscordRpc?.Initialize();
                UpdateDiscordStatus();
            }
            else
            {
                App.DiscordRpc?.ClearPresence();
            }
        }

        private void BtnStopSmudge_Click(object sender, RoutedEventArgs e) => StopSmudgeTimer();
        private void BtnResetSmudge_Click(object sender, RoutedEventArgs e) => ResetSmudgeTimer();
        private void BtnResetPace_Click(object sender, RoutedEventArgs e) => ResetPace();
        private void CmbSpeed_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbSpeed.SelectedIndex == 0) _speedMultiplier = 0.5; 
            else if (CmbSpeed.SelectedIndex == 1) _speedMultiplier = 0.75; 
            else if (CmbSpeed.SelectedIndex == 2) _speedMultiplier = 1.0; 
            else if (CmbSpeed.SelectedIndex == 3) _speedMultiplier = 1.15; 
            else if (CmbSpeed.SelectedIndex == 4) _speedMultiplier = 1.25; 
            else if (CmbSpeed.SelectedIndex == 5) _speedMultiplier = 1.5;
        }

        // --- TIMER LOGIC ---
        private void StartSmudgeTimer() { _timerSecondsRemaining = 180; ProgressTimer.Value = 180; _smudgeTimer.Start(); UpdateTimerDisplay(); }
        private void ResetSmudgeTimer() { StartSmudgeTimer(); }
        private void StopSmudgeTimer()
        {
            _smudgeTimer.Stop();
            string ready = _currentLangData?.UI.GetValueOrDefault("Ready", "READY") ?? "READY";
            string wait = _currentLangData?.UI.GetValueOrDefault("Waiting", "WAITING...") ?? "WAITING...";
            TxtTimer.Text = ready; TxtMiniTimer.Text = ready; TxtIntelTimer.Text = ready;
            TxtTimerStatus.Text = wait; TxtMiniStatus.Text = wait; TxtIntelStatus.Text = wait;
            TxtTimer.Foreground = Brushes.White; TxtTimerStatus.Foreground = Brushes.Gray;
            TxtMiniStatus.Foreground = Brushes.LightGreen; TxtIntelStatus.Foreground = Brushes.LightGreen;
            ProgressTimer.Value = 180;
        }

        private void SmudgeTimer_Tick(object sender, EventArgs e)
        {
            _timerSecondsRemaining--; ProgressTimer.Value = _timerSecondsRemaining; UpdateTimerDisplay();
            if (_timerSecondsRemaining == 120) PlayAudioCue("demon");
            if (_timerSecondsRemaining == 90) PlayAudioCue("normal");
            if (_timerSecondsRemaining <= 0) { _smudgeTimer.Stop(); UpdateTimerDisplay(); PlayAudioCue("smudge_end"); }
        }

        private void UpdateTimerDisplay()
        {
            if (!_smudgeTimer.IsEnabled) return;
            string timeText, statusText; Brush colorBrush, statusBrush;
            string safeTxt = _currentLangData?.UI.GetValueOrDefault("Safe", "SAFE") ?? "SAFE";
            string demonTxt = _currentLangData?.UI.GetValueOrDefault("Demon", "DEMON") ?? "DEMON";
            string ready = _currentLangData?.UI.GetValueOrDefault("Ready", "READY") ?? "READY";

            if (_timerSecondsRemaining <= 0) { timeText = ready; statusText = "HUNT!"; colorBrush = Brushes.Red; statusBrush = Brushes.Red; }
            else
            {
                TimeSpan t = TimeSpan.FromSeconds(_timerSecondsRemaining); timeText = string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds);
                if (_timerSecondsRemaining > 120) { colorBrush = Brushes.White; statusText = safeTxt; statusBrush = Brushes.LightGreen; }
                else if (_timerSecondsRemaining <= 120 && _timerSecondsRemaining > 90) { colorBrush = (Brush)new BrushConverter().ConvertFrom("#FF6666")!; statusText = demonTxt; statusBrush = Brushes.Orange; }
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
            if (_huntTimerSeconds <= 0) { _huntTimer.Stop(); PlayAudioCue("crucifix_end"); TxtHuntTimer.Foreground = Brushes.Red; }
        }

        private void UpdateHuntTimerDisplay()
        {
            TimeSpan t = TimeSpan.FromSeconds(_huntTimerSeconds); string txt = string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds); Brush c = Brushes.White;
            if (_huntTimerSeconds > 5) c = Brushes.LightGreen; else if (_huntTimerSeconds > 0) c = Brushes.Orange; else c = Brushes.Red;
            TxtHuntTimer.Text = txt; TxtHuntTimer.Foreground = c; TxtMiniHuntTimer.Text = txt; TxtMiniHuntTimer.Foreground = c; TxtIntelHuntTimer.Text = txt; TxtIntelHuntTimer.Foreground = c;
        }

        private void PlayAudioCue(string type)
        {
            Task.Run(() =>
            {
                try
                {
                    double vol = _config?.Volume ?? 0.3;
                    if (vol <= 0.001) return;

                    string customPath = "";
                    if (type == "smudge_end" || type == "spirit") customPath = _config?.CustomSoundSmudgeEnd ?? "";
                    else if (type == "crucifix_end") customPath = _config?.CustomSoundCrucifixEnd ?? "";
                    else if (type == "hunt_end") customPath = _config?.CustomSoundHuntEnd ?? "";
                    else if (type == "demon") customPath = _config?.CustomSoundDemonAlert ?? "";
                    else if (type == "normal") customPath = _config?.CustomSoundAlert ?? "";

                    SoundHelper.PlaySoundFileOrBeep(customPath, type, vol);
                }
                catch { }
            });
        }

        private void MenuSoundSmudge_Click(object sender, RoutedEventArgs e) => PickSoundFile("Smudge End Sound", path => _config.CustomSoundSmudgeEnd = path, "smudge_end");
        private void MenuSoundCrucifix_Click(object sender, RoutedEventArgs e) => PickSoundFile("Crucifix End Sound", path => _config.CustomSoundCrucifixEnd = path, "crucifix_end");
        private void MenuSoundHuntEnd_Click(object sender, RoutedEventArgs e) => PickSoundFile("Active Hunt End Sound", path => _config.CustomSoundHuntEnd = path, "hunt_end");
        private void MenuSoundDemon_Click(object sender, RoutedEventArgs e) => PickSoundFile("Demon Warning Sound", path => _config.CustomSoundDemonAlert = path, "demon");

        private void PickSoundFile(string title, Action<string> applyPath, string cueType)
        {
            var ofd = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Select {title} (.wav, .mp3, .ogg)",
                Filter = "Audio Files (*.wav;*.mp3;*.ogg;*.wma)|*.wav;*.mp3;*.ogg;*.wma|All Files (*.*)|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                applyPath(ofd.FileName);
                _config.Save();
                PlayAudioCue(cueType);
                MessageBox.Show($"{title} set to:\n{System.IO.Path.GetFileName(ofd.FileName)}", "Sound Updated", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void MenuSoundTest_Click(object sender, RoutedEventArgs e)
        {
            Task.Run(() =>
            {
                PlayAudioCue("demon");
                Thread.Sleep(800);
                PlayAudioCue("normal");
                Thread.Sleep(800);
                PlayAudioCue("hunt_end");
                Thread.Sleep(800);
                PlayAudioCue("smudge_end");
            });
        }

        private void MenuSoundReset_Click(object sender, RoutedEventArgs e)
        {
            _config.CustomSoundSmudgeEnd = "";
            _config.CustomSoundCrucifixEnd = "";
            _config.CustomSoundHuntEnd = "";
            _config.CustomSoundDemonAlert = "";
            _config.CustomSoundAlert = "";
            _config.Save();
            PlayAudioCue("normal");
            MessageBox.Show("All custom sounds have been reset to default synthesized beeps!", "Sounds Reset", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void UpdateVolumeMenuCheckedState()
        {
            double v = _config?.Volume ?? 0.3;
            if (MenuVolMute != null) MenuVolMute.IsChecked = (v <= 0.05);
            if (MenuVol20 != null) MenuVol20.IsChecked = (v > 0.05 && v <= 0.30);
            if (MenuVol40 != null) MenuVol40.IsChecked = (v > 0.30 && v <= 0.55);
            if (MenuVol70 != null) MenuVol70.IsChecked = (v > 0.55 && v <= 0.85);
            if (MenuVol100 != null) MenuVol100.IsChecked = (v > 0.85);
        }

        private void MenuVolume_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag != null && double.TryParse(item.Tag.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double vol))
            {
                _config.Volume = vol;
                _config.Save();
                UpdateVolumeMenuCheckedState();
                if (vol > 0.001)
                {
                    Task.Run(() => SoundHelper.PlayBeep(600, 150, vol));
                }
            }
        }

        // --- HELPER METHODS ---

        // --- DISCORD RPC LOGIC ---
        private void UpdateDiscordStatus()
        {
            if (MenuDiscord != null && !MenuDiscord.IsChecked) return;

            try
            {
                // 1. Get Logic Data
                var suspects = AllGhosts.Where(g => !g.IsEliminated).ToList();
                int remainingCount = suspects.Count;
                int totalCount = AllGhosts.Count;

                // 2. Get Translated Labels (Safety check: Use English defaults if JSON is missing keys)
                string lblID = _currentLangData?.UI.GetValueOrDefault("RpcID", "ID") ?? "ID";
                string lblSuspects = _currentLangData?.UI.GetValueOrDefault("RpcSuspects", "Suspects") ?? "Suspects";
                string lblNoMatch = _currentLangData?.UI.GetValueOrDefault("RpcNoMatch", "No Match") ?? "No Match";
                string lblChasing = _currentLangData?.UI.GetValueOrDefault("RpcChasing", "Chasing") ?? "Chasing";
                string lblSearch = _currentLangData?.UI.GetValueOrDefault("RpcSearch", "Searching...") ?? "Searching...";
                string lblFound = _currentLangData?.UI.GetValueOrDefault("RpcFound", "Found") ?? "Found";

                // 3. Build the Text
                string topText;
                string bottomText;

                // --- TOP LINE (Status) ---
                if (remainingCount == 1)
                {
                    // E.g., "FANTASMA: DEMON" or "IDENTIFIZIERT: DEMON"
                    topText = _activeTabMode == 1 
                        ? $"👻 {lblID}: {suspects[0].Name.ToUpper()} (0-Ev)" 
                        : $"👻 {lblID}: {suspects[0].Name.ToUpper()}";
                }
                else if (remainingCount == 0)
                {
                    topText = $"❌ {lblNoMatch}";
                }
                else
                {
                    // E.g., "Suspeitos: 4/24" or "0-Ev Suspects: 4/24"
                    topText = _activeTabMode == 1 
                        ? $"👁️ 0-Ev {lblSuspects}: {remainingCount}/{totalCount}" 
                        : $"{lblSuspects}: {remainingCount}/{totalCount}";
                }

                // --- BOTTOM LINE (Action) ---
                if (_lastCalculatedSpeed > 0 && _currentSpeedCategory != "None")
                {
                    // E.g., "Fugindo: 2.5 m/s (Fast)"
                    bottomText = $"🏃 {lblChasing}: {_lastCalculatedSpeed:F1} m/s ({_currentSpeedCategory})";
                }
                else if (_activeTabMode == 1)
                {
                    bottomText = "⚡ Testing Behaviors & Speeds";
                }
                else
                {
                    var foundEv = _evidenceCheckBoxes
                                    .Where(c => c.IsChecked == true)
                                    .Select(c => c.Content.ToString()) // This is ALREADY translated by LoadLanguage!
                                    .ToList();

                    if (foundEv.Count == 0)
                    {
                        bottomText = $"🔍 {lblSearch}";
                    }
                    else
                    {
                        // E.g., "Encontrado: EMF 5, UV"
                        bottomText = $"🔎 {lblFound}: " + string.Join(", ", foundEv);
                    }
                }

                // 4. Send to RPC
                string lblDownload = _currentLangData?.UI.GetValueOrDefault("RpcBtnDownload", "Download Overlay") ?? "Download Overlay";
                string lblCoffee = _currentLangData?.UI.GetValueOrDefault("RpcBtnCoffee", "Buy me a Coffee ☕") ?? "Buy me a Coffee ☕";
                App.DiscordRpc?.SetStatus(topText, bottomText, lblDownload, lblCoffee);
            }
            catch { /* Ignore */ }
        }
        private void CheckKey(int key, Action action)
        {
            bool isDown = (GetAsyncKeyState(key) & 0x8000) != 0;
            if (!_keyStateTracker.ContainsKey(key)) _keyStateTracker[key] = false;

            if (isDown && !_keyStateTracker[key])
            {
                _keyStateTracker[key] = true;
                // CHANGED PRIORITY TO 'Normal' (Smoother)
                Dispatcher.InvokeAsync(action, DispatcherPriority.Normal);
            }
            else if (!isDown && _keyStateTracker[key])
            {
                _keyStateTracker[key] = false;
            }
        }
        protected override void OnClosed(EventArgs e) { _cancellationTokenSource.Cancel(); base.OnClosed(e); }
        private void BtnDonate_Click(object sender, RoutedEventArgs e) { try { Process.Start(new ProcessStartInfo { FileName = "https://ko-fi.com/hopesan", UseShellExecute = true }); } catch { } }
        private void Window_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }
        private void MenuEn_Click(object sender, RoutedEventArgs e) => ChangeLanguage("en");
        private void MenuPt_Click(object sender, RoutedEventArgs e) => ChangeLanguage("pt");
        private void MenuJp_Click(object sender, RoutedEventArgs e) => ChangeLanguage("jp");
        private void MenuCht_Click(object sender, RoutedEventArgs e) => ChangeLanguage("cht");
        private void MenuDe_Click(object sender, RoutedEventArgs e) => ChangeLanguage("de");
        private void MenuEs_Click(object sender, RoutedEventArgs e) => ChangeLanguage("es");
        private void MenuRu_Click(object sender, RoutedEventArgs e) => ChangeLanguage("ru");
        private void MenuNl_Click(object sender, RoutedEventArgs e) => ChangeLanguage("nl");
        private void MenuUk_Click(object sender, RoutedEventArgs e) => ChangeLanguage("uk");
        private void MenuFr_Click(object sender, RoutedEventArgs e) => ChangeLanguage("fr");
        private void MenuOpacity_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is string tagStr && double.TryParse(tagStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double op))
            {
                this.Opacity = op;
                _config.Opacity = op;
                _config.Save();
            }
        }
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

        // GUARANTEED EVIDENCE
        public static string GuaranteedPrefix { get; set; } = "Guaranteed";
        public string Guaranteed { get; set; }
        public string GuaranteedText => !string.IsNullOrEmpty(Guaranteed) ? $"⚠ {GuaranteedPrefix}: {Guaranteed}" : "";
        public bool HasGuaranteed => !string.IsNullOrEmpty(Guaranteed);

        // ZERO EVIDENCE DATA
        public string ZeroEvidenceTest { get; set; }
        public string BlinkRate { get; set; } = "Normal";
        public string SmudgeCooldown { get; set; } = "90s";
        public bool StepsInSalt { get; set; } = true;
        public string SanityThresholdCategory { get; set; } = "Normal";
        public List<string> BehavioralTags { get; set; } = new List<string>();

        private bool _isManuallyEliminated;
        public bool IsManuallyEliminated
        {
            get => _isManuallyEliminated;
            set { _isManuallyEliminated = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsManuallyEliminated")); }
        }

        private bool _isEliminated;
        public bool IsEliminated
        {
            get => _isEliminated;
            set { _isEliminated = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsEliminated")); }
        }

        // --- OPTIMIZATION START: Cache the colors so we don't calculate them every frame ---
        private Brush _cachedPrimaryColor;
        private Brush _cachedGradient;

        public Brush PrimaryColorBrush => _cachedPrimaryColor;
        public Brush EvidenceGradient => _cachedGradient;

        public Ghost(string n, string s, params string[] e)
        {
            Name = n; Symbol = s; Evidences = e.ToList();
            InitializeColors(); // Calculate colors ONCE upon creation
        }
        // --- OPTIMIZATION END ---

        private void InitializeColors()
        {
            // 1. Calculate Primary Color
            string colorHex = "#FFFFFF";
            foreach (var ev in Evidences)
            {
                if (IsType(ev, "Freezing")) { colorHex = "#00CED1"; break; }
                if (IsType(ev, "EMF")) { colorHex = "#DC143C"; break; }
                if (IsType(ev, "DOTS")) { colorHex = "#39FF14"; break; }
                if (IsType(ev, "UV")) { colorHex = "#8A2BE2"; break; }
                if (IsType(ev, "SpiritBox")) { colorHex = "#FF4500"; break; }
                if (IsType(ev, "Writing")) { colorHex = "#FFD700"; break; }
                if (IsType(ev, "Orb")) { colorHex = "#E0FFFF"; break; }
            }
            _cachedPrimaryColor = (Brush)new BrushConverter().ConvertFrom(colorHex);
            if (_cachedPrimaryColor.CanFreeze) _cachedPrimaryColor.Freeze(); // Lock it for performance

            // 2. Calculate Gradient
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

            _cachedGradient = new LinearGradientBrush(stops, new Point(0, 0), new Point(1, 0)) { Opacity = 0.9 };
            if (_cachedGradient.CanFreeze) _cachedGradient.Freeze(); // Lock it
        }



        // Inside the Ghost class (at the bottom of your .cs file)

        private bool IsType(string ev, string type)
        {
            // 1. EMF 5
            if (type == "EMF")
                return ev.Contains("EMF") || ev.Contains("ЭМП") || ev.Contains("Niveau 5") || ev.Contains("ЕМП");

            // 2. Ultraviolet (UV)
            if (type == "UV")
                return ev.Contains("Ultraviolet") || ev.Contains("Violet") || ev.Contains("Finger") ||
                       ev.Contains("Digital") || ev.Contains("指") || ev.Contains("紫") ||
                       ev.Contains("紫外線") || ev.Contains("Ультрафіолет") ||
                       ev.Contains("Ультрафиолет") || ev.Contains("Ultraviolett");

            // 3. Freezing Temperatures
            if (type == "Freezing")
                return ev.Contains("Freezing") || ev.Contains("Gelado") || ev.Contains("Baixa") ||
                       ev.Contains("氷") || ev.Contains("寒") || ev.Contains("冷") || ev.Contains("低") ||
                       ev.Contains("Минусовая") || ev.Contains("Мінусова") ||
                       ev.Contains("Gefrier") || ev.Contains("Heladas") ||
                       ev.Contains("Negativas") || ev.Contains("Vries") || ev.Contains("Temp. Heladas");

            // 4. Spirit Box
            if (type == "SpiritBox")
                return ev.Contains("Spirit") || ev.Contains("Box") || ev.Contains("BOX") ||
                       ev.Contains("通灵") || ev.Contains("通靈") || ev.Contains("盒") ||
                       ev.Contains("スピリット") || ev.Contains("Радио") || ev.Contains("Радіо") || 
                       ev.Contains("Geisterbox");

            // 5. Ghost Orb
            if (type == "Orb")
                return ev.Contains("Orb") || ev.Contains("Orbe") || ev.Contains("灵球") ||
                       ev.Contains("靈球") || ev.Contains("オーブ") || ev.Contains("玉") ||
                       ev.Contains("огонёк") || ev.Contains("вогник") ||
                       ev.Contains("Geestbal") || ev.Contains("Geisterorb");

            // 6. Ghost Writing
            if (type == "Writing")
                return ev.Contains("Writing") || ev.Contains("Escrita") || ev.Contains("Escritura") ||
                       ev.Contains("笔") || ev.Contains("筆") || ev.Contains("本") ||
                       ev.Contains("ライティング") || ev.Contains("Блокнот") ||
                       ev.Contains("Buch") || ev.Contains("Geestboek") || ev.Contains("Geisterbuch");

            // 7. D.O.T.S.
            if (type == "DOTS")
                return ev.Contains("D.O.T.S.") || ev.Contains("DOTS") || ev.Contains("Проектор") || ev.Contains("點陣");

            return false;
        }

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
    public class EvidenceColorConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            string ev = value as string ?? "";

            // 1. EMF 5 (Red)
            if (ev.Contains("EMF") || ev.Contains("ЭМП") || ev.Contains("Niveau 5") ||
                ev.Contains("ЕМП"))
                return new SolidColorBrush(Color.FromRgb(220, 20, 60));

            // 2. Ultraviolet (Violet)
            if (ev.Contains("Ultraviolet") || ev.Contains("Violet") || ev.Contains("Finger") || ev.Contains("Digital") ||
                ev.Contains("Ультрафиолет") || ev.Contains("指") || ev.Contains("紫") || ev.Contains("Ultraviolett") ||
                ev.Contains("Ультрафіолет"))
                return new SolidColorBrush(Color.FromRgb(138, 43, 226));

            // 3. Freezing Temps (Cyan)
            if (ev.Contains("Freezing") || ev.Contains("Gelado") || ev.Contains("Baixa") ||
                ev.Contains("Минусовая") || ev.Contains("Gefrier") || ev.Contains("Heladas") || ev.Contains("Negativas") ||
                ev.Contains("Vries") || ev.Contains("氷") || ev.Contains("寒") || ev.Contains("冷") ||
                ev.Contains("Мінусова"))
                return new SolidColorBrush(Color.FromRgb(0, 206, 209));

            // 4. Spirit Box (Orange)
            if (ev.Contains("Spirit") || ev.Contains("Box") || ev.Contains("BOX") ||
                ev.Contains("Geisterbox") || ev.Contains("Радио") || ev.Contains("通灵") || ev.Contains("通靈") || ev.Contains("盒") ||
                ev.Contains("Радіо"))
                return new SolidColorBrush(Color.FromRgb(255, 69, 0));

            // 5. Ghost Orbs (Light Cyan)
            if (ev.Contains("Orb") || ev.Contains("Orbe") || ev.Contains("огонёк") ||
                ev.Contains("Geestbal") || ev.Contains("灵球") || ev.Contains("靈球") || ev.Contains("オーブ") || ev.Contains("玉") ||
                ev.Contains("вогник"))
                return new SolidColorBrush(Color.FromRgb(224, 255, 255));

            // 6. Ghost Writing (Gold)
            if (ev.Contains("Writing") || ev.Contains("Escrita") || ev.Contains("Escritura") ||
                ev.Contains("Блокнот") || ev.Contains("Buch") ||
                ev.Contains("Geestboek") || ev.Contains("Geisterbuch") ||
                ev.Contains("笔") || ev.Contains("筆") || ev.Contains("本") || ev.Contains("ライティング") ||
                ev.Contains("Записи"))
                return new SolidColorBrush(Color.FromRgb(255, 215, 0));

            // 7. D.O.T.S. (Green)
            if (ev.Contains("D.O.T.S.") || ev.Contains("DOTS") || ev.Contains("Проектор") || ev.Contains("點陣"))
                return new SolidColorBrush(Color.FromRgb(57, 255, 20));

            return Brushes.LightGray;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotImplementedException();
    }

    public static class SoundHelper
    {
        public static void PlaySoundFileOrBeep(string filePath, string beepType, double volume)
        {
            if (volume <= 0.001) return;
            volume = Math.Clamp(volume, 0.0, 1.0);

            if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            {
                try
                {
                    if (filePath.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                    {
                        using var player = new System.Media.SoundPlayer(filePath);
                        player.PlaySync();
                        return;
                    }
                    else
                    {
                        Application.Current?.Dispatcher.Invoke(() =>
                        {
                            try
                            {
                                var player = new MediaPlayer();
                                player.Open(new Uri(filePath, UriKind.Absolute));
                                player.Volume = volume;
                                player.Play();
                            }
                            catch { }
                        });
                        return;
                    }
                }
                catch
                {
                    // Fallback to beep
                }
            }

            PlayBeepType(beepType, volume);
        }

        public static void PlayBeepType(string type, double volume)
        {
            if (type == "demon")
            {
                PlayBeep(260, 300, volume);
                Thread.Sleep(100);
                PlayBeep(260, 300, volume);
            }
            else if (type == "normal")
            {
                PlayBeep(520, 180, volume);
                Thread.Sleep(80);
                PlayBeep(520, 180, volume);
                Thread.Sleep(80);
                PlayBeep(520, 180, volume);
            }
            else if (type == "hunt_end")
            {
                PlayBeep(523, 160, volume);
                Thread.Sleep(40);
                PlayBeep(659, 160, volume);
                Thread.Sleep(40);
                PlayBeep(784, 320, volume);
            }
            else // "smudge_end", "crucifix_end", "spirit", or default
            {
                PlayBeep(880, 200, volume);
                Thread.Sleep(50);
                PlayBeep(880, 200, volume);
                Thread.Sleep(50);
                PlayBeep(880, 500, volume);
            }
        }

        public static void PlayBeep(int frequency, int durationMs, double volume)
        {
            if (volume <= 0.001) return;
            volume = Math.Clamp(volume, 0.0, 1.0);

            try
            {
                byte[] wavBytes = GenerateSineWaveWav(frequency, durationMs, volume);
                using var ms = new MemoryStream(wavBytes);
                using var player = new System.Media.SoundPlayer(ms);
                player.PlaySync();
            }
            catch
            {
                // Ignore sound errors
            }
        }

        private static byte[] GenerateSineWaveWav(int frequency, int durationMs, double volume)
        {
            int sampleRate = 44100;
            int numSamples = (int)(sampleRate * (durationMs / 1000.0));
            int dataSize = numSamples * 2; // 16-bit mono = 2 bytes per sample

            byte[] buffer = new byte[44 + dataSize];
            using var ms = new MemoryStream(buffer);
            using var writer = new BinaryWriter(ms);

            // RIFF header
            writer.Write(new char[4] { 'R', 'I', 'F', 'F' });
            writer.Write((int)(36 + dataSize));
            writer.Write(new char[4] { 'W', 'A', 'V', 'E' });

            // fmt chunk
            writer.Write(new char[4] { 'f', 'm', 't', ' ' });
            writer.Write((int)16); // PCM chunk size
            writer.Write((short)1); // PCM format
            writer.Write((short)1); // Mono
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2); // Byte rate (SampleRate * NumChannels * BitsPerSample/8)
            writer.Write((short)2); // Block align (NumChannels * BitsPerSample/8)
            writer.Write((short)16); // Bits per sample

            // data chunk
            writer.Write(new char[4] { 'd', 'a', 't', 'a' });
            writer.Write(dataSize);

            // Sine wave generation with fade in and fade out (5ms)
            int fadeSamples = Math.Min(sampleRate * 5 / 1000, Math.Max(1, numSamples / 4));
            double maxAmp = short.MaxValue * volume * 0.7; // Headroom to prevent clipping distortion

            for (int i = 0; i < numSamples; i++)
            {
                double env = 1.0;
                if (i < fadeSamples)
                {
                    env = (double)i / fadeSamples;
                }
                else if (i > numSamples - fadeSamples)
                {
                    env = (double)(numSamples - i) / fadeSamples;
                }

                double angle = 2.0 * Math.PI * frequency * ((double)i / sampleRate);
                short sample = (short)(Math.Sin(angle) * maxAmp * env);
                writer.Write(sample);
            }

            return buffer;
        }
    }
}