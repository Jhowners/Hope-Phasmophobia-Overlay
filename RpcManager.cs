using DiscordRPC;
using System;

namespace Hophesmoverlay
{
    public class RpcManager
    {
        private DiscordRpcClient _client;
        private const string AppId = "1461766452825817179"; // Keep your ID!
        private Timestamps _startTimestamp; // <--- 1. Store the start time

        public void Initialize()
        {
            _client = new DiscordRpcClient(AppId);
            _client.Initialize();

            // 2. Set the time ONCE when the app opens
            _startTimestamp = Timestamps.Now;

            SetStatus("Main Menu", "Preparing for the hunt...");
        }

        public void SetStatus(string details, string state, string btn1Label = "Download Overlay" , string btn2label = "Buy me a Coffee ☕")
        {
            if (_client == null || !_client.IsInitialized) return;

            _client.SetPresence(new RichPresence()
            {
                Details = details,
                State = state,
                Assets = new Assets()
                {
                    LargeImageKey = "app_logo",
                    LargeImageText = "Hope Overlay v1.4",
                },
                // 3. Use the STORED timestamp, not "Now"
                Timestamps = _startTimestamp,

                Buttons = new Button[]
                {
                
                new Button()
                {
                    Label = btn1Label,
                    Url = "https://www.nexusmods.com/phasmophobia/mods/159"
                },
            
                
                new Button()
                {
                    Label = btn2label,
                    Url = "https://ko-fi.com/hopesan" 
                }
            }
            });
        }

        public void Dispose() => _client?.Dispose();
    }
}