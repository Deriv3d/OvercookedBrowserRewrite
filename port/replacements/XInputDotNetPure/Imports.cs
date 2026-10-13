// Replaces XInputDotNetPure's native imports.
//
// XInputInterface.dll is Windows-only. In the browser every pad reports "not
// connected", so XInputProvider and InControl's XInputDeviceManager see no
// devices; gamepads come through Unity's own joystick input instead.
// The Windows editor keeps the real plugin when it is present.

using System;
#if !UNITY_WEBGL || UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace XInputDotNetPure
{
	internal class Imports
	{
		internal const string DLLName = "XInputInterface";

#if UNITY_WEBGL && !UNITY_EDITOR
		private const uint ERROR_DEVICE_NOT_CONNECTED = 1167;

		public static uint XInputGamePadGetState(uint playerIndex, IntPtr state)
		{
			return ERROR_DEVICE_NOT_CONNECTED;
		}

		public static void XInputGamePadSetState(uint playerIndex, float leftMotor, float rightMotor)
		{
		}
#else
		[DllImport(DLLName)]
		public static extern uint XInputGamePadGetState(uint playerIndex, IntPtr state);

		[DllImport(DLLName)]
		public static extern void XInputGamePadSetState(uint playerIndex, float leftMotor, float rightMotor);
#endif
	}
}
