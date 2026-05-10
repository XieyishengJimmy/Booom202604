using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace Booom202604;

/// <summary>In-game hex level editor.</summary>
public partial class LevelEditor : Control
{
	enum EditorMode
	{
		AddTerrain,
		ToggleFog,
		SetPlayerStart,
		EraseEvent,
	}

	/// <summary>PickId：列表里唯一；GameType：写入关卡 JSON 的 type（玩法逻辑）。</summary>
	static readonly (string PickId, string GameType, string Tex, string Zh)[] OtherEvents =
	[
		("place_block", "place_block", "res://Art/Icon/Obstacle.png", "障碍"),
		("clear_block", "clear_block", "res://Art/Icon/GrassPatch1.png", "清除物件"),
		("treasure", "treasure", "res://Art/Icon/TreasureChest.png", "宝箱"),
		("altar", "altar", "res://Art/Icon/Altar_Useful.png", "祭坛"),
		("grass_patch1", "grass", "res://Art/Icon/GrassPatch1.png", "草丛 · 草甸一"),
		("grass_patch2", "grass", "res://Art/Icon/GrassPatch2.png", "草丛 · 草甸二"),
		("corpse", "corpse", "res://Art/Icon/Grave.png", "残骸"),
		("ruins_shrine", "ruins", "res://Art/Icon/AbandonedShrine.png", "遗迹 · 神殿遗迹"),
		("ruins_church", "ruins", "res://Art/Icon/AbandonedChurch.png", "遗迹 · 教堂残骸"),
	];

	EditorMode _mode = EditorMode.AddTerrain;

	readonly Godot.Collections.Dictionary _cells = [];
	readonly Godot.Collections.Dictionary _fog = [];
	readonly Godot.Collections.Dictionary _blk = [];
	readonly Godot.Collections.Dictionary _ev = [];
	Vector2I _player;

	int _pickMonsterIdx = -1;
	string _pickOther = "";

	bool _panMmb;

	TileMapLayer? _terrain;
	FogLayer? _fogLayer;
	BlockLayer? _blockLayer;
	SubViewport? _vp;
	SubViewportContainer? _wrap;
	Panel? _sidebar;
	Camera2D? _cam;
	OptionButton? _modeOpt;
	LineEdit? _levelNameEdit;


	SpinBox? _levelCampaignOrderSpin;


	OptionButton? _levelFilesOpt;

	OptionButton? _terrainStyleOpt;
	bool _suppressTerrainStyle;
	int _terrainVariant = 1;

	/// <summary>res:// 关卡文件；空表示「新建未落盘」草稿。</summary>
	string _currentLevelPath = "";

	bool _suppressLevelDropdown;
	OptionButton? _bossIdOpt;
	Sprite2D? _ghost;
	Label? _modeHint;
	Label? _placementHint;
	GridContainer? _monsterGrid;
	GridContainer? _otherGrid;
	ScrollContainer? _monsterScroll;
	ScrollContainer? _otherScroll;
	ScrollContainer? _sidebarScroll;

	bool _editorPendingRefit;
	/// <summary>拖拽分割条时 Resize 风暴会积压海量 CallDeferred；合并为每帧至多一次视口刷新。</summary>
	bool _viewportLayoutRefreshDeferred;
	/// <summary>分割条拖动中不更新子视口分辨率（避免与侧栏布局循环喂彼此）。</summary>
	bool _splitDragActive;
	double _exportedJsonPoll;
	DateTime _lastMonsterJsonUtc;
	DateTime _lastBossJsonUtc;

	readonly ButtonGroup _monsterGrp = new() { AllowUnpress = true };
	readonly ButtonGroup _otherGrp = new() { AllowUnpress = true };

	const string SidebarVBox = "HBox/Sidebar/SidebarScroll/VBox";

	/// <summary>92 / 56 基准 × 0.5；再在当前结果上 ×2（放大 100%）即与基准等大。</summary>
	const int MonsterPickerIconBasePx = 92;
	const int ScenePickerIconBasePx = 56;
	const float PickerIconScale = 0.5f;
	/// <summary>相对「缩放后图标」额外放大倍数：2＝比现在再大 100%。</summary>
	const float PickerIconDisplayBoost = 2f;

	/// <summary>Flow 每项最小宽度（避免大字挤成单列）。</summary>
	const int MonsterPickerCellMinWidthPx = 118;
	const int ScenePickerCellMinWidthPx = 100;

	/// <summary>与侧栏两处 GridContainer 的 theme_override h_separation 一致（level_editor.tscn）。用于按宽度折算列数。</summary>
	const int PickerGridHSeparationPx = 10;

	static int PickerIconPixelSize(int basePx, float shrink, float boost)
	{
		int half = Mathf.Max(16, Mathf.RoundToInt(basePx * shrink));
		int boosted = Mathf.Max(16, Mathf.RoundToInt(half * boost));
		return boosted;
	}

	static DateTime ExportedJsonUtc(string resPath)
	{
		string abs = ProjectSettings.GlobalizePath(resPath);
		if (!File.Exists(abs))
			return DateTime.MinValue;
		try { return File.GetLastWriteTimeUtc(abs); }
		catch { return DateTime.MinValue; }
	}

	/// <summary>与战斗中玩家 idle 同源（Gameplay <c>PlayerIdleTextureCandidates</c>）。</summary>
	static Texture2D? EditorLoadPlayerIdleTexture()
	{
		foreach (string p in new[] { "res://Art/Player/idle.png", "res://Art/Player/idel.png" })
		{
			if (ResourceLoader.Exists(p))
				return GD.Load<Texture2D>(p);
		}

		return null;
	}

	static void BumpSubtreeFontSizes(Control root, float mult)
	{
		if (root is Label or Button or LineEdit or SpinBox or OptionButton)
		{
			int cur = root.GetThemeFontSize("font_size");
			if (cur <= 0)
				cur = 14;
			root.AddThemeFontSizeOverride("font_size", Mathf.RoundToInt(cur * mult));
		}

		foreach (Node ch in root.GetChildren())
		{
			if (ch is Control c)
				BumpSubtreeFontSizes(c, mult);
		}
	}

	static void StyleOptionDropdown(OptionButton ob, int fontPx)
	{
		ob.AddThemeFontSizeOverride("font_size", fontPx);
		ob.GetPopup().AddThemeFontSizeOverride("font_size", fontPx);
	}

