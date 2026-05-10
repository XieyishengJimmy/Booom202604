using Godot;

namespace Booom202604;

/// <summary>
/// 主线最后一关经传送门通关后的结算示意图；任意点击/触摸/按键返回主菜单。
/// </summary>
public partial class VictoryScreen : Control
{
	const string MainMenuScene = "res://Scenes/main_menu.tscn";

	bool _returning;

	void ReturnToMainMenu()
	{
		if (_returning || !IsInsideTree())
			return;
		_returning = true;
		RunState.Instance?.PrepareReturnToMainMenu();
		GetTree().ChangeSceneToFile(MainMenuScene);
	}

	public override void _Input(InputEvent @event)
	{
		switch (@event)
		{
			case InputEventMouseButton mb when mb.Pressed:
			case InputEventScreenTouch st when st.Pressed && st.Index == 0:
			case InputEventKey key when key.Pressed && !key.Echo:
				ReturnToMainMenu();
				GetViewport().SetInputAsHandled();
				return;
		}
	}
}
