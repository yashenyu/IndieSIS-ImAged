using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using ImAged.Core;
using ImAged.Services;

namespace ImAged
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private const bool CaptureProtectionEnabled = false;

        // Singleton instance for SecureProcessManager
        public static SecureProcessManager SecureProcessManagerInstance { get; private set; }
        public static bool IsShuttingDown { get; private set; } = false;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Set up global exception handling to prevent error dialogs
            this.DispatcherUnhandledException += (sender, args) =>
            {
                System.Diagnostics.Debug.WriteLine($"Unhandled exception: {args.Exception.Message}");
                args.Handled = true; // Prevent the exception from being re-thrown
            };

            // Initialize the singleton SecureProcessManager
            SecureProcessManagerInstance = new SecureProcessManager();
            this.Dispatcher.InvokeAsync(async () =>
            {
                if (IsShuttingDown) return; // Prevent late init
                try
                {
                    await SecureProcessManagerInstance.InitializeAsync();
                }
                catch (Exception ex)
                {
                    // Silently handle initialization errors
                    System.Diagnostics.Debug.WriteLine($"Secure backend initialization failed: {ex.Message}");
                }
            });

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

        protected override void OnExit(ExitEventArgs e)
        {
            IsShuttingDown = true;
            try
            {
                // Dispose the singleton SecureProcessManager on app exit
                if (SecureProcessManagerInstance != null)
                {
                    SecureProcessManagerInstance.Dispose();
                    SecureProcessManagerInstance = null;
                }
            }
            catch (Exception ex)
            {
                // Log but don't show error during shutdown
                System.Diagnostics.Debug.WriteLine($"Error during app shutdown: {ex.Message}");
            }
            finally
            {
                base.OnExit(e);
            }
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