	public override void _Ready()
	{
		MonsterTable.Reload(MonsterTable.DefaultResourcePath);
		BossTable.Reload(BossTable.DefaultResourcePath);

		LevelCatalog.EnsureDirectoryExists();

		_sidebarScroll = GetNodeOrNull<ScrollContainer>("HBox/Sidebar/SidebarScroll");
		if (_sidebarScroll != null)
		{
			_sidebarScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
			_sidebarScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
		}

		_sidebar = GetNode<Panel>("HBox/Sidebar");
		_modeOpt = GetNode<OptionButton>($"{SidebarVBox}/ModeOpt");
		_levelNameEdit = GetNode<LineEdit>($"{SidebarVBox}/LevelNameEdit");
		_levelCampaignOrderSpin = GetNodeOrNull<SpinBox>($"{SidebarVBox}/CampaignOrderSpin");
		if (_levelCampaignOrderSpin != null)
		{
			_levelCampaignOrderSpin.MinValue = 1;
			_levelCampaignOrderSpin.MaxValue = 999_999;
			_levelCampaignOrderSpin.Step = 1;
			_levelCampaignOrderSpin.Rounded = true;
			_levelCampaignOrderSpin.Value = 1;
		}
		_levelFilesOpt = GetNode<OptionButton>($"{SidebarVBox}/LevelFilesOpt");
		_bossIdOpt = GetNode<OptionButton>($"{SidebarVBox}/BossIdOpt");
		_modeHint = GetNodeOrNull<Label>($"{SidebarVBox}/ModeHint");
		_placementHint = GetNodeOrNull<Label>($"{SidebarVBox}/PlacementHint");
		_monsterGrid = GetNodeOrNull<GridContainer>($"{SidebarVBox}/MonsterScroll/MonsterPickGrid");
		_otherGrid = GetNodeOrNull<GridContainer>($"{SidebarVBox}/OtherScroll/OtherPickGrid");
		_monsterScroll = GetNodeOrNull<ScrollContainer>($"{SidebarVBox}/MonsterScroll");
		if (_monsterScroll != null)
		{
			// 多列网格最宽可能超过视区，横向滚动以避免裁切；列数随后续宽变化由 UpdatePickerGridColumnsForScrollWidth 调整。
			_monsterScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;
			_monsterScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
			_monsterScroll.Resized += OnPickerViewportResized;
		}

		_otherScroll = GetNodeOrNull<ScrollContainer>($"{SidebarVBox}/OtherScroll");
		if (_otherScroll != null)
		{
			_otherScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Auto;
			_otherScroll.VerticalScrollMode = ScrollContainer.ScrollMode.Auto;
			_otherScroll.Resized += OnPickerViewportResized;
		}

		if (_sidebar != null)
			_sidebar.Resized += OnPickerViewportResized;

		_wrap = GetNode<SubViewportContainer>("HBox/ViewportContainer");
		_vp = GetNode<SubViewport>("HBox/ViewportContainer/Viewport");

		_cam = GetNodeOrNull<Camera2D>("HBox/ViewportContainer/Viewport/Camera2D");
		_terrain = GetNode<TileMapLayer>("HBox/ViewportContainer/Viewport/World/TerrainLayer");
		_fogLayer = GetNode<FogLayer>("HBox/ViewportContainer/Viewport/World/FogRoot");
		_blockLayer = GetNode<BlockLayer>("HBox/ViewportContainer/Viewport/World/BlockRoot");
		_ghost = GetNode<Sprite2D>("HBox/ViewportContainer/Viewport/World/PlayerGhost");
		_terrainStyleOpt = GetNodeOrNull<OptionButton>($"{SidebarVBox}/TerrainStyleOpt");

		if (_monsterGrid != null && _otherGrid != null)
		{
			BuildMonsterPicker();
			BuildOtherPicker();
			_pickMonsterIdx = -1;
			_pickOther = "";
			UpdatePlacementHint();
			RefreshPickGlow();
			Callable.From(UpdatePickerGridColumnsForScrollWidth).CallDeferred();
		}

		RebuildBossIdOptions();

		BuildTerrainStyleDropdown();
		ApplyTerrainVariantToTerrain();

		Texture2D? idleGhost = EditorLoadPlayerIdleTexture();
		if (_ghost != null)
		{
			Texture2D? useGhost = idleGhost ?? GD.Load<Texture2D>("res://Art/Role/player.png");
			if (useGhost != null)
				_ghost.Texture = useGhost;
			RefreshEditorPlayerGhostScale();
		}

		int bossIdPick = PickDefaultBossIdForBlank();

		_seedUiModes();

		foreach (Node ch in GetNode<VBoxContainer>(SidebarVBox).GetChildren())
		{
			if (ch.Name == "MonsterScroll" || ch.Name == "OtherScroll")
				continue;

			if (ch is Control bumpRoot)
				BumpSubtreeFontSizes(bumpRoot, 2.0f);
		}


		var editorSplit = GetNode<HSplitContainer>("HBox");
		editorSplit.DragStarted += OnEditorSplitDragStarted;
		editorSplit.DragEnded += OnEditorSplitDragEnded;

		RebuildLevelFileDropdown();

		SyncSidebarDropdownMenus();

		_modeOpt!.ItemSelected += _ =>
		{
			_mode = (EditorMode)Mathf.Clamp(_modeOpt!.Selected, 0, (int)EditorMode.EraseEvent);
			UpdateModeHelp();
			UpdatePlacementHint();
		};

		_wrap!.GuiInput += OnViewportContainerGuiInput;
		_wrap.Resized += OnWrapResizedLayout;

		string starter = $"{LevelCatalog.ResourceDir}/starter_level.json";

		if (Godot.FileAccess.FileExists(starter))
			LoadLevelFromPath(starter, rebuildList: false);
		else if (_levelFilesOpt!.ItemCount > 1)
			LoadLevelFromPath(_levelFilesOpt.GetItemMetadata(1).AsString(), rebuildList: false);
		else
			ApplyNewBlankDraft(bossIdPick);

		SelectDropdownByPathOrNone(_currentLevelPath);

		UpdateModeHelp();
		_lastMonsterJsonUtc = ExportedJsonUtc(MonsterTable.DefaultResourcePath);
		_lastBossJsonUtc = ExportedJsonUtc(BossTable.DefaultResourcePath);
		SetProcess(true);
		Callable.From(EditorDeferredLayoutBootstrap).CallDeferred();
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
		_exportedJsonPoll += delta;
		if (_exportedJsonPoll < 1.25)
			return;
		_exportedJsonPoll = 0d;
		TryReloadExportedTablesFromDisk();
	}

	/// <summary>monsters.json / bosses.json 被导表改写后无需重启编辑器。</summary>
	void TryReloadExportedTablesFromDisk()
	{
		DateTime wm = ExportedJsonUtc(MonsterTable.DefaultResourcePath);
		DateTime wb = ExportedJsonUtc(BossTable.DefaultResourcePath);

		bool wantMonsters = wm != DateTime.MinValue && wm > _lastMonsterJsonUtc;
		bool wantBosses = wb != DateTime.MinValue && wb > _lastBossJsonUtc;
		if (!wantMonsters && !wantBosses)
			return;

		int keepBoss = SelectedBossId();

		if (wantMonsters)
		{
			MonsterTable.Reload(MonsterTable.DefaultResourcePath);
			if (_monsterGrid != null && _otherGrid != null)
			{
				BuildMonsterPicker();
				if (_pickMonsterIdx >= MonsterTable.All.Count)
				{
					_pickMonsterIdx = -1;
					UncheckMonsters();
				}

				Callable.From(UpdatePickerGridColumnsForScrollWidth).CallDeferred();
			}

			_lastMonsterJsonUtc = wm;
			GD.Print("关卡编辑器：已热加载 Data/monsters.json");
		}

		if (wantBosses)
		{
			BossTable.Reload(BossTable.DefaultResourcePath);
			RebuildBossIdOptions();
			SelectBossDropdown(keepBoss);
			_lastBossJsonUtc = wb;
			GD.Print("关卡编辑器：已热加载 Data/bosses.json");
		}

		SyncSidebarDropdownMenus();
		UpdatePlacementHint();
		RefreshPickGlow();
	}

	void EditorDeferredLayoutBootstrap()
	{
		ApplyPreferredEditorSplitOnceForViewport();
		EditorRefreshViewportResolutionAndCamera(true);
		ClampEditorSplit();
		Callable.From(UpdatePickerGridColumnsForScrollWidth).CallDeferred();
	}

	/// <summary>按滚动区实时宽度折算列数：向左拖分割条拓宽侧栏时，单列可显示更多图标、减少横向滚动。</summary>
	void OnPickerViewportResized() =>
		Callable.From(UpdatePickerGridColumnsForScrollWidth).CallDeferred();

