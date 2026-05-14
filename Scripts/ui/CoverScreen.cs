using Godot;

namespace Booom202604;

/// <summary>
/// 游戏封面：开始 → 当前主菜单（初始界面）；结束 → 退出进程。
/// </summary>
public partial class CoverScreen : Control
{
	const string InitialMenuScene = "res://Scenes/main_menu.tscn";
	const string GameplayScene = "res://Scenes/gameplay.tscn";

	public void _on_begin_pressed()
	{
		// 编辑器内：进主菜单选关 / 开编辑器；导出包：直接进主线第一关（最小 campaign_order，不含实训 999）。
		if (RunState.IsEditorPlaySession)
		{
			GetTree().ChangeSceneToFile(InitialMenuScene);
			return;
		}

		if (RunState.Instance == null)
		{
			GD.PrintErr("[CoverScreen] RunState 未加载，退回主菜单。");
			GetTree().ChangeSceneToFile(InitialMenuScene);
			return;
		}

		RunState.Instance.PrepareReturnToMainMenu();
		RunState.Instance.PendingLevelPath = RunState.ResolveShippedEntryLevelPath();
		RunState.Instance.DebugModeVerboseToasts = false;
		GetTree().ChangeSceneToFile(GameplayScene);
	}

	public void _on_exit_pressed()
	{
		GetTree().Quit();
	}
}
