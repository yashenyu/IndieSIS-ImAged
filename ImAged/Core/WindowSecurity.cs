using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ImAged.Core
{
	public static class WindowSecurity
	{
		// Values from Win32 headers
		private const uint WDA_NONE = 0x0;
		private const uint WDA_MONITOR = 0x1;
		private const uint WDA_EXCLUDEFROMCAPTURE = 0x11; // Windows 10 1903+

		[DllImport("user32.dll", SetLastError = true)]
		private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint dwAffinity);

		public static bool ApplyExcludeFromCapture(Window window)
		{
			if (window == null) return false;
			try
			{
				var helper = new WindowInteropHelper(window);
				var handle = helper.EnsureHandle();
				return SetWindowDisplayAffinity(handle, WDA_EXCLUDEFROMCAPTURE);
			}
			catch
			{
				return false;
			}
		}

		public static bool RemoveExcludeFromCapture(Window window)
		{
			if (window == null) return false;
			try
			{
				var helper = new WindowInteropHelper(window);
				var handle = helper.EnsureHandle();
				return SetWindowDisplayAffinity(handle, WDA_NONE);
			}
			catch
			{
				return false;
			}
		}
	}
}


