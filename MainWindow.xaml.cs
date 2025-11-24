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
        [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);

        private const int WH_KEYBOARD_LL = 13; private const int WM_KEYDOWN = 0x0100; private const int WM_SYSKEYDOWN = 0x0104;
        private const int VK_MENU = 0x12;
        private static LowLevelKeyboardProc _proc = HookCallback;
        private static IntPtr _hookID = IntPtr.Zero;
        private LowLevelKeyboardProc _procReference;

        public List<Ghost> AllGhosts { get; set; }
        private DispatcherTimer _smudgeTimer;
        private int _timerSecondsRemaining = 180;
        private DispatcherTimer _huntTimer;
        private int _huntTimerSeconds = 25;
        private List<DateTime> _tapHistory = new List<DateTime>();
        private double _speedMultiplier = 1.0;
        private int _currentView = 0;
        private string _currentLang = "EN";

        private readonly List<string> _fastGhosts = new List<string> { "Jinn", "Revenant", "Hantu", "The Twins", "Raiju", "Moroi", "Deogen", "Thaye", "The Mimic" };
        private readonly List<string> _slowGhosts = new List<string> { "Revenant", "Hantu", "Deogen", "Thaye", "The Mimic", "The Twins", "Moroi" };
        private string _currentSpeedCategory = "None";

        public MainWindow()
        {
            InitializeComponent();
            _procReference = HookCallback;
            _hookID = SetHook(_procReference);

            InitializeGhosts("EN");
            GhostsListControl.ItemsSource = AllGhosts;
            GhostsIntelControl.ItemsSource = AllGhosts;

            _smudgeTimer = new DispatcherTimer(DispatcherPriority.Render);
            _smudgeTimer.Interval = TimeSpan.FromSeconds(1);
            _smudgeTimer.Tick += SmudgeTimer_Tick;

            _huntTimer = new DispatcherTimer(DispatcherPriority.Render);
            _huntTimer.Interval = TimeSpan.FromSeconds(1);
            _huntTimer.Tick += HuntTimer_Tick;
        }

        private void BtnDonate_Click(object sender, RoutedEventArgs e) { try { Process.Start(new ProcessStartInfo { FileName = "https://ko-fi.com/hopesan", UseShellExecute = true }); } catch { } }

        // --- MENU LOGIC ---
        private void MenuEn_Click(object sender, RoutedEventArgs e) => SetLanguage("EN");
        private void MenuPt_Click(object sender, RoutedEventArgs e) => SetLanguage("PT");
        private void MenuExit_Click(object sender, RoutedEventArgs e) { Application.Current.Shutdown(); }

        // --- MOUSE LOGIC ---
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left) this.DragMove();
        }

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
                LblSmudge.Text = "INCENSO [F9]"; LblHunt.Text = "CAÇADA [F7]"; LblSpeed.Text = "VELOCIDADE (m/s)";
                LblMiniSmudge.Text = "INCENSO"; LblMiniHunt.Text = "CAÇADA";
                LblIntelSmudge.Text = "INCENSO"; LblIntelHunt.Text = "CAÇADA"; LblIntelSpeed.Text = "VELOC.";
                LblIntelEvidence.Text = "EVIDÊNCIA MARCADA"; LblIntelTargets.Text = "FANTASMAS POSSÍVEIS";
                TxtGhostSpeedGuess.Text = "Toque no ritmo dos passos";
                ChkEv1.Content = "EMF Nível 5"; ChkEv2.Content = "Spirit Box"; ChkEv3.Content = "Impressão Digital";
                ChkEv4.Content = "Orbe Fantasma"; ChkEv5.Content = "Escrita Fantasma"; ChkEv6.Content = "Temperatura Baixa"; ChkEv7.Content = "D.O.T.S.";
            }
            else
            {
                LblControls.Text = "[INS] MINI | [HOME] INTEL | [ALT+8] RESET";
                LblEvidence.Text = "EVIDENCE (ALT+1-7)";
                LblSmudge.Text = "SMUDGE [F9]"; LblHunt.Text = "HUNT CD [F7]"; LblSpeed.Text = "SPEED (m/s)";
                LblMiniSmudge.Text = "SMUDGE"; LblMiniHunt.Text = "HUNT CD";
                LblIntelSmudge.Text = "SMUDGE"; LblIntelHunt.Text = "HUNT CD"; LblIntelSpeed.Text = "SPEED";
                LblIntelEvidence.Text = "MARKED EVIDENCE"; LblIntelTargets.Text = "POSSIBLE TARGETS";
                TxtGhostSpeedGuess.Text = "Select Speed & Tap";
                ChkEv1.Content = "EMF Level 5"; ChkEv2.Content = "Spirit Box"; ChkEv3.Content = "Fingerprints";
                ChkEv4.Content = "Ghost Orb"; ChkEv5.Content = "Ghost Writing"; ChkEv6.Content = "Freezing"; ChkEv7.Content = "D.O.T.S.";
            }
            UpdateGhostFiltering();
        }

        private static IntPtr SetHook(LowLevelKeyboardProc proc)
        {
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                return SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && (wParam == (IntPtr)WM_KEYDOWN || wParam == (IntPtr)WM_SYSKEYDOWN))
            {
                int vkCode = Marshal.ReadInt32(lParam);
                var wnd = Application.Current.MainWindow as MainWindow;
                if (wnd != null)
                {
                    bool isAltDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                    if (vkCode == 0x75) wnd.StopHuntTimer();
                    else if (vkCode == 0x76) wnd.ResetHuntTimer();
                    else if (vkCode == 0x77) wnd.StopSmudgeTimer();
                    else if (vkCode == 0x78) wnd.ResetSmudgeTimer();
                    else if (vkCode == 0x79) wnd.CalculateBPM();
                    else if (vkCode == 0x7A) wnd.ResetPace();
                    else if (vkCode == 0x2D) wnd.SetViewMode(1);
                    else if (vkCode == 0x24) wnd.SetViewMode(2);
                    else if (isAltDown && vkCode >= 0x31 && vkCode <= 0x38)
                    {
                        if (vkCode == 0x38) wnd.ResetEvidence();
                        else wnd.ToggleEvidenceByIndex(vkCode - 0x30);
                    }
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)][return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(IntPtr hhk);
        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)] private static extern IntPtr GetModuleHandle(string lpModuleName);

        protected override void OnClosed(EventArgs e) { UnhookWindowsHookEx(_hookID); base.OnClosed(e); }

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
            var checkBoxes = FindVisualChildren<CheckBox>(this);
            foreach (var box in checkBoxes) { box.IsChecked = false; }
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

        private void InterpretSpeed(double ms)
        {
            string guess = ""; Brush c = Brushes.Gray; _currentSpeedCategory = "Normal";
            if (ms < 1.3) { guess = (_currentLang == "PT") ? "Rev (Lento)/Deo" : "Rev (Slow)/Deo"; c = Brushes.Cyan; _currentSpeedCategory = "Slow"; }
            else if (ms >= 1.3 && ms < 1.6) { guess = (_currentLang == "PT") ? "Lento" : "Slightly Slow"; c = Brushes.LightGray; }
            else if (ms >= 1.6 && ms <= 1.8) { guess = (_currentLang == "PT") ? "Normal" : "Normal Speed"; c = Brushes.White; }
            else if (ms > 1.8 && ms < 2.5) { guess = (_currentLang == "PT") ? "Rápido (Hantu/Moroi)" : "Fast (Hantu/Moroi)"; c = Brushes.Orange; _currentSpeedCategory = "Fast"; }
            else if (ms >= 2.5) { guess = (_currentLang == "PT") ? "CORRA (Raiju/Rev)" : "RUN (Raiju/Rev)"; c = Brushes.Red; _currentSpeedCategory = "Fast"; }
            TxtGhostSpeedGuess.Text = guess; TxtGhostSpeedGuess.Foreground = c;
            TxtIntelSpeedGuess.Text = guess; TxtIntelSpeedGuess.Foreground = c;
            TxtPacerStatus.Text = $"FILTER: {_currentSpeedCategory.ToUpper()}"; TxtPacerStatus.Foreground = c;
            UpdateGhostFiltering();
        }

        private void ToggleEvidenceByIndex(int index)
        {
            CheckBox t = null;
            switch (index) { case 1: t = ChkEv1; break; case 2: t = ChkEv2; break; case 3: t = ChkEv3; break; case 4: t = ChkEv4; break; case 5: t = ChkEv5; break; case 6: t = ChkEv6; break; case 7: t = ChkEv7; break; }
            if (t != null) { if (t.IsChecked == false) t.IsChecked = true; else if (t.IsChecked == true) t.IsChecked = null; else t.IsChecked = false; UpdateGhostFiltering(); }
        }

        private void Evidence_Changed(object sender, RoutedEventArgs e) => UpdateGhostFiltering();

        private void UpdateGhostFiltering()
        {
            var checkBoxes = FindVisualChildren<CheckBox>(this);
            List<string> foundEv = new List<string>();
            List<string> ruledOutEv = new List<string>();

            foreach (var box in checkBoxes)
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

        private void CmbSpeed_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbSpeed.SelectedIndex == 0) _speedMultiplier = 0.5; else if (CmbSpeed.SelectedIndex == 1) _speedMultiplier = 0.75; else if (CmbSpeed.SelectedIndex == 2) _speedMultiplier = 1.0; else if (CmbSpeed.SelectedIndex == 3) _speedMultiplier = 1.25; else if (CmbSpeed.SelectedIndex == 4) _speedMultiplier = 1.5;
        }

        private void StartSmudgeTimer() { _timerSecondsRemaining = 180; ProgressTimer.Value = 180; _smudgeTimer.Start(); UpdateTimerDisplay(); }
        private void ResetSmudgeTimer() { StartSmudgeTimer(); }
        private void BtnResetPace_Click(object sender, RoutedEventArgs e) => ResetPace();
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

        private void InitializeGhosts(string lang = "EN")
        {
            if (lang == "PT")
            {
                AllGhosts = new List<Ghost> {
                    new Ghost("Spirit", "👻", "EMF Nível 5", "Spirit Box", "Escrita Fantasma") { Tell = "Incenso para caçada por 180s" },
                    new Ghost("Wraith", "👣", "EMF Nível 5", "Spirit Box", "D.O.T.S.") { Tell = "Não pisa no sal" },
                    new Ghost("Phantom", "📷", "Spirit Box", "Impressão Digital", "D.O.T.S.") { Tell = "Pisca lento / Some na foto" },
                    new Ghost("Poltergeist", "💥", "Spirit Box", "Impressão Digital", "Escrita Fantasma") { Tell = "Arremessa vários itens" },
                    new Ghost("Banshee", "😱", "Impressão Digital", "Orbe Fantasma", "D.O.T.S.") { Tell = "Grito no microfone parabólico" },
                    new Ghost("Jinn", "⚡", "EMF Nível 5", "Impressão Digital", "Temperatura Baixa") { Tell = "Rápido com energia ligada" },
                    new Ghost("Mare", "💡", "Spirit Box", "Orbe Fantasma", "Escrita Fantasma") { Tell = "Odeia luz / Desliga luz instantâneo" },
                    new Ghost("Revenant", "😈", "Orbe Fantasma", "Escrita Fantasma", "Temperatura Baixa") { Tell = "Lento escondido / Rápido ao ver" },
                    new Ghost("Shade", "🌑", "EMF Nível 5", "Escrita Fantasma", "Temperatura Baixa") { Tell = "Tímido / Não caça se estiver perto" },
                    new Ghost("Demon", "⸸", "Impressão Digital", "Escrita Fantasma", "Temperatura Baixa") { Tell = "Caça a 70% / 60s cooldown" },
                    new Ghost("Yurei", "🚪", "Orbe Fantasma", "Temperatura Baixa", "D.O.T.S.") { Tell = "Incenso prende na sala" },
                    new Ghost("Oni", "👹", "EMF Nível 5", "Temperatura Baixa", "D.O.T.S.") { Tell = "Visível por mais tempo / Sem evento fumaça" },
                    new Ghost("Yokai", "🔇", "Spirit Box", "Orbe Fantasma", "D.O.T.S.") { Tell = "Surdo na caçada / Voz atrai caçada" },
                    new Ghost("Hantu", "❄", "Impressão Digital", "Orbe Fantasma", "Temperatura Baixa") { Tell = "Rápido no frio / Sai fumaça" },
                    new Ghost("Goryo", "👀", "EMF Nível 5", "Impressão Digital", "D.O.T.S.") { Tell = "DOTS apenas na câmera" },
                    new Ghost("Myling", "🤫", "EMF Nível 5", "Impressão Digital", "Escrita Fantasma") { Tell = "Passos silenciosos" },
                    new Ghost("Onryo", "🔥", "Spirit Box", "Orbe Fantasma", "Temperatura Baixa") { Tell = "Fogo previne caçada" },
                    new Ghost("The Twins", "♊", "EMF Nível 5", "Spirit Box", "Temperatura Baixa") { Tell = "Velocidades diferentes / Interações longe" },
                    new Ghost("Raiju", "🔋", "EMF Nível 5", "Orbe Fantasma", "D.O.T.S.") { Tell = "Rápido perto de eletrônicos" },
                    new Ghost("Obake", "🖐", "EMF Nível 5", "Impressão Digital", "Orbe Fantasma") { Tell = "Transmorfo / 6 Dedos" },
                    new Ghost("The Mimic", "🎭", "Spirit Box", "Impressão Digital", "Temperatura Baixa") { Tell = "Orbes Falsos / Copia comportamentos" },
                    new Ghost("Moroi", "☠", "Spirit Box", "Escrita Fantasma", "Temperatura Baixa") { Tell = "Maldição / Velocidade por sanidade" },
                    new Ghost("Deogen", "👁", "Spirit Box", "Escrita Fantasma", "D.O.T.S.") { Tell = "Sempre te acha / Lento perto" },
                    new Ghost("Thaye", "⏳", "Orbe Fantasma", "Escrita Fantasma", "D.O.T.S.") { Tell = "Envelhece / Rápido no início -> Lento fim" },
                };
            }
            else
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
                    new Ghost("Oni", "👹", "EMF Level 5", "Freezing", "D.O.T.S.") { Tell = "Visible longer in hunt" },
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
                if (Evidences.Contains("Freezing") || Evidences.Contains("Temperatura Baixa")) return "#00CED1";
                if (Evidences.Contains("EMF Level 5") || Evidences.Contains("EMF Nível 5")) return "#DC143C";
                if (Evidences.Contains("D.O.T.S.")) return "#39FF14";
                if (Evidences.Contains("Fingerprints") || Evidences.Contains("Impressão Digital")) return "#8A2BE2";
                if (Evidences.Contains("Spirit Box")) return "#FF4500";
                if (Evidences.Contains("Ghost Writing") || Evidences.Contains("Escrita Fantasma")) return "#FFD700";
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
                    if (ev.Contains("Fingerprints") || ev.Contains("Digital")) return Color.FromRgb(138, 43, 226);
                    if (ev.Contains("Freezing") || ev.Contains("Gelado") || ev.Contains("Baixa")) return Color.FromRgb(0, 206, 209);
                    if (ev.Contains("Spirit Box")) return Color.FromRgb(255, 69, 0);
                    if (ev.Contains("Ghost Orb") || ev.Contains("Orbe")) return Color.FromRgb(224, 255, 255);
                    if (ev.Contains("Writing") || ev.Contains("Escrita")) return Color.FromRgb(255, 215, 0);
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