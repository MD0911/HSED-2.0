using Avalonia;
using System;
using System.Diagnostics;

namespace HSED_2._0
{
    internal class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
#if DEBUG
            DebugFileBootstrap.Hook("HSED_2_0");
#endif
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
#if DEBUG
            DebugFileBootstrap.FlushAndClose();
#endif
        }

        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace(); // Avalonia loggt auch über Trace
    }
}