	void UpdatePickerGridColumnsForScrollWidth()
	{
		if (_monsterGrid == null || _monsterScroll == null || _otherGrid == null || _otherScroll == null)
			return;

		int iconPxM = PickerIconPixelSize(MonsterPickerIconBasePx, PickerIconScale, PickerIconDisplayBoost);
		int cellWm = Mathf.Max(MonsterPickerCellMinWidthPx, iconPxM + 16);
		float innerM = Mathf.Max(48f, _monsterScroll.Size.X);
		int sep = PickerGridHSeparationPx;
		float unitM = cellWm + sep;
		int colsM = unitM <= 1f ? 2 : Mathf.FloorToInt((innerM + sep) / unitM);
		_monsterGrid.Columns = Mathf.Clamp(colsM, 2, 24);

		int iconPxO = PickerIconPixelSize(ScenePickerIconBasePx, PickerIconScale, PickerIconDisplayBoost);
		int cellWo = Mathf.Max(ScenePickerCellMinWidthPx, iconPxO + 16);
		float innerO = Mathf.Max(48f, _otherScroll.Size.X);
		float unitO = cellWo + sep;
		int colsO = unitO <= 1f ? 2 : Mathf.FloorToInt((innerO + sep) / unitO);
		_otherGrid.Columns = Mathf.Clamp(colsO, 2, 24);
	}

	void ApplyPreferredEditorSplitOnceForViewport()
	{
		var split = GetNodeOrNull<HSplitContainer>("HBox");
		if (split == null)
			return;

		Vector2 vp = GetViewportRect().Size;

		if (vp.X < 200f)
			return;

		float sideW = _sidebar?.CustomMinimumSize.X ?? 360f;

		int off = Mathf.Clamp((int)(vp.X * 0.70f), 900,
			Mathf.Max(910, (int)vp.X - (int)sideW - 96));

		split.SplitOffsets = new[] { off };
	}

	void OnEditorSplitDragStarted()
	{
		_splitDragActive = true;
		_viewportLayoutRefreshDeferred = false;
	}

	void OnEditorSplitDragEnded()
	{
		_splitDragActive = false;
		ClampEditorSplit();
		EditorRefreshViewportResolutionAndCamera(true);
		Callable.From(UpdatePickerGridColumnsForScrollWidth).CallDeferred();
	}

	void ClampEditorSplit() =>
		GetNodeOrNull<HSplitContainer>("HBox")?.ClampSplitOffset(0);

	void SyncSidebarDropdownMenus()
	{
		int pxMode = Mathf.Max(1, _modeOpt?.GetThemeFontSize("font_size") ?? 16);
		int pxBoss = Mathf.Max(1, _bossIdOpt?.GetThemeFontSize("font_size") ?? pxMode);
		int px = Mathf.Max(pxMode, pxBoss);
		if (_modeOpt != null)
			StyleOptionDropdown(_modeOpt, px);
		if (_bossIdOpt != null)
			StyleOptionDropdown(_bossIdOpt, px);
		if (_levelFilesOpt != null)
			StyleOptionDropdown(_levelFilesOpt, px);
		if (_terrainStyleOpt != null)
			StyleOptionDropdown(_terrainStyleOpt, px);
	}

	void BuildTerrainStyleDropdown()
	{
		if (_terrainStyleOpt == null)
			return;

		_suppressTerrainStyle = true;
		_terrainStyleOpt.Clear();
		string[] paths = TerrainTilesetFactory.MapTexturePaths;
		for (int i = 0; i < paths.Length; i++)
		{
			int v = i + 1;
			_terrainStyleOpt.AddItem($"地砖样式 {v} · Art/Map/{v}.png");
			_terrainStyleOpt.SetItemMetadata(i, v);
		}

		int idx = Mathf.Clamp(_terrainVariant - 1, 0, paths.Length - 1);
		_terrainStyleOpt.Select(idx);
		_suppressTerrainStyle = false;
	}

	void SyncTerrainStyleDropdown()
	{
		if (_terrainStyleOpt == null || _terrainStyleOpt.ItemCount == 0)
			return;

		_suppressTerrainStyle = true;
		int idx = Mathf.Clamp(_terrainVariant - 1, 0, _terrainStyleOpt.ItemCount - 1);
		_terrainStyleOpt.Select(idx);
		_suppressTerrainStyle = false;
	}

	void ApplyTerrainVariantToTerrain()
	{
		if (_terrain == null)
			return;

		_terrainVariant = TerrainTilesetFactory.ClampTerrainVariant(_terrainVariant);
		_terrain.TileSet = TerrainTilesetFactory.CreateHexTileset(_terrainVariant);
		TerrainTilesetFactory.ApplyTerrainPresentation(_terrain);
		RefreshEditorPlayerGhostScale();
	}

	void RefreshEditorPlayerGhostScale()
	{
		if (_ghost == null)
			return;

		_ghost.Scale = TerrainTilesetFactory.PlayerWorldScaleMapSceneReference;
	}

	public void _on_terrain_style_selected(long index)
	{
		if (_suppressTerrainStyle)
			return;

		int v = (int)index + 1;
		if (_terrainStyleOpt != null)
		{
			Variant meta = _terrainStyleOpt.GetItemMetadata((int)index);
			if (meta.VariantType == Variant.Type.Int)
				v = meta.AsInt32();
		}

		_terrainVariant = TerrainTilesetFactory.ClampTerrainVariant(v);
		ApplyTerrainVariantToTerrain();
		RefreshVisuals();
	}

	void OnWrapResizedLayout() => RequestEditorViewportLayoutRefresh();

	void RequestEditorViewportLayoutRefresh()
	{
		if (_splitDragActive)
			return;
		if (_viewportLayoutRefreshDeferred)
			return;
		_viewportLayoutRefreshDeferred = true;
		Callable.From(FlushEditorViewportLayoutRefresh).CallDeferred();
	}

	void FlushEditorViewportLayoutRefresh()
	{
		_viewportLayoutRefreshDeferred = false;
		if (_splitDragActive)
			return;
		EditorRefreshViewportResolutionAndCamera(false);
	}

	public override void _Notification(int what)
	{
		base._Notification(what);
		if (what != NotificationResized)
			return;
		// 不在此处 ClampSplit：会与拖动中的 SplitContainer 排序互相触发；松手由 DragEnded 再 Clamp。
		RequestEditorViewportLayoutRefresh();
	}

	void EditorRefreshViewportResolutionAndCamera(bool forceFit)
	{
		if (_wrap == null || _vp == null || _cam == null || _terrain == null)
			return;

		Vector2 sz = _wrap.Size;
		if (sz.X < 4f || sz.Y < 4f)
			return;

		const int MaxAxis = 8192;
		Vector2I next = new(
			Mathf.Clamp(Mathf.Max(8, Mathf.CeilToInt(sz.X)), 8, MaxAxis),
			Mathf.Clamp(Mathf.Max(8, Mathf.CeilToInt(sz.Y)), 8, MaxAxis));

		bool changed = _vp.Size != next;
		if (changed)
			_vp.Size = next;

		if (!(changed || forceFit || _editorPendingRefit))
			return;

		FitCam();
		_cam.MakeCurrent();
		_editorPendingRefit = false;
	}

	void ApplyMonsterPickToggled(int idx, bool pressed)
	{
		if (!pressed)
		{
			if (_pickMonsterIdx == idx)
				_pickMonsterIdx = -1;
			RefreshPickGlow();
			UpdatePlacementHint();
			return;
		}

		_pickMonsterIdx = idx;
		_pickOther = "";
		UncheckOthers();
		RefreshPickGlow();
		UpdatePlacementHint();
	}

	void ApplyOtherPickToggled(string type, bool pressed)
	{
		if (!pressed)
		{
			if (_pickOther == type)
				_pickOther = "";
			RefreshPickGlow();
			UpdatePlacementHint();
			return;
		}

		_pickOther = type;
		_pickMonsterIdx = -1;
		UncheckMonsters();
		RefreshPickGlow();
		UpdatePlacementHint();
	}

