// D-pad menu navigation on a whole pad (not in
// Steam). The game's menus move with UIUp/UIDown/UILeft/UIRight, which only
// read the sticks (both mapping assets list StickX = [LStickX, RStickX] and
// StickY = [LStickY, RStickY], no D-pad). This adds the pad's D-pad to those
// four buttons for whole-pad players only (PadSide.Both): on a split pad the
// left half's D-pad is pickup/chop/dash/switch and stays that way. Levels are
// unchanged because the chef moves with the stick values, not these buttons.

public static class WebGLPortMenuDpad
{
	public static ILogicalButton With(ILogicalButton _stick, PlayerGameInput _input, PlayerInputLookup.LogicalButtonID _id)
	{
		if (_input == null || _input.Side != PadSide.Both)
		{
			return _stick;
		}
		bool vertical = _id == PlayerInputLookup.LogicalButtonID.UIUp || _id == PlayerInputLookup.LogicalButtonID.UIDown;
		ILogicalValue dpad = new LogicalPadValue<PCPadInputProvider>(_input.Pad, vertical ? ControlPadInput.Value.DPadY : ControlPadInput.Value.DPadX);
		// DPadY is + for up (StandardActionSet), while the stick's UIUp is StickY < -0.75.
		bool positive = _id == PlayerInputLookup.LogicalButtonID.UIUp || _id == PlayerInputLookup.LogicalButtonID.UIRight;
		ILogicalButton button = positive
			? new ValueThresholdButton(dpad, ValueThresholdButton.ThresholdType.Greater, 0.75f)
			: new ValueThresholdButton(dpad, ValueThresholdButton.ThresholdType.LessThan, -0.75f);
		return new ComboLogicalButton(new ILogicalButton[2] { _stick, button });
	}
}
