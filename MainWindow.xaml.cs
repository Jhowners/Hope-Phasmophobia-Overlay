using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
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
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

        private const int VK_MENU = 0x12;
        private const int VK_F6 = 0x75; private const int VK_F7 = 0x76;
        private const int VK_F8 = 0x77; private const int VK_F9 = 0x78;
        private const int VK_F10 = 0x79; private const int VK_F11 = 0x7A;
        private const int VK_INSERT = 0x2D; private const int VK_HOME = 0x24;

        private CancellationTokenSource _cancellationTokenSource;
        private Dictionary<int, bool> _keyStateTracker = new Dictionary<int, bool>();

        public List<Ghost> AllGhosts { get; set; }
        private List<CheckBox> _evidenceCheckBoxes = new List<CheckBox>();
        private DispatcherTimer _smudgeTimer;
        private int _timerSecondsRemaining = 180;
        private DispatcherTimer _huntTimer;
        private int _huntTimerSeconds = 25;
        private List<DateTime> _tapHistory = new List<DateTime>();
        private double _speedMultiplier = 1.0;
        private int _currentView = 0;
        private string _currentLang = "EN";
        private string _currentSpeedCategory = "None";

        private readonly List<string> _fastGhosts = new List<string> { "Jinn", "Revenant", "Hantu", "The Twins", "Raiju", "Moroi", "Deogen", "Thaye", "The Mimic" };
        private readonly List<string> _slowGhosts = new List<string> { "Revenant", "Hantu", "Deogen", "Thaye", "The Mimic", "The Twins", "Moroi" };

        public MainWindow()
        {
            InitializeComponent();
            Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.AboveNormal;

            _evidenceCheckBoxes.Add(ChkEv1); _evidenceCheckBoxes.Add(ChkEv2); _evidenceCheckBoxes.Add(ChkEv3);
            _evidenceCheckBoxes.Add(ChkEv4); _evidenceCheckBoxes.Add(ChkEv5); _evidenceCheckBoxes.Add(ChkEv6); _evidenceCheckBoxes.Add(ChkEv7);

            InitializeGhosts("EN");
            GhostsListControl.ItemsSource = AllGhosts;
            GhostsIntelControl.ItemsSource = AllGhosts;

            _smudgeTimer = new DispatcherTimer(DispatcherPriority.Render);
            _smudgeTimer.Interval = TimeSpan.FromSeconds(1);
            _smudgeTimer.Tick += SmudgeTimer_Tick;

            _huntTimer = new DispatcherTimer(DispatcherPriority.Render);
            _huntTimer.Interval = TimeSpan.FromSeconds(1);
            _huntTimer.Tick += HuntTimer_Tick;

            _cancellationTokenSource = new CancellationTokenSource();
            Task.Factory.StartNew(() => InputLoop(_cancellationTokenSource.Token), TaskCreationOptions.LongRunning);
        }

        private void InputLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                CheckKey(VK_F6, () => StopHuntTimer());
                CheckKey(VK_F7, () => ResetHuntTimer());
                CheckKey(VK_F8, () => StopSmudgeTimer());
                CheckKey(VK_F9, () => ResetSmudgeTimer());
                CheckKey(VK_F10, () => CalculateBPM());
                CheckKey(VK_F11, () => ResetPace());
                CheckKey(VK_INSERT, () => SetViewMode(1));
                CheckKey(VK_HOME, () => SetViewMode(2));

                bool isAltDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                if (isAltDown)
                {
                    for (int i = 1; i <= 8; i++)
                    {
                        int vKey = 0x30 + i;
                        int idx = i;
                        CheckKey(vKey, () =>
                        {
                            if (idx == 8) ResetEvidence();
                            else ToggleEvidenceByIndex(idx);
                        });
                    }
                }
                Thread.Sleep(10);
            }
        }

        private void CheckKey(int key, Action action)
        {
            bool isDown = (GetAsyncKeyState(key) & 0x8000) != 0;
            if (!_keyStateTracker.ContainsKey(key)) _keyStateTracker[key] = false;

            if (isDown && !_keyStateTracker[key])
            {
                _keyStateTracker[key] = true;
                Dispatcher.InvokeAsync(action, DispatcherPriority.Send);
            }
            else if (!isDown && _keyStateTracker[key])
            {
                _keyStateTracker[key] = false;
            }
        }

        protected override void OnClosed(EventArgs e) { _cancellationTokenSource.Cancel(); base.OnClosed(e); }

        // --- UI LOGIC ---
        private void BtnDonate_Click(object sender, RoutedEventArgs e) { try { Process.Start(new ProcessStartInfo { FileName = "https://ko-fi.com/hopesan", UseShellExecute = true }); } catch { } }
        private void Window_MouseDown(object sender, MouseButtonEventArgs e) { if (e.ChangedButton == MouseButton.Left) DragMove(); }

        private void MenuEn_Click(object sender, RoutedEventArgs e) => SetLanguage("EN");
        private void MenuPt_Click(object sender, RoutedEventArgs e) => SetLanguage("PT");
        private void MenuExit_Click(object sender, RoutedEventArgs e) { Application.Current.Shutdown(); }

        // --- LANGUAGE LOGIC ---
        private void SetLanguage(string lang)
        {
            _currentLang = lang;
            InitializeGhosts(lang);
            GhostsListControl.ItemsSource = null; GhostsListControl.ItemsSource = AllGhosts;
            GhostsIntelControl.ItemsSource = null; GhostsIntelControl.ItemsSource = AllGhosts;

            if (lang == "PT")
            {
                LblControls.Text = "[INS] MINI | [HOME] INTEL | [ALT+8] RESETAR";
                LblEvidence.Text = "EVIDÊNCIA (ALT+1-7)";
                LblSmudge.Text = "INCENSO [F9]";
                LblHunt.Text = "CAÇADA [F7]";
                LblSpeed.Text = "VELOCIDADE (m/s)";
                LblMiniSmudge.Text = "INCENSO";
                LblMiniHunt.Text = "CAÇADA";
                LblIntelSmudge.Text = "INCENSO";
                LblIntelHunt.Text = "CAÇADA";
                LblIntelSpeed.Text = "VELOC.";
                LblIntelEvidence.Text = "EVIDÊNCIA MARCADA";
                LblIntelTargets.Text = "FANTASMAS POSSÍVEIS";
                TxtGhostSpeedGuess.Text = "Toque no ritmo dos passos";
                ChkEv1.Content = "EMF Nível 5";
                ChkEv2.Content = "Spirit Box";
                ChkEv3.Content = "Impressão Digital";
                ChkEv4.Content = "Orbe Fantasma";
                ChkEv5.Content = "Escrita Fantasma";
                ChkEv6.Content = "Temperatura Baixa";
                ChkEv7.Content = "D.O.T.S.";
            }
            else
            {
                LblControls.Text = "[INS] MINI | [HOME] INTEL | [ALT+8] RESET";
                LblEvidence.Text = "EVIDENCE (ALT+1-7)";
                LblSmudge.Text = "SMUDGE [F9]";
                LblHunt.Text = "HUNT CD [F7]";
                LblSpeed.Text = "SPEED (m/s)";
                LblMiniSmudge.Text = "SMUDGE";
                LblMiniHunt.Text = "HUNT CD";
                LblIntelSmudge.Text = "SMUDGE";
                LblIntelHunt.Text = "HUNT CD";
                LblIntelSpeed.Text = "SPEED";
                LblIntelEvidence.Text = "MARKED EVIDENCE";
                LblIntelTargets.Text = "POSSIBLE TARGETS";
                TxtGhostSpeedGuess.Text = "Select Speed & Tap";
                ChkEv1.Content = "EMF Level 5";
                ChkEv2.Content = "Spirit Box";
                ChkEv3.Content = "Fingerprints";
                ChkEv4.Content = "Ghost Orb";
                ChkEv5.Content = "Ghost Writing";
                ChkEv6.Content = "Freezing";
                ChkEv7.Content = "D.O.T.S.";
            }
            UpdateGhostFiltering();
        }

        private void SetViewMode(int mode)
        {
            if (_currentView == mode) mode = 0; _currentView = mode;
            MainJournal.Visibility = Visibility.Collapsed; MiniHud.Visibility = Visibility.Collapsed; IntelHud.Visibility = Visibility.Collapsed;
            if (_currentView == 0) MainJournal.Visibility = Visibility.Visible;
            else if (_currentView == 1) MiniHud.Visibility = Visibility.Visible;
            else if (_currentView == 2) IntelHud.Visibility = Visibility.Visible;
        }

        private void ResetEvidence()
        {
            foreach (var box in _evidenceCheckBoxes) { box.IsChecked = false; }
            UpdateGhostFiltering();
        }

        private void ResetPace()
        {
            _tapHistory.Clear();
            string txt = "-- m/s"; TxtBPM.Text = txt; TxtIntelBPM.Text = txt;
            TxtGhostSpeedGuess.Text = (_currentLang == "PT") ? "Toque no ritmo dos passos" : "Select Speed & Tap";
            TxtIntelSpeedGuess.Text = (_currentLang == "PT") ? "Aguardando" : "Waiting";
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
                    int rawBpm = (int)(60 / avgInterval); double ms = (rawBpm * 0.0148) / _speedMultiplier;
                    string speedText = $"{ms:F1} m/s"; TxtBPM.Text = speedText; TxtIntelBPM.Text = speedText; InterpretSpeed(ms);
                }
            }
            else { TxtBPM.Text = "Tap..."; TxtIntelBPM.Text = "Tap..."; }
        }

        // ... (Imports and Variables same as before) ...

        // --- PRECISION SPEED LOGIC (V22.0 UPDATE) ---
        private void InterpretSpeed(double ms)
        {
            string guess = "";
            Brush c = Brushes.Gray;
            _currentSpeedCategory = "Normal";

            // DEOGEN (0.4 - 3.0)
            if (ms < 0.6)
            {
                guess = "Deogen (Very Close)"; c = Brushes.Cyan; _currentSpeedCategory = "Slow";
            }
            // REVENANT PASSIVE / THAYE OLD (1.0)
            else if (ms >= 0.8 && ms < 1.2)
            {
                guess = "Rev (Passive) / Thaye (Old)"; c = Brushes.Cyan; _currentSpeedCategory = "Slow";
            }
            // HANTU WARM / TWIN SLOW / MOROI HIGH SANITY (1.4 - 1.5)
            else if (ms >= 1.35 && ms < 1.55)
            {
                guess = "Twin (Slow 1.5) / Moroi (Hi-San) / Hantu (Warm)"; c = Brushes.LightGray; _currentSpeedCategory = "Slow";
            }
            // NORMAL RANGE (1.6 - 1.8)
            else if (ms >= 1.6 && ms <= 1.8)
            {
                guess = "Normal Speed (1.7 m/s)"; c = Brushes.White;
                _currentSpeedCategory = "Normal";
            }
            // TWIN FAST / MOROI MID / HANTU COOL (1.9 - 2.1)
            else if (ms > 1.85 && ms < 2.15)
            {
                guess = "Twin (Fast 1.9) / Hantu (Cool)"; c = Brushes.Orange; _currentSpeedCategory = "Fast";
            }
            // MOROI LOW SANITY / HANTU COLD / JINN / RAIJU (2.25 - 2.5)
            else if (ms >= 2.2 && ms < 2.6)
            {
                guess = "Raiju / Jinn / Hantu / Moroi"; c = Brushes.Red; _currentSpeedCategory = "Fast";
            }
            // REVENANT HUNT / THAYE YOUNG / HANTU FREEZING (2.7 - 3.0)
            else if (ms >= 2.65 && ms <= 3.1)
            {
                guess = "REV (Chase) / Thaye (Young) / Hantu / Moroi"; c = Brushes.Red; _currentSpeedCategory = "Fast";
            }
            // MOROI (LINE OF SIGHT BOOST) -> ONLY GHOST FASTER THAN 3.0
            else if (ms > 3.1 && ms < 4.0)
            {
                guess = "MOROI (Max LOS Speed)"; c = Brushes.Magenta; _currentSpeedCategory = "Fast";
            }
            // IMPOSSIBLE SPEED
            else if (ms >= 4.0)
            {
                guess = "Too Fast? (Check taps)"; c = Brushes.Gray; _currentSpeedCategory = "None";
            }

            // PT Translations
            if (_currentLang == "PT")
            {
                guess = guess.Replace("Very Close", "Muito Perto").Replace("Passive", "Passivo").Replace("Old", "Velho")
                             .Replace("Slow", "Lento").Replace("High Sanity", "Sanidade Alta").Replace("Hi-San", "San-Alta")
                             .Replace("Normal Speed", "Velocidade Normal").Replace("Fast", "Rápido").Replace("Cool", "Frio")
                             .Replace("Chase", "Caça").Replace("Young", "Jovem").Replace("Freezing", "Congelando")
                             .Replace("Too Fast?", "Muito Rápido?").Replace("Max LOS Speed", "Velocidade Máx");
            }

            TxtGhostSpeedGuess.Text = guess; TxtGhostSpeedGuess.Foreground = c;
            TxtIntelSpeedGuess.Text = guess; TxtIntelSpeedGuess.Foreground = c;
            TxtPacerStatus.Text = $"FILTER: {_currentSpeedCategory.ToUpper()}"; TxtPacerStatus.Foreground = c;
            UpdateGhostFiltering();
        }

        // ... (Rest of the file follows the logic from Version 20.0) ...

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

        // --- OPTIMIZED FILTERING ---
        private void UpdateGhostFiltering()
        {
            List<string> foundEv = new List<string>();
            List<string> ruledOutEv = new List<string>();

            foreach (var box in _evidenceCheckBoxes)
            {
                if (box.IsChecked == true) foundEv.Add(box.Content.ToString());
                if (box.IsChecked == null) ruledOutEv.Add(box.Content.ToString());
            }

            string TranslateEv(string ev)
            {
                if (_currentLang == "PT")
                {
                    return ev.Replace("EMF Level 5", "EMF 5").Replace("Spirit Box", "Spirit Box")
                             .Replace("Fingerprints", "Digitais").Replace("Ghost Orb", "Orbe")
                             .Replace("Ghost Writing", "Escrita").Replace("Freezing", "Gelado")
                             .Replace("D.O.T.S.", "DOTS");
                }
                return ev.Replace("Ghost ", "").Replace("Level ", "").Replace("Spirit ", "");
            }

            if (foundEv.Count > 0)
            {
                string foundStr = string.Join(" + ", foundEv.Select(x => TranslateEv(x)));
                TxtIntelFound.Text = foundStr; TxtIntelFound.Visibility = Visibility.Visible;
            }
            else if (ruledOutEv.Count > 0)
            {
                TxtIntelFound.Visibility = Visibility.Collapsed;
            }
            else
            {
                TxtIntelFound.Text = (_currentLang == "PT") ? "AGUARDANDO..." : "WAITING...";
                TxtIntelFound.Visibility = Visibility.Visible;
            }

            if (ruledOutEv.Count > 0)
            {
                string notStr = (_currentLang == "PT") ? "NÃO: " : "NOT: ";
                string ruledOutStr = notStr + string.Join(", ", ruledOutEv.Select(x => TranslateEv(x)));
                TxtIntelRuledOut.Text = ruledOutStr; TxtIntelRuledOut.Visibility = Visibility.Visible;
            }
            else
            {
                TxtIntelRuledOut.Visibility = Visibility.Collapsed;
            }

            foreach (var ghost in AllGhosts)
            {
                bool elim = false;
                foreach (var ev in foundEv)
                {
                    if (ghost.Name == "The Mimic" && (ev == "Ghost Orb" || ev == "Orbe Fantasma")) continue;
                    if (!ghost.Evidences.Contains(ev)) { elim = true; break; }
                }
                if (!elim)
                {
                    foreach (var ev in ruledOutEv)
                    {
                        if (ghost.Evidences.Contains(ev)) { elim = true; break; }
                        if (ghost.Name == "The Mimic" && (ev == "Ghost Orb" || ev == "Orbe Fantasma")) { elim = true; break; }
                    }
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
            _smudgeTimer.Stop();
            string ready = (_currentLang == "PT") ? "PRONTO" : "READY";
            string wait = (_currentLang == "PT") ? "AGUARDANDO" : "WAITING...";
            TxtTimer.Text = ready; TxtMiniTimer.Text = ready; TxtIntelTimer.Text = ready;
            TxtTimerStatus.Text = wait; TxtMiniStatus.Text = wait; TxtIntelStatus.Text = wait;
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
            if (!_smudgeTimer.IsEnabled && (TxtTimer.Text == "READY" || TxtTimer.Text == "PRONTO")) return;
            string timeText, statusText; Brush colorBrush, statusBrush;
            string readyTxt = (_currentLang == "PT") ? "ESPÍRITO!" : "SPIRIT!";
            string huntTxt = (_currentLang == "PT") ? "ATAQUE IMINENTE" : "HUNT IMMINENT";
            string safeTxt = (_currentLang == "PT") ? "SEGURO (TODOS)" : "SAFE (ALL)";
            string demonTxt = (_currentLang == "PT") ? "DEMON PERIGO" : "DEMON UNSAFE";
            string normTxt = (_currentLang == "PT") ? "NORMAL PERIGO" : "NORMAL UNSAFE";

            if (_timerSecondsRemaining <= 0) { timeText = readyTxt; statusText = huntTxt; colorBrush = Brushes.Red; statusBrush = Brushes.Red; }
            else
            {
                TimeSpan t = TimeSpan.FromSeconds(_timerSecondsRemaining); timeText = string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds);
                if (_timerSecondsRemaining > 120) { colorBrush = Brushes.White; statusText = safeTxt; statusBrush = Brushes.LightGreen; }
                else if (_timerSecondsRemaining <= 120 && _timerSecondsRemaining > 90) { colorBrush = (Brush)new BrushConverter().ConvertFrom("#FF6666"); statusText = demonTxt; statusBrush = Brushes.Orange; }
                else { colorBrush = Brushes.Red; statusText = normTxt; statusBrush = Brushes.Red; }
            }
            TxtTimer.Text = timeText; TxtTimer.Foreground = colorBrush; TxtTimerStatus.Text = statusText; TxtTimerStatus.Foreground = statusBrush;
            TxtMiniTimer.Text = timeText; TxtMiniTimer.Foreground = colorBrush; TxtMiniStatus.Text = statusText; TxtMiniStatus.Foreground = statusBrush;
            TxtIntelTimer.Text = timeText; TxtIntelTimer.Foreground = colorBrush; TxtIntelStatus.Text = statusText; TxtIntelStatus.Foreground = statusBrush;
        }

        private void ResetHuntTimer() { _huntTimerSeconds = 25; ProgressHuntTimer.Value = 25; _huntTimer.Start(); UpdateHuntTimerDisplay(); }
        private void StopHuntTimer()
        {
            _huntTimer.Stop();
            string ready = (_currentLang == "PT") ? "PRONTO" : "READY";
            string wait = (_currentLang == "PT") ? "AGUARDANDO" : "WAITING...";
            TxtHuntTimer.Text = ready; TxtMiniHuntTimer.Text = ready; TxtIntelHuntTimer.Text = ready; TxtHuntTimerStatus.Text = wait;
            TxtHuntTimer.Foreground = Brushes.White; TxtMiniHuntTimer.Foreground = Brushes.White; TxtIntelHuntTimer.Foreground = Brushes.White;
        }
        private void HuntTimer_Tick(object sender, EventArgs e)
        {
            _huntTimerSeconds--; ProgressHuntTimer.Value = _huntTimerSeconds; UpdateHuntTimerDisplay();
            if (_huntTimerSeconds <= 0) { _huntTimer.Stop(); PlayAudioCue("spirit"); TxtHuntTimer.Text = (_currentLang == "PT") ? "PRONTO" : "READY"; TxtMiniHuntTimer.Text = TxtHuntTimer.Text; TxtIntelHuntTimer.Text = TxtHuntTimer.Text; TxtHuntTimer.Foreground = Brushes.Red; }
        }
        private void UpdateHuntTimerDisplay()
        {
            TimeSpan t = TimeSpan.FromSeconds(_huntTimerSeconds); string txt = string.Format("{0:D2}:{1:D2}", t.Minutes, t.Seconds); Brush c = Brushes.White; string status = "SAFE";
            string safeTxt = (_currentLang == "PT") ? "SEGURO" : "SAFE";
            string demonTxt = "DEMON!";
            string readyTxt = (_currentLang == "PT") ? "PRONTO" : "READY";
            if (_huntTimerSeconds > 5) { c = Brushes.LightGreen; status = safeTxt; } else if (_huntTimerSeconds > 0) { c = (Brush)new BrushConverter().ConvertFrom("#FF6666"); status = demonTxt; } else { c = Brushes.Red; status = readyTxt; }
            TxtHuntTimer.Text = txt; TxtHuntTimer.Foreground = c; TxtMiniHuntTimer.Text = txt; TxtMiniHuntTimer.Foreground = c; TxtIntelHuntTimer.Text = txt; TxtIntelHuntTimer.Foreground = c; TxtHuntTimerStatus.Text = status; TxtHuntTimerStatus.Foreground = c;
        }

        private void PlayAudioCue(string type) { Task.Run(() => { try { if (type == "demon") { Console.Beep(200, 400); Thread.Sleep(100); Console.Beep(200, 400); } else if (type == "normal") { Console.Beep(500, 200); Thread.Sleep(100); Console.Beep(500, 200); Thread.Sleep(100); Console.Beep(500, 200); } else if (type == "spirit") { Console.Beep(1000, 300); Thread.Sleep(50); Console.Beep(1000, 300); Thread.Sleep(50); Console.Beep(1000, 800); } } catch { } }); }
        public static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject { if (depObj != null) for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++) { DependencyObject child = VisualTreeHelper.GetChild(depObj, i); if (child != null && child is T) yield return (T)child; foreach (T childOfChild in FindVisualChildren<T>(child)) yield return childOfChild; } }

        // --- COMPLETE GHOST DATABASE (V22.0) ---
        private void InitializeGhosts(string lang = "EN")
        {
            if (lang == "PT")
            {
                AllGhosts = new List<Ghost> {
                    new Ghost("Spirit", "👻", "EMF Nível 5", "Spirit Box", "Escrita Fantasma") { Tell = "Smudge previne caça por 180s (Normal 90s).", HuntThreshold = "< 50%" },
                    new Ghost("Wraith", "👣", "EMF Nível 5", "Spirit Box", "D.O.T.S.") { Tell = "Não pisa no sal. Teleporta para jogador (Gera EMF 2).", HuntThreshold = "< 50%" },
                    new Ghost("Phantom", "📷", "Spirit Box", "Impressão Digital", "D.O.T.S.") { Tell = "Some na foto. Pisca lento na caçada (Visível menos tempo).", HuntThreshold = "< 50%" },
                    new Ghost("Poltergeist", "💥", "Spirit Box", "Impressão Digital", "Escrita Fantasma") { Tell = "Joga múltiplos itens. Joga itens longe. Drena sanidade.", HuntThreshold = "< 50%" },
                    new Ghost("Banshee", "😱", "Impressão Digital", "Orbe Fantasma", "D.O.T.S.") { Tell = "Grito no mic parabólico. Foca apenas 1 jogador. Ignora outros.", HuntThreshold = "Alvo 50%" },
                    new Ghost("Jinn", "⚡", "EMF Nível 5", "Impressão Digital", "Temperatura Baixa") { Tell = "Velocidade: 2.5 m/s se longe e energia ON.", HuntThreshold = "< 50%" },
                    new Ghost("Mare", "💡", "Spirit Box", "Orbe Fantasma", "Escrita Fantasma") { Tell = "Odeia luz. Apaga luz instantâneo. Caça cedo no escuro.", HuntThreshold = "60% / 40%" },
                    new Ghost("Revenant", "😈", "Orbe Fantasma", "Escrita Fantasma", "Temperatura Baixa") { Tell = "Velocidade: 3.0 m/s (Vendo) | 1.0 m/s (Escondido).", HuntThreshold = "< 50%" },
                    new Ghost("Shade", "🌑", "EMF Nível 5", "Escrita Fantasma", "Temperatura Baixa") { Tell = "Tímido. Não caça se houver jogadores na sala.", HuntThreshold = "< 35%" },
                    new Ghost("Demon", "⸸", "Impressão Digital", "Escrita Fantasma", "Temperatura Baixa") { Tell = "Pode caçar a qualquer %. Cooldown 20s. Smudge 60s.", HuntThreshold = "Qualquer" },
                    new Ghost("Yurei", "🚪", "Orbe Fantasma", "Temperatura Baixa", "D.O.T.S.") { Tell = "Fecha portas totalmente. Smudge prende na sala.", HuntThreshold = "< 50%" },
                    new Ghost("Oni", "👹", "EMF Nível 5", "Temperatura Baixa", "D.O.T.S.") { Tell = "Muito visível na caçada. Drena 20% sanidade ao tocar.", HuntThreshold = "< 50%" },
                    new Ghost("Yokai", "🔇", "Spirit Box", "Orbe Fantasma", "D.O.T.S.") { Tell = "Surdo (Ouve < 2.5m). Falar perto atrai caçada cedo.", HuntThreshold = "80% (Voz)" },
                    new Ghost("Hantu", "❄", "Impressão Digital", "Orbe Fantasma", "Temperatura Baixa") { Tell = "Velocidade: 1.4 - 2.7 m/s (Frio). Hálito na caçada.", HuntThreshold = "< 50%" },
                    new Ghost("Goryo", "👀", "EMF Nível 5", "Impressão Digital", "D.O.T.S.") { Tell = "DOTS só na câmera e sem ninguém perto. Não troca de sala.", HuntThreshold = "< 50%" },
                    new Ghost("Myling", "🤫", "EMF Nível 5", "Impressão Digital", "Escrita Fantasma") { Tell = "Passos silenciosos (< 12m). Mais sons no parabólico.", HuntThreshold = "< 50%" },
                    new Ghost("Onryo", "🔥", "Spirit Box", "Orbe Fantasma", "Temperatura Baixa") { Tell = "Fogo previne caçada. Apagar 3 velas = Caça.", HuntThreshold = "60%" },
                    new Ghost("The Twins", "♊", "EMF Nível 5", "Spirit Box", "Temperatura Baixa") { Tell = "Velocidade: 1.5 m/s (Lento) ou 1.9 m/s (Rápido).", HuntThreshold = "< 50%" },
                    new Ghost("Raiju", "🔋", "EMF Nível 5", "Orbe Fantasma", "D.O.T.S.") { Tell = "Velocidade: 2.5 m/s perto de eletrônicos.", HuntThreshold = "< 65%" },
                    new Ghost("Obake", "🖐", "EMF Nível 5", "Impressão Digital", "Orbe Fantasma") { Tell = "Mão de 6 Dedos. Muda de forma na caçada.", HuntThreshold = "< 50%" },
                    new Ghost("The Mimic", "🎭", "Spirit Box", "Impressão Digital", "Temperatura Baixa") { Tell = "SEMPRE tem Orbes (Falsos). Copia qualquer fantasma.", HuntThreshold = "Copia" },
                    new Ghost("Moroi", "☠", "Spirit Box", "Escrita Fantasma", "Temperatura Baixa") { Tell = "Velocidade: 1.5 - 2.25 m/s (Sanidade). Smudge cega 7.5s.", HuntThreshold = "< 50%" },
                    new Ghost("Deogen", "👁", "Spirit Box", "Escrita Fantasma", "D.O.T.S.") { Tell = "Velocidade: 3.0 m/s (Longe) -> 0.4 m/s (Perto). Te acha.", HuntThreshold = "< 40%" },
                    new Ghost("Thaye", "⏳", "Orbe Fantasma", "Escrita Fantasma", "D.O.T.S.") { Tell = "Envelhece. Velocidade: 2.75 m/s -> 1.0 m/s.", HuntThreshold = "75% -> 15%" },
                };
            }
            else
            {
                AllGhosts = new List<Ghost> {
                    new Ghost("Spirit", "👻", "EMF Level 5", "Spirit Box", "Ghost Writing") { Tell = "Smudge prevents hunt for 180s (Normal 90s).", HuntThreshold = "< 50%" },
                    new Ghost("Wraith", "👣", "EMF Level 5", "Spirit Box", "D.O.T.S.") { Tell = "No salt steps. Teleports to player (EMF 2).", HuntThreshold = "< 50%" },
                    new Ghost("Phantom", "📷", "Spirit Box", "Fingerprints", "D.O.T.S.") { Tell = "Disappears in photo. Slow blink (Invisible longer).", HuntThreshold = "< 50%" },
                    new Ghost("Poltergeist", "💥", "Spirit Box", "Fingerprints", "Ghost Writing") { Tell = "Throws multiple items at once. Drains sanity (2%).", HuntThreshold = "< 50%" },
                    new Ghost("Banshee", "😱", "Fingerprints", "Ghost Orb", "D.O.T.S.") { Tell = "Paramic Scream. Targets 1 player (Ignores others).", HuntThreshold = "< 50% (Target)" },
                    new Ghost("Jinn", "⚡", "EMF Level 5", "Fingerprints", "Freezing") { Tell = "Speed: 2.5 m/s if breaker ON and far.", HuntThreshold = "< 50%" },
                    new Ghost("Mare", "💡", "Spirit Box", "Ghost Orb", "Ghost Writing") { Tell = "Hates lights. Immediate switch off. Hunts early in dark.", HuntThreshold = "60% / 40%" },
                    new Ghost("Revenant", "😈", "Ghost Orb", "Ghost Writing", "Freezing") { Tell = "Speed: 3.0 m/s (Chasing) | 1.0 m/s (Hiding).", HuntThreshold = "< 50%" },
                    new Ghost("Shade", "🌑", "EMF Level 5", "Ghost Writing", "Freezing") { Tell = "Shy. Won't hunt if players are in same room.", HuntThreshold = "< 35%" },
                    new Ghost("Demon", "⸸", "Fingerprints", "Ghost Writing", "Freezing") { Tell = "Rare ability to hunt at any %. 20s cooldown.", HuntThreshold = "Any / 70%" },
                    new Ghost("Yurei", "🚪", "Ghost Orb", "Freezing", "D.O.T.S.") { Tell = "Full door close (Sanity drain). Smudge traps it.", HuntThreshold = "< 50%" },
                    new Ghost("Oni", "👹", "EMF Level 5", "Freezing", "D.O.T.S.") { Tell = "Very visible during hunt. Drains 20% sanity on hit.", HuntThreshold = "< 50%" },
                    new Ghost("Yokai", "🔇", "Spirit Box", "Ghost Orb", "D.O.T.S.") { Tell = "Deaf (Hears < 2.5m). Talking triggers early hunt.", HuntThreshold = "80% (Voice)" },
                    new Ghost("Hantu", "❄", "Fingerprints", "Ghost Orb", "Freezing") { Tell = "Speed: 1.4 - 2.7 m/s (Based on Temp). Cold breath.", HuntThreshold = "< 50%" },
                    new Ghost("Goryo", "👀", "EMF Level 5", "Fingerprints", "D.O.T.S.") { Tell = "DOTS only on Cam and no one near. No roaming.", HuntThreshold = "< 50%" },
                    new Ghost("Myling", "🤫", "EMF Level 5", "Fingerprints", "Ghost Writing") { Tell = "Silent footsteps during hunt (< 12m range).", HuntThreshold = "< 50%" },
                    new Ghost("Onryo", "🔥", "Spirit Box", "Ghost Orb", "Freezing") { Tell = "Fire prevents hunt. Blowing 3 flames = Hunt.", HuntThreshold = "60%" },
                    new Ghost("The Twins", "♊", "EMF Level 5", "Spirit Box", "Freezing") { Tell = "Speed: 1.5 m/s (Decoy) or 1.9 m/s (Main).", HuntThreshold = "< 50%" },
                    new Ghost("Raiju", "🔋", "EMF Level 5", "Ghost Orb", "D.O.T.S.") { Tell = "Speed: 2.5 m/s near electronics.", HuntThreshold = "< 65%" },
                    new Ghost("Obake", "🖐", "EMF Level 5", "Fingerprints", "Ghost Orb") { Tell = "6-Fingered Print. Shapeshifts (Blinks wrong model).", HuntThreshold = "< 50%" },
                    new Ghost("The Mimic", "🎭", "Spirit Box", "Fingerprints", "Freezing") { Tell = "ALWAYS has Fake Orbs. Copies ANY ghost.", HuntThreshold = "Copy" },
                    new Ghost("Moroi", "☠", "Spirit Box", "Ghost Writing", "Freezing") { Tell = "Speed: 1.5 - 2.25 m/s (Based on Sanity).", HuntThreshold = "< 50%" },
                    new Ghost("Deogen", "👁", "Spirit Box", "Ghost Writing", "D.O.T.S.") { Tell = "Speed: 3.0 m/s (Far) -> 0.4 m/s (Close). Finds you.", HuntThreshold = "< 40%" },
                    new Ghost("Thaye", "⏳", "Ghost Orb", "Ghost Writing", "D.O.T.S.") { Tell = "Ages. Speed: 2.75 m/s (Young) -> 1.0 m/s (Old).", HuntThreshold = "75% -> 15%" },
                };
            }
        }
    }

    public class Ghost : INotifyPropertyChanged
    {
        public string Name { get; set; }
        public string Symbol { get; set; }
        public List<string> Evidences { get; set; }
        public string Tell { get; set; }
        public string HuntThreshold { get; set; }
        private bool _isEliminated;
        public bool IsEliminated { get => _isEliminated; set { _isEliminated = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("IsEliminated")); } }
        public Brush PrimaryColorBrush { get { return (Brush)new BrushConverter().ConvertFrom(PrimaryColor); } }
        public string PrimaryColor { get { if (Evidences.Contains("Freezing") || Evidences.Contains("Temperatura Baixa")) return "#00CED1"; if (Evidences.Contains("EMF Level 5") || Evidences.Contains("EMF Nível 5")) return "#DC143C"; if (Evidences.Contains("D.O.T.S.")) return "#39FF14"; if (Evidences.Contains("Fingerprints") || Evidences.Contains("Impressão Digital")) return "#8A2BE2"; if (Evidences.Contains("Spirit Box")) return "#FF4500"; if (Evidences.Contains("Ghost Writing") || Evidences.Contains("Escrita Fantasma")) return "#FFD700"; return "#E0FFFF"; } }
        public Brush EvidenceGradient { get { var stops = new GradientStopCollection(); Color GetColor(string ev) { if (ev.Contains("EMF")) return Color.FromRgb(220, 20, 60); if (ev.Contains("Fingerprints") || ev.Contains("Digital")) return Color.FromRgb(138, 43, 226); if (ev.Contains("Freezing") || ev.Contains("Gelado") || ev.Contains("Baixa")) return Color.FromRgb(0, 206, 209); if (ev.Contains("Spirit Box")) return Color.FromRgb(255, 69, 0); if (ev.Contains("Ghost Orb") || ev.Contains("Orbe")) return Color.FromRgb(224, 255, 255); if (ev.Contains("Writing") || ev.Contains("Escrita")) return Color.FromRgb(255, 215, 0); if (ev.Contains("D.O.T.S.")) return Color.FromRgb(57, 255, 20); return Colors.White; } if (Evidences.Count >= 1) stops.Add(new GradientStop(GetColor(Evidences[0]), 0.0)); if (Evidences.Count >= 2) stops.Add(new GradientStop(GetColor(Evidences[1]), 0.5)); if (Evidences.Count >= 3) stops.Add(new GradientStop(GetColor(Evidences[2]), 1.0)); return new LinearGradientBrush(stops, new Point(0, 0), new Point(1, 0)) { Opacity = 0.9 }; } }
        public Ghost(string n, string s, params string[] e) { Name = n; Symbol = s; Evidences = e.ToList(); }
        public event PropertyChangedEventHandler PropertyChanged;
    }

    public class BooleanToVisibilityConverter : System.Windows.Data.IValueConverter { public object Convert(object v, Type t, object p, System.Globalization.CultureInfo c) => (bool)v ? Visibility.Visible : Visibility.Collapsed; public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotImplementedException(); }
    public class InverseBooleanToVisibilityConverter : System.Windows.Data.IValueConverter { public object Convert(object v, Type t, object p, System.Globalization.CultureInfo c) => (bool)v ? Visibility.Collapsed : Visibility.Visible; public object ConvertBack(object v, Type t, object p, System.Globalization.CultureInfo c) => throw new NotImplementedException(); }
}