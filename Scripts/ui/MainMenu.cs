using Godot;

namespace Booom202604;

public partial class MainMenu : Control
{
	OptionButton? _levelPick;

	public override void _Ready()
	{
		_levelPick = GetNodeOrNull<OptionButton>("VBox/LevelPickOpt");
		FillLevelDropdown();

		if (_levelPick != null)
			StyleLevelDropdown(_levelPick, 22);
	}

	static void StyleLevelDropdown(OptionButton ob, int px)
	{
		ob.AddThemeFontSizeOverride("font_size", px);
		ob.GetPopup().AddThemeFontSizeOverride("font_size", px);
	}

	void FillLevelDropdown()
	{
		if (_levelPick == null)
			return;

		LevelCatalog.EnsureDirectoryExists();
		_levelPick.Clear();

		_levelPick.AddItem("— 请选择关卡 —");
		_levelPick.SetItemMetadata(0, "");

		foreach (string p in LevelCatalog.EnumerateLevelJsonPathsSortedAscending())
		{
			int ix = _levelPick.ItemCount;
			_levelPick.AddItem(LevelCatalog.GetDropdownLabel(p));
			_levelPick.SetItemMetadata(ix, p);
		}

		if (_levelPick.ItemCount > 1)
			_levelPick.Select(1);
		else
			_levelPick.Select(0);
	}

	void PopupBrief(string title, string body)
	{
		var dlg = new AcceptDialog { Title = title, DialogText = body };
		AddChild(dlg);
		dlg.Confirmed += () => dlg.QueueFree();
		dlg.Canceled += () => dlg.QueueFree();
		dlg.CloseRequested += () => dlg.QueueFree();
		dlg.PopupCentered();
	}

	public void _on_play_pressed()
	{
		if (_levelPick == null)
			return;

		string path = _levelPick.GetItemMetadata(_levelPick.Selected).AsString();
		if (string.IsNullOrEmpty(path))
		{
			PopupBrief("无法开始", "请先在列表中选择一份关卡 JSON（目录 res://levels/）。");
			return;
		}

		if (RunState.Instance == null)
		{
			GD.PrintErr("[MainMenu] RunState autoload 缺失：请在 project.godot 的 [autoload] 中注册 RunState。");
			PopupBrief("无法开始", "全局单例 RunState 未加载。请在编辑器 项目设置 → AutoLoad 添加 Script autoload：res://Scripts/autoload/RunState.cs，命名为 RunState。");
			return;
		}

		RunState.Instance.PendingLevelPath = path;
		GetTree().ChangeSceneToFile("res://Scenes/gameplay.tscn");
	}

	public void _on_open_editor()
	{
		GetTree().ChangeSceneToFile("res://Scenes/level_editor.tscn");
	}

	public void _on_quit()
	{
		GetTree().Quit();
	}
}
