using System.IO;
using System.Text.Json;
using System.Windows.Media;

namespace Hophesmoverlay
{
    // --- 1. CONFIGURATION MODEL ---
    public class AppSettings
    {
        public double Opacity { get; set; } = 1.0;
        public string Language { get; set; } = "en";
        public KeyBindings Keys { get; set; } = new KeyBindings();

        public static AppSettings Load()
        {
            if (!File.Exists("settings.json")) return new AppSettings();
            try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText("settings.json")); }
            catch { return new AppSettings(); }
        }

        public void Save()
        {
            File.WriteAllText("settings.json", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
    }

    public class KeyBindings
    {
        // 0x08 is Backspace, 0x12 is Alt. The Logic handles the Alt check.
        public int Reset { get; set; } = 0x08;
        public int SmudgeStop { get; set; } = 0x77; // F8
        public int SmudgeStart { get; set; } = 0x78; // F9
        public int HuntStop { get; set; } = 0x75; // F6
        public int HuntStart { get; set; } = 0x76; // F7
        public int SpeedTap { get; set; } = 0x79; // F10
        public int SpeedReset { get; set; } = 0x7A; // F11
        public int HuntDuration { get; set; } = 112; // F12
        public int Evidence1 { get; set; } = 49; // Key '1'
        public int Evidence2 { get; set; } = 50; // Key '2'
        public int Evidence3 { get; set; } = 51; // Key '3'
        public int Evidence4 { get; set; } = 52; // Key '4'
        public int Evidence5 { get; set; } = 53; // Key '5'
        public int Evidence6 { get; set; } = 54; // Key '6'
        public int Evidence7 { get; set; } = 55; // Key '7'
    }

    // --- 2. LANGUAGE MODEL ---
    public class LangFile
    {
        public string Code { get; set; }
        public Dictionary<string, string> UI { get; set; }
        public Dictionary<string, string> SpeedSys { get; set; }
        public List<GhostData> Ghosts { get; set; }
    }

    public class GhostData
    {
        public string ID { get; set; }
        public string Name { get; set; }
        public string Symbol { get; set; }
        public List<string> Evidences { get; set; }
        public string Guaranteed { get; set; }
        public string Tell { get; set; }
        public string HuntThreshold { get; set; }
        public double MinSpeed { get; set; }
        public double MaxSpeed { get; set; }
    }
}