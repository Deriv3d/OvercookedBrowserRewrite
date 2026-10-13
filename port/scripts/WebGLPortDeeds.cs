// Deeds (the game's achievement events) are off in the browser.
//
// GameUtils.FireDeed runs on every chop, every served order and at the end of
// a level. On WebGL each call threw (System.Reflection.TargetException,
// "Instance constructor requires a target"), caught by the
// deed-failure-not-fatal patch and logged with its stack trace. Building and
// logging that exception costs a frame spike, and testing showed an FPS drop each
// time an order was turned in on a phone. Achievements
// aren't wanted yet, so FireDeed returns at once; ?deeds=1 runs them
// (still guarded), for working on achievements later.

using UnityEngine;

public static class WebGLPortDeeds
{
	public static readonly bool Enabled = WebGLPortDevFlags.Query(Application.absoluteURL, "deeds") == "1";
}
