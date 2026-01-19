using System;
using System.Windows;


namespace Hophesmoverlay 
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {

        public static RpcManager DiscordRpc = new RpcManager();
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 3. Clean up the connection when the app closes
            DiscordRpc?.Dispose();
            base.OnExit(e);
        }
    }
}