using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using ImAged.Core;

namespace ImAged
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private const bool CaptureProtectionEnabled = false;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            this.Dispatcher.InvokeAsync(() =>
            {
                foreach (Window window in this.Windows)
                {
                    AttachSecurity(window);
                }

                EventManager.RegisterClassHandler(
                    typeof(Window),
                    FrameworkElement.LoadedEvent,
                    new RoutedEventHandler((sender, args) =>
                    {
                        if (CaptureProtectionEnabled && sender is Window w)
                        {
                            WindowSecurity.ApplyExcludeFromCapture(w);
                        }
                    }));
            });
        }

        private static void AttachSecurity(Window window)
        {
            void Apply(object sender, EventArgs args)
            {
                if (CaptureProtectionEnabled)
                {
                    WindowSecurity.ApplyExcludeFromCapture(window);
                }
            }

            if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            {
                Apply(window, EventArgs.Empty);
            }
            else
            {
                window.Loaded += (s, e) => Apply(s, EventArgs.Empty);
            }
        }
    }
}
