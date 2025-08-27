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
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            this.Dispatcher.InvokeAsync(() =>
            {
                foreach (Window window in this.Windows)
                {
                    AttachSecurity(window);
                }

                // Apply to any window created after startup (use Loaded routed event)
                EventManager.RegisterClassHandler(
                    typeof(Window),
                    FrameworkElement.LoadedEvent,
                    new RoutedEventHandler((sender, args) =>
                    {
                        if (sender is Window w)
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
                WindowSecurity.ApplyExcludeFromCapture(window);
            }

            if (new WindowInteropHelper(window).Handle != IntPtr.Zero)
            {
                Apply(window, EventArgs.Empty);
            }
            else
            {
                // Fallback to Loaded when SourceInitialized is not available as a routed event
                window.Loaded += (s, e) => Apply(s, EventArgs.Empty);
            }
        }
    }
}
