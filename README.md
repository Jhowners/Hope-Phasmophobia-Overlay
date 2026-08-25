# Hope Phasmophobia Overlay (HPO)
### The Cognitive HUD for High-Level Investigation

[![Language](https://img.shields.io/badge/Language-C%23-239120.svg)](https://dotnet.microsoft.com/)
[![Framework](https://img.shields.io/badge/Framework-WPF%20%7C%20.NET%206.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-GPLv3-blue.svg)](LICENSE)
[![Ko-fi](https://img.shields.io/badge/Support-Buy%20Me%20A%20Coffee-F16061.svg)](https://ko-fi.com/hopesan)

> **🩸 v1.4 UPDATE:** Full support for **Blood Moon** (Event Speed Math), **Discord Rich Presence (RPC)**, and 6 new languages including French, German, and Ukrainian!

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
* **All 30 Ghosts Supported:** Full logic for all entities including **Aswang**, **Deildegast**, **Kormos**, **Dayan**, **Gallu**, and **Obambo**.
* **Frictionless Filtering:** As you mark evidence via hotkeys, the ghost list filters instantly.
* **Smart Logic:** Marking "Normal Speed" on the Pacer automatically eliminates the Revenant. Marking "Ghost Orb" eliminates the Mimic (unless you flag it as a Fake Orb).

### 2. The Ghost Pacer (BPM) + Blood Moon 🩸
* **Rhythm Entrainment:** Tap `F10` to the sound of the ghost's footsteps.
* **Dynamic Speed Analysis:** The app calculates the BPM and cross-references it with your evidence list.
* **Event Support:** Select **"Blood Moon (+15%)"** from the speed dropdown to automatically adjust math for the faster event ghosts.

### 3. Discord Rich Presence (RPC) 🎮
* **Live Status:** Automatically displays your current investigation status on Discord.
* **Real-Time Info:** Shows "Searching for Evidence," "Chasing (2.5 m/s)," or "Identified: Demon" to your friends.
* *Privacy Mode:* Can be toggled off instantly via the Right-Click menu.

### 4. Three Tactical Modes
* **📖 Analysis Mode:** Full journal view for the van or safe spots.
* **🧠 Intel Mode [HOME]:** A compact, peripheral HUD designed for looping. Shows only vital stats without blocking your vision.
* **⏱️ Survival Mode [PAUSE/BREAK]:** Strips the UI down to just the Timer. Zero distractions for the heat of the chase.

---

## 🌍 Localization / Languages
Right-click the overlay window to switch languages instantly.
Now supporting 10 languages!

* 🇺🇸 **English** (Default)
* 🇧🇷 **Português (Brasil)**
* 🇯🇵 **Japanese (日本語)** (Thanks to Aozakimimu148 七瀬七々 )
* 🇹🇼 **Traditional Chinese (繁體中文)** (Thanks to Aozakimimu148 七瀬七々 )
* 🇩🇪 **Deutsch**
* 🇪🇸 **Español**
* 🇫🇷 **Français**
* 🇳🇱 **Nederlands**
* 🇷🇺 **Russian (Русский)**
* 🇺🇦 **Ukrainian (Українська)**

---

## 🎮 Controls & Usage

**Important:** You must run Phasmophobia in **Borderless Windowed** mode for the overlay to appear.

### Global Hotkeys
| Key | Function |
| :--- | :--- |
| **F1** | Start / Stop Hunt Cooldown Timer |
| **F7** | Start / Restart Crucifix Cooldown Timer (0:25) |
| **F8** | Stop Smudge Timer |
| **F9** | Start / Restart Smudge Timer (3:00) |
| **F10** | Tap for Ghost Speed (BPM) |
| **F11** | Reset Speed / Pacer |
| **Alt + 1-7** | Toggle Evidence (Matches In-Game Journal Order) |
| **Alt + Backspace** | Reset All Evidence |
| **Home** | Toggle **Intel Mode** (Tactical HUD) |
| **Pause/Break** | Toggle **Survival Mode** (Mini Timer) |
| **Right-Click** | Options Menu [Language / Discord RPC] |

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
3. Extract the folder to your desktop (Do not run from inside the zip).
4. Run `Hophesmoverlay.exe`.

> **Note:** If you experience slight stuttering in the Main Menu, don't worry! This is normal due to game engine priority. The overlay runs perfectly smooth once you load into a lobby or contract.

---

## ☕ Support Development

This project is free and open source because I believe essential tools should be accessible to everyone.

If this tool saves your life during an Apocalypse Run, consider buying me a coffee to support future features!

[![ko-fi](https://ko-fi.com/img/githubbutton_sm.svg)](https://ko-fi.com/hopesan)

---

## 👨‍💻 For Developers (Portfolio Note)
This project demonstrates:
* **WPF & XAML:** Complex UI binding, transparent window layering, and dynamic resource dictionaries for localization.
* **DiscordRPC:** Integration of external APIs for rich user presence.
* **P/Invoke (User32.dll):** Implementation of Global Low-Level Keyboard Hooks for hotkey detection outside the application focus.
* **MVVM Architecture:** Clean separation of logic and UI using `INotifyPropertyChanged`.
* **Multi-Threading:** High-priority dispatcher timers to ensure UI responsiveness during heavy game load.
