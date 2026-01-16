using System;
using System.Windows;


namespace Hophesmoverlay 
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // 1. Create a static reference so we can call "App.DiscordRpc" from anywhere
        public static RpcManager DiscordRpc { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 2. Initialize Discord RPC when the app starts
            try
            {
                DiscordRpc = new RpcManager();
                DiscordRpc.Initialize();
            }
            catch (Exception ex)
            {
                // Just in case RPC fails, we don't want the app to crash
                System.Diagnostics.Debug.WriteLine($"RPC Error: {ex.Message}");
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 3. Clean up the connection when the app closes
            DiscordRpc?.Dispose();
            base.OnExit(e);
        }
    }
}