	void BuildMonsterPicker()
	{
		if (_monsterGrid == null)
			return;

		foreach (Node child in _monsterGrid.GetChildren())
			child.QueueFree();

		int iconPx = PickerIconPixelSize(MonsterPickerIconBasePx, PickerIconScale, PickerIconDisplayBoost);
		int cellW = Mathf.Max(MonsterPickerCellMinWidthPx, iconPx + 16);

		IReadOnlyList<MonsterTable.Row> catalog = MonsterTable.All;
		if (catalog.Count == 0)
		{
			_monsterGrid.AddChild(new Label
			{
				Text = "未加载怪物（请编辑 excel/monsters.xlsx 后运行：dotnet run --project Tools/MonsterCsvToJson -- all）",
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
			});
			return;
		}

		for (int i = 0; i < catalog.Count; i++)
		{
			MonsterTable.Row t = catalog[i];
			string dominant = t.IsMagic ? "魔法检定怪物" : "力量检定怪物";
			var col = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
			col.CustomMinimumSize = new Vector2(cellW, 0);
			var tb = new TextureButton
			{
				ToggleMode = true,
				IgnoreTextureSize = true,
				StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
				CustomMinimumSize = new Vector2(iconPx, iconPx),
				SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
				SizeFlagsVertical = SizeFlags.ShrinkCenter,
				FocusMode = FocusModeEnum.None,
				TooltipText = $"{t.Name}\n{dominant} · 战力 {t.Power}\n{t.Description}",
				ButtonGroup = _monsterGrp,
			};
			Texture2D? tex = ResourceLoader.Exists(t.IconPath) ? GD.Load<Texture2D>(t.IconPath) : null;
			if (tex != null)
				tb.TextureNormal = tex;
			var cap = new Label
			{
				Text = t.Name,
				HorizontalAlignment = HorizontalAlignment.Center,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
			};
			cap.CustomMinimumSize = new Vector2(cellW - 8, 0);
			cap.MouseFilter = Control.MouseFilterEnum.Stop;

			col.AddChild(tb);
			col.AddChild(cap);

			int idx = i;
			tb.Toggled += pressed => ApplyMonsterPickToggled(idx, pressed);

			cap.GuiInput += ev =>
			{
				if (ev is InputEventMouseButton mb && mb.Pressed &&
				    mb.ButtonIndex == MouseButton.Left)
				{
					GetViewport()?.SetInputAsHandled();
					UncheckOthers();
					tb.ButtonPressed = !tb.ButtonPressed;
				}
			};

			_monsterGrid.AddChild(col);
		}
	}

	static void UncheckOtherGroup(Node row)
	{
		foreach (Node c in row.GetChildren())
		{
			if (c is BaseButton bb)
				bb.ButtonPressed = false;
			else
				UncheckOtherGroup(c);
		}
	}

	void UncheckOthers()
	{
		if (_otherGrid != null)
			UncheckOtherGroup(_otherGrid);
	}

	void UncheckMonsters()
	{
		if (_monsterGrid == null)
			return;
		foreach (Node column in _monsterGrid.GetChildren())
		{
			foreach (Node c in column.GetChildren())
			{
				if (c is BaseButton bb)
					bb.ButtonPressed = false;
			}
		}
	}

	void BuildOtherPicker()
	{
		GridContainer row = _otherGrid!;
		foreach (Node child in row.GetChildren())
			child.QueueFree();

		int iconPx = PickerIconPixelSize(ScenePickerIconBasePx, PickerIconScale, PickerIconDisplayBoost);
		int cellW = Mathf.Max(ScenePickerCellMinWidthPx, iconPx + 16);

		foreach (var o in OtherEvents)
		{
			var vb = new VBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
			vb.CustomMinimumSize = new Vector2(cellW, 0);
			var tb = new TextureButton
			{
				ToggleMode = true,
				IgnoreTextureSize = true,
				StretchMode = TextureButton.StretchModeEnum.KeepAspectCentered,
				CustomMinimumSize = new Vector2(iconPx, iconPx),
				SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
				SizeFlagsVertical = SizeFlags.ShrinkCenter,
				FocusMode = FocusModeEnum.None,
				TooltipText = $"物件：{o.Zh}",
				ButtonGroup = _otherGrp,
			};
			if (ResourceLoader.Exists(o.Tex))
				tb.TextureNormal = GD.Load<Texture2D>(o.Tex);
			string zh = o.Zh;
			string pickCaptured = o.PickId;
			tb.Toggled += pressed => ApplyOtherPickToggled(pickCaptured, pressed);

			vb.AddChild(tb);
			var zhLabel = new Label
			{
				Text = zh,
				HorizontalAlignment = HorizontalAlignment.Center,
				AutowrapMode = TextServer.AutowrapMode.WordSmart,
				SizeFlagsHorizontal = SizeFlags.ExpandFill,
				MouseFilter = Control.MouseFilterEnum.Stop,
			};
			zhLabel.CustomMinimumSize = new Vector2(cellW - 8, 0);

			zhLabel.GuiInput += ev =>
			{
				if (ev is InputEventMouseButton mb && mb.Pressed &&
				    mb.ButtonIndex == MouseButton.Left)
				{
					GetViewport()?.SetInputAsHandled();
					UncheckMonsters();
					tb.ButtonPressed = !tb.ButtonPressed;
				}
			};

			vb.AddChild(zhLabel);
			row.AddChild(vb);
		}
	}

	void UpdatePlacementHint()
	{
		if (_placementHint == null)
			return;

		if (_mode != EditorMode.AddTerrain)
		{
			if (HasPlacementPick())
				_placementHint.Text = "已选中内容：请切换到「铺设地砖」模式后在地图上写入；地图上右键也可取消选中。";
			else
				_placementHint.Text = "";
			return;
		}

		if (_pickMonsterIdx >= 0)
		{
			IReadOnlyList<MonsterTable.Row> catalog = MonsterTable.All;
			if (_pickMonsterIdx < catalog.Count)
			{
				MonsterTable.Row r = catalog[_pickMonsterIdx];
				string duel = r.IsMagic ? "魔法" : "力量";
			_placementHint.Text =
				$"将放置：「{r.Name}」（{duel}）· {r.Power} 战力 — 同一格再点相同怪物可移除；地图上右键可取消选中";
			}
			else
				_placementHint.Text = "怪物表与界面不同步；请重启编辑器或重新导表。";
		}
		else if (!string.IsNullOrEmpty(_pickOther))
		{
			if (_pickOther == "clear_block")
				_placementHint.Text = "将对格内执行：移除障碍与本格怪物/场景事件（保留地砖）；左键地砖施放。";
			else if (_pickOther == "place_block")
				_placementHint.Text =
					"将放置障碍 — 同一格再点一次可移除障碍；地图上右键可取消选中";
			else
				_placementHint.Text =
					$"将放置：{OtherZh(_pickOther)} — 同一格再点相同所选可移除事件；地图上右键可取消选中";
		}
		else
			_placementHint.Text =
				"空白处左键铺设地砖；有地砖且无选中时右键可整块移除。请先点亮怪物或场景物件再在格子上写入。";
	}

	static string OtherZh(string pickId)
	{
		foreach (var o in OtherEvents)
			if (o.PickId == pickId)
				return o.Zh;
		return pickId;
	}

	void _seedUiModes()
	{
		_modeOpt!.Clear();
		AddModeRow(EditorMode.AddTerrain, "铺设地砖（含放置内容）");
		AddModeRow(EditorMode.ToggleFog, "迷雾：未探索 ⇄ 已能看见");
		AddModeRow(EditorMode.SetPlayerStart, "放置玩家起始格");
		AddModeRow(EditorMode.EraseEvent, "移除事件");
		_modeOpt.Select((int)EditorMode.AddTerrain);
		SetTitleIfPresent();
	}

