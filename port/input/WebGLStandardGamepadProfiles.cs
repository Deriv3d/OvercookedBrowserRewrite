// InControl device profiles for gamepads in the browser.
//
// On WebGL, Unity's old Input class reads pads through the browser Gamepad API.
// Chrome gives a pad it recognises the W3C "standard" layout (Unity's WebGL
// manual: "aligns with the W3 spec"), and its name looks like
//   "Wireless Controller (STANDARD GAMEPAD Vendor: 054c Product: 09cc)".
// None of InControl's own profiles match that name, and the closest one
// (PlayStation4WinProfile) uses the Windows DirectInput button order, which is
// different. Without these profiles a DS4 lands on InControl's "unknown device"
// fallback, which maps Button0..19 to InputControlType.Button0..19 and so
// drives none of the game's actions.
//
// Registered by the patch "incontrol-webgl-profiles" (tools/apply_patches.py),
// which puts both names first in InControl's profile list.
//
// Chrome hands Unity the W3C "standard" layout, but Unity's WebGL player
// re-numbers it Xbox-on-Windows style before the old Input class sees it
// (build 25 raw log, simulated DS4; browser index -> Unity index):
//   buttons 0 Cross  1 Circle  2 Square  3 Triangle  4 L1  5 R1
//           8 Share -> 6   9 Options -> 7   10 L3 -> 8   11 R3 -> 9
//           12-15 D-pad up/down/left/right stay 12-15
//           L2/R2 (6, 7) are not buttons at all
//   axes    0 left X  1 left Y (-1 up)  3 right X  4 right Y (-1 up)
//           5 D-pad X (+ right)  6 D-pad Y (+ up)
//           8 L2  9 R2 (0 released .. 1 pressed)
//           2 both triggers in one (L2 +, R2 -), not used here
// Build 15-25 used the browser numbers directly, which put L2/R2 on the
// right stick's X and right X on right Y.
//
// Compiled on every platform on purpose: the profile list names these types,
// and InControl throws on a name it can't load. Outside a browser no pad name
// matches, so they never attach.

using InControl;

namespace OvercookedPort
{
	// Shared mapping for any pad Chrome reports with the standard layout.
	public abstract class WebGLStandardMappingProfile : UnityInputDeviceProfile
	{
		protected WebGLStandardMappingProfile()
		{
			// InControl's platform string in the browser is the OS plus the
			// browser (build 14: "WINDOWS 10 CHROME 155.0.0.0"); these names
			// never appear outside a browser, so no platform filter is needed.
			SupportedPlatforms = null;

			// InControl 1.5.12 also gates every profile on the Unity version,
			// and the game's 2022.3.62 fails it (build 14 [pad-diag]:
			// IsSupportedOnThisVersionOfUnity=False, which also makes
			// IsSupportedOnThisPlatform false). Open the range for ours only;
			// outside a browser no joystick name matches them anyway.
			MinUnityVersion = new VersionInfo( 0, 0, 0, 0 );
			MaxUnityVersion = new VersionInfo( int.MaxValue, 0, 0, 0 );

			ButtonMappings = new[] {
				Map( "Cross", InputControlType.Action1, 0 ),
				Map( "Circle", InputControlType.Action2, 1 ),
				Map( "Square", InputControlType.Action3, 2 ),
				Map( "Triangle", InputControlType.Action4, 3 ),
				Map( "L1", InputControlType.LeftBumper, 4 ),
				Map( "R1", InputControlType.RightBumper, 5 ),
				// The game's StandardActionSet reads Back (the Xbox View button),
				// which is the left half's pause in a split pad; Share is the
				// DS4's button in that spot, so it drives Back.
				Map( "Share", InputControlType.Back, 6 ),
				Map( "Options", InputControlType.Start, 7 ),
				Map( "L3", InputControlType.LeftStickButton, 8 ),
				Map( "R3", InputControlType.RightStickButton, 9 ),
				Map( "DPad Up", InputControlType.DPadUp, 12 ),
				Map( "DPad Down", InputControlType.DPadDown, 13 ),
				Map( "DPad Left", InputControlType.DPadLeft, 14 ),
				Map( "DPad Right", InputControlType.DPadRight, 15 ),
				// PS and the touchpad click aren't mapped: where Unity puts
				// them wasn't measured, and the game uses neither.
			};

			// InControl 1.5 splits each stick into four directions. Its helpers
			// read Left/Up from the negative half of the axis and Right/Down from
			// the positive half, which matches the browser (up = -1), as in the
			// game's own PlayStation4WinProfile.
			AnalogMappings = new[] {
				LeftStickLeftMapping( Analog( 0 ) ),
				LeftStickRightMapping( Analog( 0 ) ),
				LeftStickUpMapping( Analog( 1 ) ),
				LeftStickDownMapping( Analog( 1 ) ),
				RightStickLeftMapping( Analog( 3 ) ),
				RightStickRightMapping( Analog( 3 ) ),
				RightStickUpMapping( Analog( 4 ) ),
				RightStickDownMapping( Analog( 4 ) ),
				// L2/R2 exist only as axes; the game reads them as LeftTrigger /
				// RightTrigger (split chop and Cancel).
				Trigger( "L2", InputControlType.LeftTrigger, 8 ),
				Trigger( "R2", InputControlType.RightTrigger, 9 ),
			};
		}

