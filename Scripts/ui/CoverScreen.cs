using Godot;

namespace Booom202604;

/// <summary>
/// 游戏封面：开始 → 当前主菜单（初始界面）；结束 → 退出进程。
/// </summary>
public partial class CoverScreen : Control
{
	const string InitialMenuScene = "res://Scenes/main_menu.tscn";

	public void _on_begin_pressed()
	{
		GetTree().ChangeSceneToFile(InitialMenuScene);
	}

	public void _on_exit_pressed()
	{
		GetTree().Quit();
	}
}