	void SetTitleIfPresent()
	{
		var title = GetNodeOrNull<Label>("HBox/Sidebar/SidebarScroll/VBox/Title");
		if (title != null)
			title.Text = "关卡编辑器";
	}

	void AddModeRow(EditorMode mode, string label)
	{
		_modeOpt!.AddItem(label);
		int idx = _modeOpt.ItemCount - 1;
		_modeOpt.GetPopup().SetItemTooltip(idx, ModeTooltip(mode));
	}

	static string ModeTooltip(EditorMode mode) => mode switch
	{
		EditorMode.AddTerrain =>
			"左键空白处铺设地砖（新格默认有迷雾）；已选怪物/物件时，在有地砖的格子上左键放置；若在已有事件的格子上再叠放「相同」所选事件，则会清除该事件；已选「障碍」时对已有障碍的格子再点则移除障碍。右键：若已选中内容则清空选择；否则删除整块地砖（含迷雾、障碍与事件）。",
		EditorMode.ToggleFog =>
			"只对已有地砖生效：迷雾「开」= 未驱散；「关」= 地图上可见。可与游戏中的吸收机制区分。左键切换。",
		EditorMode.SetPlayerStart => "点选一格设为玩家起始格（半透明幽灵预览）。",
		EditorMode.EraseEvent => "左键移除该格的事件（不改变地形与障碍）。",
		_ => "",
	};

	void UpdateModeHelp()
	{
		if (_modeHint == null)
			return;

		_modeHint.Text = _mode switch
		{
			EditorMode.AddTerrain => ModeTooltip(EditorMode.AddTerrain),
			EditorMode.ToggleFog => ModeTooltip(EditorMode.ToggleFog),
			EditorMode.SetPlayerStart => ModeTooltip(EditorMode.SetPlayerStart),
			EditorMode.EraseEvent => ModeTooltip(EditorMode.EraseEvent),
			_ => "",
		};
	}

	void OnViewportContainerGuiInput(InputEvent @event)
	{
		if (HandleViewportCamera(@event))
			GetViewport()?.SetInputAsHandled();
	}

	bool HandleViewportCamera(InputEvent @event)
	{
		if (_cam == null || _vp == null || _terrain == null)
			return false;

		if (@event is InputEventMouseButton mb)
		{
			if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
			{
				ZoomAtCursor(1.12f);
				return true;
			}

			if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed)
			{
				ZoomAtCursor(1f / 1.12f);
				return true;
			}

			if (mb.ButtonIndex == MouseButton.Middle)
			{
				_panMmb = mb.Pressed;
				return mb.Pressed;
			}
		}

		if (_panMmb && @event is InputEventMouseMotion mm)
		{
			_cam.Position -= mm.Relative / _cam.Zoom;
			return true;
		}