		static InputControlMapping Trigger( string handle, InputControlType target, int axis )
		{
			return new InputControlMapping {
				Handle = handle,
				Target = target,
				Source = Analog( axis ),
				SourceRange = InputRange.ZeroToOne,
				TargetRange = InputRange.ZeroToOne
			};
		}

		static InputControlMapping Map( string handle, InputControlType target, int button )
		{
			return new InputControlMapping {
				Handle = handle,
				Target = target,
				Source = Button( button )
			};
		}
	}


	// Sony DualShock 4: v1, v2 and the Sony wireless adapter, over USB or
	// Bluetooth. Unity cuts the joystick name at 63 characters (seen in
	// build 10: "Wireless Controller (STANDARD GAMEPAD Vendor: 054c Product: 09c"),
	// so the product id can't be relied on; Sony's vendor id plus Chrome's
	// "STANDARD GAMEPAD" (written only when it applied the standard layout)
	// is enough, since every Sony pad Chrome remaps uses the same layout.
	// A pad whose name is long enough to lose the vendor id too falls to the
	// generic profile below, which has the same mapping.
	// Other browsers name pads differently (untested guesses, iPhone Safari
	// didn't match the Chrome patterns):
	//   Firefox      "054c-09cc-Wireless Controller"
	//   macOS Safari "54c-9cc-Wireless Controller" (hex without leading zeros)
	//   iOS Safari   Apple's controller name, e.g. "DUALSHOCK 4 Wireless
	//                Controller", sometimes with " Extended Gamepad" added
	public class WebGLDualShock4Profile : WebGLStandardMappingProfile
	{
		public WebGLDualShock4Profile()
		{
			Name = "PlayStation 4 Controller";
			Meta = "PlayStation 4 Controller in a browser (standard mapping)";

			JoystickRegex = new[] {
				@"STANDARD GAMEPAD Vendor: 054c",
				@"^0*54c-",
				@"DUALSHOCK",
				@"^Wireless Controller",
			};
		}
	}


	// Any other pad the browser maps to the standard layout (Xbox pads…).
	// Checked after the DS4 profile, and only when nothing else matched.
	// Chrome writes "STANDARD GAMEPAD" into the name; Safari doesn't, so any
	// name saying Gamepad or Controller counts too (Safari only exposes pads
	// it can map to the standard layout).
	public class WebGLStandardGamepadProfile : WebGLStandardMappingProfile
	{
		public WebGLStandardGamepadProfile()
		{
			Name = "Gamepad";
			Meta = "Gamepad in a browser (standard mapping)";

			LastResortRegex = "STANDARD GAMEPAD|Gamepad|Controller";
		}
	}
}
