# Hope Phasmophobia Overlay (HPO)
### The Cognitive HUD for High-Level Investigation

[![Language](https://img.shields.io/badge/Language-C%23-239120.svg)](https://dotnet.microsoft.com/)
[![Framework](https://img.shields.io/badge/Framework-WPF%20%7C%20.NET%206.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)
[![Ko-fi](https://img.shields.io/badge/Support-Buy%20Me%20A%20Coffee-F16061.svg)](https://ko-fi.com/hopesan)

**Stop fighting your memory. Start fighting the Ghost.**

Phasmophobia is a game of cognitive endurance. When a Revenant starts hunting, your brain shifts into "Fight or Flight." In that split second, trying to mentally calculate footstep speed or remember Smudge cooldowns adds **Cognitive Load**—mental friction that causes hesitation and errors.

**Hope Phasmophobia Overlay** acts as your "Second Brain." It floats directly over your game, offloading data processing so you can maintain 100% focus on survival.

---

## 🧠 The Science of "Flow"
Every time you Alt-Tab to a wiki or look at a second monitor, you suffer from **Context Switching Penalty**. Your brain takes milliseconds to refocus—time you don't have during an Apocalypse Run.

HPO keeps you in the **Flow State** by using:
* **Pre-Attentive Processing:** Evidence cards glow with specific colors (🔴 Red = EMF 5, 🔵 Cyan = Freezing). Your brain recognizes these patterns instantly without needing to read text.
* **Muscle Memory Input:** Toggle evidence via global hotkeys (`Alt+1` through `Alt+7`) without ever unlocking your mouse or stopping your movement.

---

## ⚡ Key Features

### 1. Real-Time Deduction Engine
Don't guess. Know.
* **Frictionless Filtering:** As you mark evidence via hotkeys, the ghost list filters instantly.
* **Smart Logic:** Marking "Normal Speed" on the Pacer automatically eliminates the Revenant. Marking "Ghost Orb" eliminates the Mimic (unless you flag it as a Fake Orb).

### 2. The Ghost Pacer (BPM)
* **Rhythm Entrainment:** Tap `F10` to the sound of the ghost's footsteps.
* **Instant Identification:** The app calculates the BPM and cross-references it with your evidence list, highlighting potential matches (e.g., Hantu, Moroi, Thaye) in real-time.

### 3. Smart Smudge Timer
* **Audio-Visual Cues:** Tracks the hidden cooldowns for Spirits (180s), Demons (60s), and Standard Ghosts (90s).
* **Panic Management:** Know exactly when you are safe to leave your hiding spot.

### 4. Three Tactical Modes
* **📖 Analysis Mode:** Full journal view for the van or safe spots.
* **🧠 Intel Mode [HOME]:** A compact, peripheral HUD designed for looping. Shows only vital stats without blocking your vision.
* **⏱️ Survival Mode [PAUSE BREAK]:** Strips the UI down to just the Timer. Zero distractions for the heat of the chase.

---

## 🎮 Controls & Usage

**Important:** You must run Phasmophobia in **Borderless Windowed** mode for the overlay to appear.

### Global Hotkeys
| Key | Function |
| :--- | :--- |
| **F7** | Start / Restart Crucifix Timer (0:25) |
| **F8** | Stop / Reset Smudge Timer 
| **F9** | Start / Restart Smudge Timer (3:00) |
| **F10** | Tap for Ghost Speed (BPM) |
| **F11** | Reset Speed / Pacer |
| **Alt + 1-8** | Toggle Evidence (EMF, Box, Fingerprints, etc.) |
| **Home** | Toggle **Intel Mode** (Tactical HUD) |
| **Insert** | Toggle **Survival Mode** (Mini Timer) |
| **Right-Click** | Close |

---

## 🛡️ Safety & Compliance
This application is an **External Overlay**.
* ✅ It does **NOT** inject code into the game.
* ✅ It does **NOT** read game memory.
* ✅ It is safe to use and does not violate anti-cheat policies.

---

## 📥 Installation

1. Go to the **[Releases Page](../../releases)**.
2. Download the latest `.zip` file.
3. Extract the folder to your desktop.
4. Run `Hophesmoverlay.exe`.

---

## ☕ Support Development

This project is free and open source because I believe essential tools should be accessible to everyone.

If this tool saves your life during an Apocalypse Run, consider buying me a coffee to support future features (like Voice Recognition)!

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/hopesan)

---

## 👨‍💻 For Developers (Portfolio Note)
This project demonstrates:
* **WPF & XAML:** Complex UI binding, transparent window layering, and dynamic resource dictionaries.
* **P/Invoke (User32.dll):** Implementation of Global Low-Level Keyboard Hooks for hotkey detection outside the application focus.
* **MVVM Architecture:** Clean separation of logic and UI using `INotifyPropertyChanged`.
* **Multi-Threading:** High-priority dispatcher timers to ensure UI responsiveness during heavy game load.