		return false;
	}

	void ZoomAtCursor(float factor)
	{
		Camera2D cam = _cam!;
		SubViewport vp = _vp!;
		float nz = Mathf.Clamp(cam.Zoom.X * factor, 0.2f, 5f);
		if (Mathf.IsEqualApprox(nz, cam.Zoom.X))
			return;

		Vector2 mouse = vp.GetMousePosition();
		Transform2D inv = vp.GetCanvasTransform().AffineInverse();
		Vector2 pivotWorld = inv * mouse;
		float ratio = cam.Zoom.X / nz;
		cam.Position = pivotWorld + (cam.Position - pivotWorld) * ratio;
		cam.Zoom = new Vector2(nz, nz);
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is not InputEventMouseButton mb || !mb.Pressed)
			return;

		if (mb.ButtonIndex != MouseButton.Left && mb.ButtonIndex != MouseButton.Right)
			return;

		if (_sidebar!.GetGlobalRect().HasPoint(mb.GlobalPosition))
			return;

		Rect2 rr = new(Vector2.Zero, _wrap!.Size);
		if (!rr.HasPoint(_wrap.GetLocalMousePosition()))
			return;

		if (_terrain == null || _terrain.TileSet == null)
			return;

		MouseButton btn = mb.ButtonIndex;
		Callable.From(() => Paint(btn)).CallDeferred();
	}

	void Paint(MouseButton mb)
	{
		Vector2 mx = _vp!.GetMousePosition();
		Transform2D inv = _vp.GetCanvasTransform().AffineInverse();
		Vector2 wp = inv * mx;
		Vector2 localT = _terrain!.ToLocal(wp);
		Vector2I cell = _terrain.LocalToMap(localT);
		string ck = HexGridUtil.CellKey(cell);

		if (mb == MouseButton.Right && HasPlacementPick())
		{
			ClearPlacementPick();
			return;
		}

		switch (_mode)
		{
			case EditorMode.AddTerrain when mb == MouseButton.Left:
				if (HasPlacementPick())
				{
					if (_cells.ContainsKey(ck))
						ApplyPlacementOnCell(cell, ck);
					break;
				}

				if (!_cells.ContainsKey(ck))
				{
					_cells[ck] = true;
					_fog[ck] = true;
					_blk[ck] = false;
					if (_ev.ContainsKey(ck))
						_ev.Remove(ck);
					_terrain.SetCell(cell, 0, Vector2I.Zero);
				}

				break;

			case EditorMode.AddTerrain when mb == MouseButton.Right:
				if (_cells.ContainsKey(ck))
					EraseCellCompletely(cell, ck);
				break;

			case EditorMode.ToggleFog when mb == MouseButton.Left && _cells.ContainsKey(ck):
				bool f = _fog.ContainsKey(ck) && _fog[ck].AsBool();
				_fog[ck] = !f;
				break;

			case EditorMode.SetPlayerStart when mb == MouseButton.Left && _cells.ContainsKey(ck):
				_player = cell;
				break;

			case EditorMode.EraseEvent when mb == MouseButton.Left && _ev.ContainsKey(ck):
				_ev.Remove(ck);
				break;
		}

		RefreshVisuals();
	}

	bool HasPlacementPick() => _pickMonsterIdx >= 0 || !string.IsNullOrEmpty(_pickOther);

	void ClearPlacementPick()
	{
		_pickMonsterIdx = -1;
		_pickOther = "";
		UncheckMonsters();
		UncheckOthers();
		RefreshPickGlow();
		UpdatePlacementHint();
	}

	void RefreshPickGlow()
	{
		if (_monsterGrid != null)
		{
			int i = 0;
			foreach (Node child in _monsterGrid.GetChildren())
			{
				Color tint = (_pickMonsterIdx == i++) ? new Color(1f, 1f, 0.78f, 1f) : Colors.White;
				if (child is CanvasItem ci)
					ci.Modulate = tint;
			}
		}

		if (_otherGrid != null)
		{
			int j = 0;
			foreach (Node child in _otherGrid.GetChildren())
			{
				string ty = "";
				if (j < OtherEvents.Length)
					ty = OtherEvents[j].PickId;

				Color tint = ty != "" && _pickOther == ty ? new Color(1f, 1f, 0.78f, 1f) : Colors.White;
				if (child is CanvasItem ci)
					ci.Modulate = tint;

				j++;
			}
		}
	}

	void EraseCellCompletely(Vector2I cell, string ck)
	{
		_cells.Remove(ck);
		_fog.Remove(ck);
		_blk.Remove(ck);
		_ev.Remove(ck);
		_terrain!.EraseCell(cell);
	}

	void ApplyPlacementOnCell(Vector2I cell, string ck)
	{
		bool placingBlock = _pickOther == "place_block";
		bool placingClearBlock = _pickOther == "clear_block";

		if (placingClearBlock)
		{
			if (_blk.ContainsKey(ck))
				_blk[ck] = false;
			if (_ev.ContainsKey(ck))
				_ev.Remove(ck);
			return;
		}

		IReadOnlyList<MonsterTable.Row> catalog = MonsterTable.All;

		if (placingBlock)
		{
			if (_ev.ContainsKey(ck))
				_ev.Remove(ck);

			if (_blk.ContainsKey(ck) && _blk[ck].AsBool())
			{
				_blk[ck] = false;
				return;
			}

			_blk[ck] = true;
			return;
		}

		bool hadBlock = _blk.ContainsKey(ck) && _blk[ck].AsBool();

		if (_pickMonsterIdx >= 0 && _pickMonsterIdx < catalog.Count)
		{
			if (hadBlock)
				_blk[ck] = false;

			MonsterTable.Row t = catalog[_pickMonsterIdx];
			if (_ev.TryGetValue(ck, out Variant existedVar))
			{
				Godot.Collections.Dictionary existed = existedVar.AsGodotDictionary();
				if (MonsterPickMatchesExistingEvent(existed, t))
				{
					_ev.Remove(ck);
					return;
				}
			}

			_ev[ck] = new Godot.Collections.Dictionary
			{
				["monster_id"] = t.Id,
				["type"] = t.IsMagic ? "monster_mag" : "monster_str",
				["value"] = t.Power,
				["icon"] = t.IconPath,
				["name"] = t.Name,
				["description"] = t.Description,
			};
			return;
		}

		if (!string.IsNullOrEmpty(_pickOther))
		{
			if (hadBlock)
				_blk[ck] = false;

			if (_ev.TryGetValue(ck, out Variant exOtherVar))
			{
				Godot.Collections.Dictionary existed = exOtherVar.AsGodotDictionary();
				if (OtherPickMatchesExistingEvent(existed, _pickOther))
				{
					_ev.Remove(ck);
					return;
				}
			}

			_ev[ck] = BuildOtherDictFromPick(_pickOther);
		}
	}

	void RebuildBossIdOptions()
	{
		if (_bossIdOpt == null)
			return;

		int keep = SelectedBossId();
		_bossIdOpt.Clear();
		_bossIdOpt.AddItem("（未指定 BOSS）");
		_bossIdOpt.SetItemMetadata(0, 0);
		int ix = 1;
		foreach (BossTable.Row r in BossTable.EnumerateSorted())
		{
			_bossIdOpt.AddItem($"{r.Name} · {r.Id}");
			_bossIdOpt.SetItemMetadata(ix, r.Id);
			ix++;
		}

		SelectBossDropdown(keep);
	}

	int SelectedBossId()
	{
		if (_bossIdOpt == null || _bossIdOpt.Selected < 0)
			return 0;

		Variant meta = _bossIdOpt.GetItemMetadata(_bossIdOpt.Selected);
		return meta.VariantType switch
		{
			Variant.Type.Int => meta.AsInt32(),
			Variant.Type.Float => (int)meta.AsDouble(),
			Variant.Type.String when int.TryParse(meta.AsString().Trim(),
				System.Globalization.NumberStyles.Integer,
				System.Globalization.CultureInfo.InvariantCulture, out int legacy) => legacy,
			_ => 0,
		};
	}

	void SelectBossDropdown(int id)
	{
		if (_bossIdOpt == null)
			return;

		for (int i = 0; i < _bossIdOpt.ItemCount; i++)
		{
			Variant meta = _bossIdOpt.GetItemMetadata(i);
			int mid = meta.VariantType switch
			{
				Variant.Type.Int => meta.AsInt32(),
				Variant.Type.Float => (int)meta.AsDouble(),
				Variant.Type.String when int.TryParse(meta.AsString().Trim(),
					System.Globalization.NumberStyles.Integer,
					System.Globalization.CultureInfo.InvariantCulture, out int p) => p,
				_ => 0,
			};
			if (mid == id)
			{
				_bossIdOpt.Selected = i;
				return;
			}
		}

		_bossIdOpt.Selected = 0;
	}


	static Godot.Collections.Dictionary BuildOtherDictFromPick(string pickId)
	{
		foreach (var o in OtherEvents)
		{
			if (o.PickId != pickId)
				continue;

			var d = new Godot.Collections.Dictionary
			{
				["type"] = o.GameType,
				["icon"] = o.Tex,
			};

			if (o.GameType == "altar")
				d["altar_used"] = false;

			return d;
		}

		return new Godot.Collections.Dictionary { ["type"] = pickId };
	}

	static bool MonsterPickMatchesExistingEvent(Godot.Collections.Dictionary ev, MonsterTable.Row t)
	{
		string mt = DictStr(ev, "type", "");
		if (mt != "monster_str" && mt != "monster_mag")
			return false;
		string want = t.IsMagic ? "monster_mag" : "monster_str";
		return mt == want && GetInt(ev, "monster_id") == t.Id;
	}

	/// <summary>与所选「物件」条目一致：比 type + icon（涵盖草丛/遗迹变种与祭坛图标）。</summary>
	static bool OtherPickMatchesExistingEvent(Godot.Collections.Dictionary ev, string pickId)
	{
		Godot.Collections.Dictionary want = BuildOtherDictFromPick(pickId);
		string wt = DictStr(want, "type", "");
		if (string.IsNullOrEmpty(wt) || wt != DictStr(ev, "type", ""))
			return false;
		return DictStr(want, "icon", "") == DictStr(ev, "icon", "");
	}

	void RefreshVisuals()
	{
		_blockLayer!.Setup(_terrain!);
		_blockLayer.SetBlocks(_blk);
		_fogLayer!.Setup(_terrain!);
		_fogLayer.Rebuild(_fog);

		var icons = GetNode<Node2D>("HBox/ViewportContainer/Viewport/World/EventIcons");
		foreach (Node ch in icons.GetChildren())
			ch.QueueFree();

		foreach (Variant vk in _ev.Keys)
		{
			string ck = vk.AsString();
			Godot.Collections.Dictionary ev = _ev[vk].AsGodotDictionary();

			var evForIcon = (Godot.Collections.Dictionary)ev.Duplicate();
			if (DictStr(evForIcon, "type", "") == "altar")
				evForIcon["altar_used"] = false;

			var spr = new Sprite2D();
			spr.Texture = HexEventMarker.TextureForEventDict(evForIcon);

			if (spr.Texture != null)
			{
				spr.Scale = new Vector2(HexEventMarker.EventIconSpriteScale, HexEventMarker.EventIconSpriteScale);
				spr.Offset = new Vector2(0f, -spr.Texture.GetHeight() * 0.05f);
			}

			Vector2I c = HexGridUtil.ParseKey(ck);
			spr.Name = $"Pv_{c.X}_{c.Y}";
			spr.Position = _terrain!.MapToLocal(c);
			icons.AddChild(spr);
		}

		if (_ghost != null)
		{
			_ghost.Position = _terrain!.MapToLocal(_player);
			if (_ghost.Texture != null)
			{
				float sy = Mathf.Abs(_ghost.Scale.Y);
				_ghost.Position += new Vector2(0f,
					PlayerSpriteAnchorLayout.WorldOffsetYAnchorBelowCenter(_ghost.Texture, sy));
			}
		}
	}

	Godot.Collections.Dictionary Gather()
	{
		Godot.Collections.Array cellsArr = [];
		foreach (Variant k in _cells.Keys)
		{
			string ck = k.AsString();
			Vector2I cc = HexGridUtil.ParseKey(ck);
			cellsArr.Add(new Godot.Collections.Dictionary
			{
				["x"] = cc.X,
				["y"] = cc.Y,
				["present"] = true,
				["fog"] = _fog.ContainsKey(ck) && _fog[ck].AsBool(),
				["block"] = _blk.ContainsKey(ck) && _blk[ck].AsBool(),
			});
		}

		Godot.Collections.Array evArr = [];
		foreach (Variant k2 in _ev.Keys)
		{
			Godot.Collections.Dictionary ev = (Godot.Collections.Dictionary)_ev[k2].AsGodotDictionary().Duplicate();
			Vector2I c2 = HexGridUtil.ParseKey(k2.AsString());
			if (ev.ContainsKey("type") && ev["type"].AsString() == "altar")
				ev["altar_used"] = ev.ContainsKey("altar_used") && ev["altar_used"].AsBool();
			ev["x"] = c2.X;
			ev["y"] = c2.Y;
			evArr.Add(ev);
		}

		int camOrder =
			Mathf.Clamp(_levelCampaignOrderSpin != null ? (int)Mathf.Round(_levelCampaignOrderSpin.Value) : 1,
				1, 999_999);

		return new Godot.Collections.Dictionary
		{
			["version"] = LevelIo.Version,
			["level_name"] = LevelNameTrimmed(),
			[LevelCatalog.CampaignOrderIndexKey] = camOrder,
			["player_start"] = new Godot.Collections.Dictionary { ["x"] = _player.X, ["y"] = _player.Y },
			["boss"] = new Godot.Collections.Dictionary { ["boss_id"] = SelectedBossId() },
			[TerrainTilesetFactory.TerrainVariantDictKey] = _terrainVariant,
			["cells"] = cellsArr,
			["events"] = evArr,
		};
	}

	static string DictStr(Godot.Collections.Dictionary d, string key, string def)
	{
		if (!d.TryGetValue(key, out Variant v))
			return def;
		switch (v.VariantType)
		{
			case Variant.Type.String:
				return v.AsString();

			case Variant.Type.Int:
				return v.AsInt32().ToString();

			default:
				return def;
		}
	}

	static int GetInt(Godot.Collections.Dictionary d, string key, int def = 0) =>
		d.ContainsKey(key) ? d[key].AsInt32() : def;

	static bool GetBool(Godot.Collections.Dictionary d, string key, bool def = false) =>
		d.ContainsKey(key) ? d[key].AsBool() : def;

	static float GetFloat(Godot.Collections.Dictionary d, string key, float def = 0f) =>
		d.ContainsKey(key) ? d[key].AsSingle() : def;

	static int ReadBossId(Godot.Collections.Dictionary b)
	{
		if (!b.TryGetValue("boss_id", out Variant bidVar))
			return 0;
		return bidVar.VariantType switch
		{
			Variant.Type.Int => bidVar.AsInt32(),
			Variant.Type.Float => (int)bidVar.AsDouble(),
			Variant.Type.String when int.TryParse(bidVar.AsString().Trim(),
				System.Globalization.NumberStyles.Integer,
				System.Globalization.CultureInfo.InvariantCulture, out int p) => p,
			_ => 0,
		};
	}

	public void Reload(Godot.Collections.Dictionary d)
	{
		_terrainVariant = TerrainTilesetFactory.ResolveTerrainVariantFromLevel(d);
		_cells.Clear();
		_fog.Clear();
		_blk.Clear();
		_ev.Clear();
		_terrain!.Clear();
		_terrain.TileSet = TerrainTilesetFactory.CreateHexTileset(_terrainVariant);
		TerrainTilesetFactory.ApplyTerrainPresentation(_terrain);
		RefreshEditorPlayerGhostScale();

		if (d.ContainsKey("cells") && d["cells"].VariantType == Variant.Type.Array)
		{
			Godot.Collections.Array cells = d["cells"].AsGodotArray();
			foreach (Variant itAny in cells)
			{
				Godot.Collections.Dictionary item = itAny.AsGodotDictionary();
				Vector2I c = new(GetInt(item, "x"), GetInt(item, "y"));
				if (item.ContainsKey("present") && !GetBool(item, "present", true))
					continue;

				string ck = HexGridUtil.CellKey(c);
				_cells[ck] = true;
				_terrain.SetCell(c, 0, Vector2I.Zero);
				_fog[ck] = GetBool(item, "fog", true);
				_blk[ck] = GetBool(item, "block", false);
			}
		}

		if (d.ContainsKey("player_start") && d["player_start"].VariantType == Variant.Type.Dictionary)
		{
			Godot.Collections.Dictionary ps = d["player_start"].AsGodotDictionary();
			_player = new Vector2I(GetInt(ps, "x"), GetInt(ps, "y"));
		}

		if (d.ContainsKey("events") && d["events"].VariantType == Variant.Type.Array)
		{
			Godot.Collections.Array evs = d["events"].AsGodotArray();
			foreach (Variant eAny in evs)
			{
				Godot.Collections.Dictionary e = eAny.AsGodotDictionary();
				Vector2I c2 = new(GetInt(e, "x"), GetInt(e, "y"));
				string kk = HexGridUtil.CellKey(c2);
				Godot.Collections.Dictionary dup = (Godot.Collections.Dictionary)e.Duplicate();
				MonsterTable.EnrichMonsterEvent(dup);
				_ev[kk] = dup;
			}
		}

		if (d.ContainsKey("boss") && d["boss"].VariantType == Variant.Type.Dictionary)
		{
			Godot.Collections.Dictionary b = d["boss"].AsGodotDictionary();
			SelectBossDropdown(ReadBossId(b));
		}
		else
			SelectBossDropdown(0);

		if (_levelNameEdit != null)
		{
			string rn = DictStr(d, "level_name", "").Trim();
			if (string.IsNullOrEmpty(rn))
				rn = PathStemFromResPath(_currentLevelPath);
			_levelNameEdit.Text = rn ?? "";
		}

		if (_levelCampaignOrderSpin != null)
		{
			int parsed = LevelCatalog.ReadCampaignOrderIndex(d);
			_levelCampaignOrderSpin.Value = parsed == LevelCatalog.CampaignOrderUnset ? 1 : parsed;
		}

		RefreshVisuals();
		SyncTerrainStyleDropdown();
		_editorPendingRefit = true;
	}

	void FitCam()
	{
		if (_cam == null || _terrain == null)
			return;

		if (_cells.Count == 0)
		{
			_cam.Position = Vector2.Zero;
			_cam.Zoom = new Vector2(0.75f, 0.75f);
			return;
		}

		Vector2 sum = Vector2.Zero;
		foreach (Variant vk in _cells.Keys)
			sum += _terrain.MapToLocal(HexGridUtil.ParseKey(vk.AsString()));

		Vector2 center = sum / _cells.Count;
		_cam.Position = center;

		System.Collections.Generic.List<Vector2> pts = [];
		foreach (Variant vk in _cells.Keys)
			pts.Add(_terrain.MapToLocal(HexGridUtil.ParseKey(vk.AsString())));

		Rect2 rect = new(pts[0], Vector2.Zero);
		for (int i = 1; i < pts.Count; i++)
			rect = rect.Expand(pts[i]);

		float span = Mathf.Max(rect.Size.X, rect.Size.Y);
		float z = Mathf.Clamp(span / 700f, 0.35f, 1.35f);
		_cam.Zoom = new Vector2(z, z);
	}

	string LevelNameTrimmed() => _levelNameEdit?.Text?.Trim() ?? "";

	string PathStemFromResPath(string resPath) =>
		LevelCatalog.FileStemFromResPath(resPath);

	static string SafeFilenameStem(string displayNameTrimmed)
	{
		string stem = displayNameTrimmed.Trim();
		foreach (char ch in Path.GetInvalidFileNameChars())
			stem = stem.Replace(ch, '_');

		stem = stem.Trim('.', ' ', '_');
		if (string.IsNullOrEmpty(stem))
			stem = "level";

		return stem;
	}

	static int PickDefaultBossIdForBlank()
	{
		foreach (BossTable.Row br in BossTable.EnumerateSorted())
			return br.Id;

		return 0;
	}



	void RebuildLevelFileDropdown()
	{
		if (_levelFilesOpt == null)
			return;

		_suppressLevelDropdown = true;

		_levelFilesOpt.Clear();
		_levelFilesOpt.AddItem("— 请选择关卡 / 新建后保存写入列表 —");
		_levelFilesOpt.SetItemMetadata(0, "");

		LevelCatalog.EnsureDirectoryExists();

		foreach (string full in LevelCatalog.EnumerateLevelJsonPathsSortedByCampaignOrderThenStem())
		{
			int idx = _levelFilesOpt.ItemCount;
			_levelFilesOpt.AddItem(LevelCatalog.GetDropdownLabel(full));
			_levelFilesOpt.SetItemMetadata(idx, full);
		}

		_suppressLevelDropdown = false;
	}


	void SelectDropdownByPathOrNone(string resPath)
	{
		if (_levelFilesOpt == null)
			return;

		_suppressLevelDropdown = true;

		if (string.IsNullOrEmpty(resPath))
			_levelFilesOpt.Select(0);
		else
		{
			bool found = false;

			for (int i = 0; i < _levelFilesOpt.ItemCount; i++)
			{
				if (_levelFilesOpt.GetItemMetadata(i).AsString() == resPath)
				{
					_levelFilesOpt.Select(i);
					found = true;
					break;
				}
			}

			if (!found)
				_levelFilesOpt.Select(0);
		}

		_suppressLevelDropdown = false;
	}

	void LoadLevelFromPath(string resPath, bool rebuildList)
	{
		Godot.Collections.Dictionary data = LevelIo.LoadFromFile(resPath);
		if (data.Count == 0)
		{
			GD.PushWarning($"LevelEditor: 读取失败 {resPath}");
			return;
		}

		_currentLevelPath = resPath;
		Reload(data);

		if (rebuildList)
			RebuildLevelFileDropdown();

		SelectDropdownByPathOrNone(resPath);
		Callable.From(() => EditorRefreshViewportResolutionAndCamera(true)).CallDeferred();
	}

	void ApplyNewBlankDraft(int defaultBossId)
	{
		var cellsArr = new Godot.Collections.Array
		{
			new Godot.Collections.Dictionary
			{
				["x"] = 0,
				["y"] = 0,
				["present"] = true,
				["fog"] = false,
				["block"] = false,
			},
		};

		var dict = new Godot.Collections.Dictionary
		{
			["version"] = LevelIo.Version,
			["level_name"] = "",
			[LevelCatalog.CampaignOrderIndexKey] = 1,
			["player_start"] = new Godot.Collections.Dictionary { ["x"] = 0, ["y"] = 0 },
			["boss"] = new Godot.Collections.Dictionary { ["boss_id"] = defaultBossId },
			[TerrainTilesetFactory.TerrainVariantDictKey] = 1,
			["cells"] = cellsArr,
			["events"] = new Godot.Collections.Array(),
		};

		_currentLevelPath = "";
		if (_levelNameEdit != null)
			_levelNameEdit.Text = "";

		Reload(dict);
		Callable.From(() => EditorRefreshViewportResolutionAndCamera(true)).CallDeferred();
	}

	void PopupDialog(string title, string message)
	{
		var dlg = new AcceptDialog { Title = title, DialogText = message };
		AddChild(dlg);
		dlg.Confirmed += () => dlg.QueueFree();
		dlg.Canceled += () => dlg.QueueFree();
		dlg.CloseRequested += () => dlg.QueueFree();
		dlg.PopupCentered();
	}

	bool TrySaveCore(string resolvedResPath)
	{
		string nameTrim = LevelNameTrimmed();
		if (string.IsNullOrEmpty(nameTrim))
		{
			PopupDialog("无法保存", "请填写关卡名后再保存。");
			return false;
		}

		int desiredOrder =
			Mathf.Clamp(_levelCampaignOrderSpin != null ? (int)Mathf.Round(_levelCampaignOrderSpin.Value) : 1,
				1, 999_999);
		List<string> dupPaths =
			LevelCatalog.FindDuplicateCampaignOrderConflictsElsewhere(_currentLevelPath, desiredOrder);
		if (dupPaths.Count > 0)
		{
			var lines = new System.Text.StringBuilder();
			foreach (string pth in dupPaths)
				lines.AppendLine($"· {LevelCatalog.FileStemFromResPath(pth)} ({pth})");

			PopupDialog("闯关序号冲突", $"闯关序号【{desiredOrder}】已被其他关卡使用：\n{lines}请修改为未占用的序号。");
			return false;
		}

		LevelCatalog.EnsureDirectoryExists();

		Godot.Collections.Dictionary payload = Gather();

		payload["level_name"] = nameTrim;

		Error err = LevelIo.SaveToFile(resolvedResPath, payload);
		if (err != Error.Ok)
		{
			PopupDialog("无法保存", $"写入失败：\n{resolvedResPath}\n（{err}）");
			return false;
		}

		_currentLevelPath = resolvedResPath;

		if (_levelNameEdit != null && string.IsNullOrWhiteSpace(_levelNameEdit.Text))
			_levelNameEdit.Text = nameTrim;

		RebuildLevelFileDropdown();
		SelectDropdownByPathOrNone(_currentLevelPath);
		GD.Print($"关卡已保存 → {resolvedResPath}");

		return true;
	}

	public void _on_level_files_selected(long index)
	{
		if (_suppressLevelDropdown || _levelFilesOpt == null)
			return;

		string path = _levelFilesOpt.GetItemMetadata((int)index).AsString();
		if (string.IsNullOrEmpty(path))
			return;

		LoadLevelFromPath(path, rebuildList: false);
	}

	public void _on_new_level_pressed()
	{
		int bid = PickDefaultBossIdForBlank();
		ApplyNewBlankDraft(bid);
		RebuildLevelFileDropdown();
		SelectDropdownByPathOrNone("");
	}

	public void _on_save_as_pressed()
	{
		string nm = LevelNameTrimmed();

		if (string.IsNullOrEmpty(nm))
		{
			PopupDialog("无法保存", "请填写关卡名后再「另存为」。");
			return;
		}

		string dst = $"{LevelCatalog.ResourceDir}/{SafeFilenameStem(nm)}.json";
		TrySaveCore(dst);
	}

	public void _on_reload_pressed()
	{
		if (string.IsNullOrEmpty(_currentLevelPath))
		{
			PopupDialog("提示", "当前为未保存草稿，没有可重新读取的磁盘文件。");
			return;
		}

		LoadLevelFromPath(_currentLevelPath, rebuildList: false);
	}

	public void _on_save_pressed()
	{
		string nm = LevelNameTrimmed();

		if (string.IsNullOrEmpty(nm))
		{
			PopupDialog("无法保存", "请填写关卡名后再保存。");
			return;
		}

		string target = string.IsNullOrEmpty(_currentLevelPath)
			? $"{LevelCatalog.ResourceDir}/{SafeFilenameStem(nm)}.json"
			: _currentLevelPath;

		TrySaveCore(target);
	}

	public void _on_playtest_pressed()
	{
		string nm = LevelNameTrimmed();

		if (string.IsNullOrEmpty(nm))
		{
			PopupDialog("无法试玩", "试玩前请填写关卡名并保存。");
			return;
		}

		string target = string.IsNullOrEmpty(_currentLevelPath)
			? $"{LevelCatalog.ResourceDir}/{SafeFilenameStem(nm)}.json"
			: _currentLevelPath;

		if (!TrySaveCore(target))
			return;

		RunState.Instance.PrepareReturnToMainMenu();
		RunState.Instance.PendingLevelPath = _currentLevelPath;
		GetTree().ChangeSceneToFile("res://Scenes/gameplay.tscn");
	}

	public void _on_back_menu_pressed()
	{
		RunState.Instance.PrepareReturnToMainMenu();
		GetTree().ChangeSceneToFile("res://Scenes/main_menu.tscn");
	}
}
