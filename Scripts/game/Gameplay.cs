using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using System.Linq;

namespace Booom202604;

/// <summary>
/// Prototype gameplay ported from GDScript; skills not implemented.
/// </summary>
public partial class Gameplay : Node2D
{
	private const string DefaultLevel = "res://levels/starter_level.json";
	private const string MainMenuScene = "res://Scenes/main_menu.tscn";

	[Export(PropertyHint.File, "*.json")]
	public string LevelJsonPath { get; set; } = DefaultLevel;

	/// <summary>Gameplay：摄像机「最近」缩放（数值越小画面越放大，与 Godot Camera2D.Zoom 一致）。只改 C# 默认值对已在 `gameplay.tscn` 里保存过覆盖的节点<strong>不会</strong>生效；请打开 gameplay.tscn 选中根节点 Gameplay，在检查器里改。本会限制开局 Fit 与滚轮缩放的范围。</summary>
	[Export(PropertyHint.Range, "0.02,40,or_greater,or_lesser")]
	public float GameplayCameraZoomLimitClose { get; set; } = 0.28f;

	/// <summary>Gameplay：摄像机「最远」缩放（数值越大整张地图显得越小）。若在 Close～Far 区间内算出的开局 Zoom 已由地图尺寸决定，仅调这两项可能<strong>几乎不变开局构图</strong>，请调下方的 Fit Divisor。</summary>
	[Export(PropertyHint.Range, "0.05,40,or_greater,or_lesser")]
	public float GameplayCameraZoomLimitFar { get; set; } = 14f;

	/// <summary>开局自动取景：Zoom ≈ max(地形包围盒边长) / 本值。<strong>数值越大开局越远</strong>；与滚轮上下限独立（结果仍会被 Close/Far 夹住）。</summary>
	[Export(PropertyHint.Range, "1,99999,or_greater")]
	public float GameplayCameraFitDivisor { get; set; } = 700f;

	/// <summary>滚轮每档相对倍率（&gt;1）。与关卡编辑器一致：向上滚会增大 Zoom（拉远视角）。</summary>
	[Export(PropertyHint.Range, "1.01,2")]
	public float GameplayCameraZoomWheelFactor { get; set; } = 1.12f;

	TileMapLayer? _terrain;
	FogLayer? _fog;
	BossWarningLayer? _bossWarn;
	BlockLayer? _blocks;
	Sprite2D? _playerSprite;
	Hud? _hudUi;

	const float PlayerMoveTweenSeconds = 0.5f;
	const float PlayerFightKnockbackSeconds = 0.3f;
	const float PlayerWalkFramesPerSecond = 10f;
	static readonly string[] PlayerIdleTextureCandidates =
	[
		"res://Art/Player/idle.png",
		"res://Art/Player/idel.png",
	];
	static readonly string[] PlayerWalkFramePaths =
	[
		"res://Art/Player/walk1.png",
		"res://Art/Player/walk2.png",
		"res://Art/Player/walk3.png",
		"res://Art/Player/walk4.png",
	];

	Texture2D? _playerIdleTex;
	readonly Texture2D?[] _playerWalkFrames = new Texture2D?[4];
	Camera2D? _camera;

	readonly Godot.Collections.Dictionary _valid = [];
	readonly Godot.Collections.Dictionary _fogState = [];
	readonly Godot.Collections.Dictionary _blockState = [];
	readonly Godot.Collections.Dictionary _events = [];

	Vector2I _playerCell;

	enum Turn
	{
		Player,
		Boss,
	}

	Turn _turn = Turn.Player;
	bool _spentBasic;
	bool _busyPlayerAction;

	float _bossMeter;
	float _bossWarnMax = 50f;
	float _bossChargeMax = 50f;
	float _bossGain = 22f;
	string _bossName = "BOSS";
	string _bossSkillText = "";

	bool _bossUsesTableSkill;
	int _bossSkillTarget;
	int _bossSkillArea;
	int _bossSkillEffect;
	string _bossSkillDetail = "";
	int _bossTableId;

	readonly HashSet<string> _bossLockedCellKeys = [];

	int _fogGoalTotal = 1;

	/// <summary>怪物公布后邻居一圈迷雾：不能被「移动顺带吸收」「迷雾缠身」驱散； refcount 为多怪共享格。</summary>
	readonly Dictionary<string, int> _fogNeighborAbsorptionLockRef = [];

	/// <summary>每只已亮相怪物格子 key → 曾为其加锁的邻居迷雾格。</summary>
	readonly Dictionary<string, HashSet<string>> _monsterNeighborFogLocksByAnchor = [];

	static readonly StringName CellKeyMeta = new("cell_key");

	bool CellHasFog(string ck) =>
		_fogState.ContainsKey(ck) && _fogState[ck].AsBool();

	void RefreshEventIconsFogVisibility()
	{
		var icons = GetNodeOrNull<Node2D>("World/EventIcons");
		if (icons == null)
			return;

		foreach (Node ch in icons.GetChildren())
		{
			if (!ch.HasMeta(CellKeyMeta))
				continue;
			string ck = ch.GetMeta(CellKeyMeta).AsString();
			if (ch is CanvasItem cv)
				cv.Visible = !CellHasFog(ck);
		}
	}

	static bool MonsterEventType(string t) =>
		t == "monster_str" || t == "monster_mag";

	bool TryMonsterEventAtKey(string ck, out Godot.Collections.Dictionary ev)
	{
		ev = default!;
		if (!_events.TryGetValue(ck, out Variant vv))
			return false;

		ev = vv.AsGodotDictionary();
		return MonsterEventType(GetString(ev, "type"));
	}

	bool FogNeighborAbsorptionLocked(string ck) =>
		_fogNeighborAbsorptionLockRef.TryGetValue(ck, out int r) && r > 0;

	void NotifyMonsterFogRevealedIfNeeded(Vector2I cell)
	{
		string ck = HexGridUtil.CellKey(cell);
		if (!TryMonsterEventAtKey(ck, out _))
			return;

		OnMonsterNeighborsFogLocksForReveal(cell);
	}

	void OnMonsterNeighborsFogLocksForReveal(Vector2I monsterCell)
	{
		if (_terrain == null || _fog == null)
			return;

		string anchor = HexGridUtil.CellKey(monsterCell);

		// 仅在怪物格已被揭示（无迷雾）时才产生锁定；被 BOSS 迷雾重新盖住时走 OnMonsterCellBecameFogCovered 释放。
		if (CellHasFog(anchor))
			return;

		if (_monsterNeighborFogLocksByAnchor.ContainsKey(anchor))
			return;

		HashSet<string> touched = [];

		foreach (Vector2I n in HexGridUtil.Neighbors(_terrain, monsterCell))
		{
			string nk = HexGridUtil.CellKey(n);

			if (!_valid.ContainsKey(nk) || !CellHasFog(nk))
				continue;

			touched.Add(nk);
			if (_fogNeighborAbsorptionLockRef.TryGetValue(nk, out int c))
				_fogNeighborAbsorptionLockRef[nk] = c + 1;
			else
				_fogNeighborAbsorptionLockRef[nk] = 1;

			_fog.SetAbsorptionLockedVisual(n, true);
		}

		if (touched.Count > 0)
			_monsterNeighborFogLocksByAnchor[anchor] = touched;
	}

	/// <summary>任意来源将迷雾重新盖在<strong>怪物格</strong>上时撤销其邻圈吸收锁。</summary>
	void OnMonsterCellBecameFogCovered(Vector2I monsterCell)
	{
		string ck = HexGridUtil.CellKey(monsterCell);
		if (!TryMonsterEventAtKey(ck, out _))
			return;

		ReleaseMonsterNeighborFogLocks(ck);
	}

	void ReleaseMonsterNeighborFogLocks(string monsterAnchorCk)
	{
		if (!_monsterNeighborFogLocksByAnchor.TryGetValue(monsterAnchorCk,
				out HashSet<string>? nkSet))
			return;

		_monsterNeighborFogLocksByAnchor.Remove(monsterAnchorCk);

		if (_terrain == null || _fog == null)
			return;

		foreach (string nk in nkSet)
		{
			if (!_fogNeighborAbsorptionLockRef.TryGetValue(nk, out int r))
				continue;

			r--;
			if (r <= 0)
			{
				_fogNeighborAbsorptionLockRef.Remove(nk);
				if (_valid.ContainsKey(nk))
					_fog.SetAbsorptionLockedVisual(HexGridUtil.ParseKey(nk), false);
			}
			else
				_fogNeighborAbsorptionLockRef[nk] = r;
		}
	}

	void StripAllMonsterNeighborAssociationsForFogKey(string nk)
	{
		_fogNeighborAbsorptionLockRef.Remove(nk);
		var anchors = new List<string>();
		foreach (string anchor in _monsterNeighborFogLocksByAnchor.Keys)
			anchors.Add(anchor);

		foreach (string anchor in anchors)
		{
			if (!_monsterNeighborFogLocksByAnchor.TryGetValue(anchor, out HashSet<string>? hs))
				continue;

			if (hs.Remove(nk) && hs.Count == 0)
				_monsterNeighborFogLocksByAnchor.Remove(anchor);
		}
	}

	void SyncMonsterNeighborFogLocksFromRevealedMonsters()
	{
		foreach (Variant vk in _events.Keys)
		{
			string ck = vk.AsString();
			if (!TryMonsterEventAtKey(ck, out _))
				continue;

			if (CellHasFog(ck))
				continue;

			OnMonsterNeighborsFogLocksForReveal(HexGridUtil.ParseKey(ck));
		}
	}

	/// <summary>仅技能等显式驱散：绕过吸收锁，清除该格迷雾并从所有怪物邻居锁名单中剔除该格。</summary>
	public void DispelFogBySkill(Vector2I cell)
	{
		string ck = HexGridUtil.CellKey(cell);
		if (!_valid.ContainsKey(ck))
			return;

		if (!(_fogState.ContainsKey(ck) && _fogState[ck].AsBool()))
			return;

		StripAllMonsterNeighborAssociationsForFogKey(ck);

		if (_terrain != null)
			_fog!.SetAbsorptionLockedVisual(cell, false);

		_fogState[ck] = false;
		_fog!.SetCell(cell, false);
		NotifyMonsterFogRevealedIfNeeded(cell);

		RefreshEventIconsFogVisibility();
		RunState.Instance.ClampHp();
		RefreshHud();
	}

	List<int> skillList = new List<int>();
	List<int> askillList = new List<int>();
	List<int> pskillList = new List<int>();

	public Godot.Collections.Array aarray = Json.ParseString(FileAccess.GetFileAsString("res://skill/activeskill.json")).AsGodotArray();
	public Godot.Collections.Array parray = Json.ParseString(FileAccess.GetFileAsString("res://skill/passiveskill.json")).AsGodotArray();

	public List<int> cardnum = [];
	public int cardchoose = 0;

	public bool grassskill = false;
	public int grasscount = 0;
	public bool corpseHp = false;
	public int corpseCount = 0;

	public override void _Ready()
	{
		RunState.Instance.PrepareLevelStart();

		MonsterTable.Reload(MonsterTable.DefaultResourcePath);
		BossTable.Reload(BossTable.DefaultResourcePath);

		_terrain = GetNode<TileMapLayer>("World/TerrainLayer");
		_playerSprite = GetNode<Sprite2D>("World/Player");
		_camera = GetNodeOrNull<Camera2D>("Camera2D");
		if (_camera != null)
			_camera.Enabled = true;

		_hudUi = GetNode<Hud>("UICanvas/HUD");
		var portraitTex = GD.Load<Texture2D>("res://Art/Role/player.png");
		if (portraitTex != null)
			_hudUi.SetPortrait(portraitTex);
		_fog = GetNode<FogLayer>("World/FogRoot");
		_bossWarn = GetNodeOrNull<BossWarningLayer>("World/BossWarningRoot");
		_blocks = GetNode<BlockLayer>("World/BlockRoot");

		LoadPlayerTexturesForWorldSprite();

		string path = RunState.Instance.PendingLevelPath;
		if (string.IsNullOrWhiteSpace(path))
			path = LevelJsonPath;

		Godot.Collections.Dictionary lvl = LevelIo.LoadFromFile(path);
		if (lvl.Count == 0)
			lvl = LevelIo.LoadFromFile(DefaultLevel);


		foreach (var item in parray)
		{
			var pskilldict = item.AsGodotDictionary();
			int pskillid = pskilldict["ID"].AsInt32();
			skillList.Add(pskillid);
		}

		foreach (var item in aarray)
		{
			var askilldict = item.AsGodotDictionary();
			int askillid = askilldict["ID"].AsInt32();
			skillList.Add(askillid);
		}

		ApplyLevel(lvl);
		SnapPlayer();
		RefreshHud();
		FitCamera();
	}

	static int GetInt(Godot.Collections.Dictionary d, string key, int def = 0)
	{
		return d.ContainsKey(key) ? d[key].AsInt32() : def;
	}

	static bool GetBool(Godot.Collections.Dictionary d, string key, bool def = false)
	{
		return d.ContainsKey(key) ? d[key].AsBool() : def;
	}

	static float GetFloat(Godot.Collections.Dictionary d, string key, float def)
	{
		return d.ContainsKey(key) ? d[key].AsSingle() : def;
	}

	static int ReadBossIdVariant(Godot.Collections.Dictionary b)
	{
		if (!b.TryGetValue("boss_id", out Variant bidVar))
			return 0;
		return bidVar.VariantType switch
		{
			Variant.Type.Int => bidVar.AsInt32(),
			Variant.Type.Float => (int)bidVar.AsDouble(),
			Variant.Type.String when int.TryParse(bidVar.AsString().Trim(),
				System.Globalization.NumberStyles.Integer,
				System.Globalization.CultureInfo.InvariantCulture, out int parsed) => parsed,
			_ => 0,
		};
	}

	void ApplyPlayerWorldSpriteScaleFromTerrain()
	{
		if (_playerSprite == null || _terrain?.TileSet == null)
			return;

		float sc = TerrainTilesetFactory.PlayerSpriteScaleMatchingTerrainPixels(_terrain.TileSet);
		_playerSprite.Scale = new Vector2(sc, sc);
	}


	void ApplyLevel(Godot.Collections.Dictionary d)
	{
		int terrainVar = TerrainTilesetFactory.ResolveTerrainVariantFromLevel(d);
		_terrain!.TileSet = TerrainTilesetFactory.CreateHexTileset(terrainVar);
		TerrainTilesetFactory.ApplyTerrainPresentation(_terrain);
		ApplyPlayerWorldSpriteScaleFromTerrain();
		_terrain.Clear();
		_valid.Clear();
		_fogState.Clear();
		_blockState.Clear();
		_events.Clear();

		foreach (Node ch in GetNode("World/EventIcons").GetChildren())
			ch.QueueFree();

		_bossWarnMax = 50f;
		_bossChargeMax = 50f;
		_bossGain = 22f;
		_bossName = "BOSS";
		_bossSkillText = "";
		_bossUsesTableSkill = false;
		_bossSkillTarget = 0;
		_bossSkillArea = 0;
		_bossSkillEffect = 0;
		_bossSkillDetail = "";
		_bossTableId = 0;
		_bossLockedCellKeys.Clear();
		_bossWarn?.ClearAll();
		_fogNeighborAbsorptionLockRef.Clear();
		_monsterNeighborFogLocksByAnchor.Clear();

		if (!d.ContainsKey("cells"))
			return;

		Godot.Collections.Array cells = d["cells"].AsGodotArray();

		foreach (Variant it in cells)
		{
			Godot.Collections.Dictionary item = it.AsGodotDictionary();
			Vector2I c = new(GetInt(item, "x"), GetInt(item, "y"));
			string ck = HexGridUtil.CellKey(c);

			if (item.ContainsKey("present") && !GetBool(item, "present", true))
				continue;

			_valid[ck] = true;
			_terrain.SetCell(c, 0, Vector2I.Zero);
			_fogState[ck] = GetBool(item, "fog", true);
			_blockState[ck] = GetBool(item, "block", false);
		}

		if (d.ContainsKey("player_start") && d["player_start"].VariantType == Variant.Type.Dictionary)
		{
			Godot.Collections.Dictionary ps = d["player_start"].AsGodotDictionary();
			_playerCell = new Vector2I(GetInt(ps, "x"), GetInt(ps, "y"));
		}

		if (d.ContainsKey("events") && d["events"].VariantType == Variant.Type.Array)
		{
			Godot.Collections.Array evs = d["events"].AsGodotArray();

			foreach (Variant evv in evs)
			{
				Godot.Collections.Dictionary ev = evv.AsGodotDictionary();
				Vector2I ec = new(GetInt(ev, "x"), GetInt(ev, "y"));
				string k = HexGridUtil.CellKey(ec);
				Godot.Collections.Dictionary copy = (Godot.Collections.Dictionary)ev.Duplicate();
				MonsterTable.EnrichMonsterEvent(copy);
				_events[k] = copy;
			}

		}

		_fog!.Setup(_terrain);
		_fog.Rebuild(_fogState);

		_blocks!.Setup(_terrain);
		_blocks.SetBlocks(_blockState);

		SpawnEventIcons();

		RefreshEventIconsFogVisibility();

		SyncMonsterNeighborFogLocksFromRevealedMonsters();

		_fogGoalTotal = Mathf.Max(CountTrue(_fogState), 1);

		if (d.ContainsKey("boss") && d["boss"].VariantType == Variant.Type.Dictionary)
		{
			Godot.Collections.Dictionary b = d["boss"].AsGodotDictionary();
			int bossId = ReadBossIdVariant(b);

			bool fromTable = false;
			if (bossId != 0 && BossTable.TryGet(bossId, out BossTable.Row? br) && br != null)
			{
				_bossName = string.IsNullOrEmpty(br.Name) ? $"{bossId}" : br.Name;
				_bossWarnMax = Mathf.Max(br.WarnMeter, 0);
				_bossChargeMax = Mathf.Max(br.ChargeMeter, 0);
				_bossGain = Mathf.Max(br.GainPerTurn, 1);
				_bossSkillText = br.SkillDescription ?? "";
				_bossSkillTarget = br.SkillTarget;
				_bossSkillArea = br.SkillArea;
				_bossSkillEffect = br.SkillEffect;
				_bossSkillDetail = string.IsNullOrWhiteSpace(br.SkillDetail) ? "" : br.SkillDetail;
				_bossUsesTableSkill = true;
				_bossTableId = bossId;
				if (BossTable.MeterTotal(br) < 1)
				{
					_bossWarnMax = 50f;
					_bossChargeMax = 50f;
				}

				fromTable = true;
			}

			if (!fromTable && b.ContainsKey("meter_max"))
			{
				_bossUsesTableSkill = false;
				_bossTableId = 0;
				_bossSkillTarget = 0;
				_bossSkillArea = 0;
				_bossSkillEffect = 0;
				_bossSkillDetail = "";

				float total = Mathf.Max(GetFloat(b, "meter_max", 100f), 10f);
				_bossGain = Mathf.Max(GetFloat(b, "meter_gain_per_turn", _bossGain), 1f);
				float half = Mathf.Floor(total * 0.5f);
				_bossWarnMax = half;
				_bossChargeMax = total - half;
				_bossName = "BOSS（旧关卡）";
				_bossSkillText = "";
			}
			else if (!fromTable)
			{
				_bossUsesTableSkill = false;
				_bossTableId = 0;
				_bossSkillTarget = 0;
				_bossSkillArea = 0;
				_bossSkillEffect = 0;
				_bossSkillDetail = "";

				_bossWarnMax = 50f;
				_bossChargeMax = 50f;
				_bossGain = Mathf.Max(_bossGain, 1f);
				_bossName = "BOSS";
				_bossSkillText = "";
			}
		}


		_bossWarn?.Setup(_terrain!);


		_bossMeter = 0f;

	}





	void SpawnEventIcons()


	{


		var icons = GetNode<Node2D>("World/EventIcons");


		foreach (Variant vk in _events.Keys)



		{

			string ckStr = vk.AsString();

			Godot.Collections.Dictionary ev = _events[vk].AsGodotDictionary();

			var spr = new Sprite2D();

			spr.Texture = HexEventMarker.TextureForEventDict(ev);

			if (spr.Texture != null)
			{

				spr.Scale = new Vector2(HexEventMarker.EventIconSpriteScale, HexEventMarker.EventIconSpriteScale);

				spr.Offset = new Vector2(0f, -spr.Texture.GetHeight() * 0.05f);



			}




			Vector2I cell = HexGridUtil.ParseKey(ckStr);

			spr.Name = $"Ev_{cell.X}_{cell.Y}";

			spr.SetMeta(CellKeyMeta, ckStr);

			spr.Position = _terrain!.MapToLocal(cell);

			icons.AddChild(spr);

		}







	}





	int CountTrue(Godot.Collections.Dictionary d)


	{


		int cnt = 0;



		foreach (Variant k in d.Keys)


		{


			if (d[k].AsBool())





				cnt++;

		}







		return cnt;



	}





	int RemainingFog()



	{


		int cnt = 0;



		foreach (Variant k in _fogState.Keys)


		{


			if (_fogState[k].AsBool())





				cnt++;


		}




		return cnt;



	}





	public override void _UnhandledInput(InputEvent @event)


	{

		if (_camera != null && HandleGameplayCameraInput(@event))
		{
			GetViewport()?.SetInputAsHandled();

			return;
		}

		if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
		{
			Vector2 local = _terrain!.GetLocalMousePosition();

			Vector2I cell = _terrain.LocalToMap(local);

			_ = BeginClickTurnAsync(cell);
		}
	}

	float GameplayZoomLoHi(out float hi)
	{
		float lo = Mathf.Min(GameplayCameraZoomLimitClose, GameplayCameraZoomLimitFar);
		hi = Mathf.Max(GameplayCameraZoomLimitClose, GameplayCameraZoomLimitFar);

		const float epsilon = 1e-5f;

		if (hi - lo < epsilon)
			hi = lo + epsilon;

		return lo;
	}

	bool HandleGameplayCameraInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mb)
		{
			float f = Mathf.Clamp(GameplayCameraZoomWheelFactor, 1.01f, 2f);

			if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed)
			{
				ZoomGameplayCameraAtViewportMouse(f);
				return true;
			}

			if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed)
			{
				ZoomGameplayCameraAtViewportMouse(1f / f);
				return true;
			}

			if (mb.ButtonIndex is MouseButton.Middle or MouseButton.Right)
				return true;
		}

		if (@event is InputEventMouseMotion mm && (Input.IsMouseButtonPressed(MouseButton.Middle)
												|| Input.IsMouseButtonPressed(MouseButton.Right)))
		{
			_camera!.Position -= mm.Relative / _camera.Zoom;
			return true;
		}

		return false;
	}

	void ZoomGameplayCameraAtViewportMouse(float factor)
	{
		float lo = GameplayZoomLoHi(out float hi);
		Vector2 nzv = _camera!.Zoom;

		float nz = Mathf.Clamp(nzv.X * factor, lo, hi);
		if (Mathf.IsEqualApprox(nz, nzv.X))
			return;

		Viewport vp = GetViewport();
		Vector2 mouse = vp.GetMousePosition();

		Transform2D inv = vp.GetCanvasTransform().AffineInverse();
		Vector2 pivotWorld = inv * mouse;

		float ratio = nzv.X / nz;
		_camera.Position = pivotWorld + (_camera.Position - pivotWorld) * ratio;
		_camera.Zoom = new Vector2(nz, nz);
	}




	async Task BeginClickTurnAsync(Vector2I cell)


	{


		await HandleClickAsync(cell);





	}





	async Task HandleClickAsync(Vector2I cell)


	{


		if (_turn != Turn.Player)


			return;




		string ck = HexGridUtil.CellKey(cell);





		if (!_valid.ContainsKey(ck))


			return;




		if (CellHasFog(ck))


		{


			await ToastAsync("受阻", "仍有迷雾遮挡，无法移动到或交互该格。");


			return;


		}




		bool adj = false;





		foreach (Vector2I n in HexGridUtil.Neighbors(_terrain!, _playerCell))


		{





			if (HexGridUtil.IsSameCell(n, cell))


			{





				adj = true;





				break;



			}




		}





		if (!adj)


			return;




		if (_spentBasic)


			return;




		if (_busyPlayerAction)


			return;




		_busyPlayerAction = true;


		try


		{



			if (_events.ContainsKey(ck))


				await TryInteractAsync(cell);



			else


				await TryMoveAsync(cell);



		}


		finally


		{


			_busyPlayerAction = false;


		}


	}





	async Task ToastAsync(string title, string msg)


	{

		if (_hudUi == null)




			return;



		await _hudUi.ToastAsync(title, msg);



	}





	async Task TryMoveAsync(Vector2I dst)


	{


		if (HexGridUtil.IsSameCell(dst, _playerCell))


			return;




		string dk = HexGridUtil.CellKey(dst);





		if (_blockState.ContainsKey(dk) && _blockState[dk].AsBool())


		{





			await ToastAsync("受阻", "该格是不可通行的障碍占位。");



			return;



		}




		if (_events.ContainsKey(dk))


		{





			await ToastAsync("受阻", "该格有事件占位，不能直接走入。请先相邻触发。");



			return;



		}


		Vector2I origin = _playerCell;

		await ApproachCellWithWalkAsync(origin, dst);

		_playerCell = dst;


		SetPlayerIdleVisual();


		SnapPlayer();




		await EnergyAbsorbAsync();






		await ToSignal(GetTree().CreateTimer(0.06f), SceneTreeTimer.SignalName.Timeout);



		_spentBasic = true;

		RefreshHud();

		RunState.Instance.ClampHp();

		await CheckFailWinAsync();


		await FinishIfNeededAsync();





	}







	async Task EnergyAbsorbAsync()



	{

		int gained = 0;

		foreach (Vector2I n in HexGridUtil.Neighbors(_terrain!, _playerCell))
		{
			string nk = HexGridUtil.CellKey(n);
			if (!_valid.ContainsKey(nk) || !_fogState.ContainsKey(nk))
				continue;


			if (!_fogState[nk].AsBool())
				continue;

			if (FogNeighborAbsorptionLocked(nk))
				continue;

			_fogState[nk] = false;

			_fog!.SetCell(n, false);
			gained++;
			NotifyMonsterFogRevealedIfNeeded(n);
		}

		RunState.Instance.PlayerEnergy = Mathf.Min(RunState.Instance.PlayerEnergy + gained, RunState.Instance.PlayerEnergyMax);

		if (gained > 0)
			await ToastAsync("吸收迷雾", $"获得能量 +{gained}（仅移动连带吸收计数）。");

		RefreshEventIconsFogVisibility();

	}

	void LoadPlayerTexturesForWorldSprite()
	{
		_playerIdleTex = null;
		foreach (string p in PlayerIdleTextureCandidates)
		{
			if (ResourceLoader.Exists(p))
			{
				_playerIdleTex = GD.Load<Texture2D>(p);
				break;
			}
		}

		for (int i = 0; i < 4; i++)
		{
			string path = PlayerWalkFramePaths[i];
			_playerWalkFrames[i] = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
		}

		if (_playerSprite != null)
		{
			ApplyPlayerWorldSpriteScaleFromTerrain();
			_playerSprite.FlipH = false;
			SetPlayerIdleVisual();
		}
	}

	void SetPlayerIdleVisual()
	{
		if (_playerSprite == null)
			return;

		_playerSprite.Texture = _playerIdleTex ?? _playerWalkFrames[0];
	}

	void SetPlayerWalkVisualFrame(int frameIndex)
	{
		if (_playerSprite == null)
			return;

		int i = ((frameIndex % 4) + 4) % 4;
		_playerSprite.Texture = _playerWalkFrames[i] ?? _playerIdleTex;
	}

	float PlayerSpriteAnchorOffsetYWorld() =>
		PlayerSpriteAnchorLayout.WorldOffsetYAnchorBelowCenter(
			_playerSprite?.Texture ?? _playerIdleTex ?? _playerWalkFrames[0],
			Mathf.Abs(_playerSprite?.Scale.Y ?? 1f));

	Vector2 PlayerWorldPositionForCell(Vector2I cell) =>
		_terrain!.MapToLocal(cell) + new Vector2(0f, PlayerSpriteAnchorOffsetYWorld());

	/// <summary>素材默认面朝「六角左边」三向；向右三向则用 FlipH。</summary>
	static bool HexStepMatchesSpriteDefaultLeftFacing(TileSet.CellNeighbor dir) =>
		dir is TileSet.CellNeighbor.LeftSide or TileSet.CellNeighbor.TopLeftSide
			or TileSet.CellNeighbor.BottomLeftSide;

	void ApplyPlayerFacingForAdjacentStep(Vector2I fromCell, Vector2I toCell)
	{
		if (_playerSprite == null || _terrain == null)
			return;

		if (!HexGridUtil.TryGetNeighborStepDirection(_terrain, fromCell, toCell, out TileSet.CellNeighbor dir))
			return;

		_playerSprite.FlipH = !HexStepMatchesSpriteDefaultLeftFacing(dir);
	}

	async Task ApproachCellWithWalkAsync(Vector2I fromCell, Vector2I toCell)
	{
		if (_playerSprite == null || _terrain == null)
			return;

		if (HexGridUtil.IsSameCell(fromCell, toCell))
			return;

		ApplyPlayerFacingForAdjacentStep(fromCell, toCell);

		Vector2 baseFrom = _terrain.MapToLocal(fromCell);
		Vector2 baseTo = _terrain.MapToLocal(toCell);

		float elapsed = 0f;
		const float step = 1f / 60f;
		double walkAccum = 0d;
		int walkIx = 0;
		double walkFrameDt = 1.0 / PlayerWalkFramesPerSecond;

		while (elapsed < PlayerMoveTweenSeconds)
		{
			await ToSignal(GetTree().CreateTimer(step), SceneTreeTimer.SignalName.Timeout);
			elapsed += step;
			float u = Mathf.Clamp(elapsed / PlayerMoveTweenSeconds, 0f, 1f);

			walkAccum += step;
			while (walkAccum >= walkFrameDt)
			{
				walkAccum -= walkFrameDt;
				SetPlayerWalkVisualFrame(walkIx++);
			}

			Vector2 basePos = baseFrom.Lerp(baseTo, u);
			_playerSprite.Position = basePos + new Vector2(0f, PlayerSpriteAnchorOffsetYWorld());
		}

		SetPlayerIdleVisual();
		_playerSprite.Position = baseTo + new Vector2(0f, PlayerSpriteAnchorOffsetYWorld());
	}

	async Task PlayerFightLossKnockbackAsync(Vector2I returnCell)
	{
		if (_playerSprite == null || _terrain == null)
		{
			_playerCell = returnCell;
			return;
		}

		// 受击动画素材待补：占位为短暂变色。
		Color saved = _playerSprite.Modulate;
		_playerSprite.Modulate = new Color(1f, 0.42f, 0.42f, 1f);

		ApplyPlayerFacingForAdjacentStep(_playerCell, returnCell);

		Vector2 from = PlayerWorldPositionForCell(_playerCell);
		Vector2 to = PlayerWorldPositionForCell(returnCell);

		float elapsed = 0f;
		const float step = 1f / 60f;

		while (elapsed < PlayerFightKnockbackSeconds)
		{
			await ToSignal(GetTree().CreateTimer(step), SceneTreeTimer.SignalName.Timeout);
			elapsed += step;
			float u = Mathf.Clamp(elapsed / PlayerFightKnockbackSeconds, 0f, 1f);
			_playerSprite.Position = from.Lerp(to, u);
		}

		_playerSprite.Modulate = saved;
		_playerCell = returnCell;
		SetPlayerIdleVisual();
		SnapPlayer();
	}


	async Task TryInteractAsync(Vector2I cell)



	{

		string ck = HexGridUtil.CellKey(cell);

		if (!_events.ContainsKey(ck))
			return;

		Godot.Collections.Dictionary ev = (Godot.Collections.Dictionary)_events[ck].AsGodotDictionary().Duplicate();

		string t = GetString(ev, "type");

		Vector2I approachOrigin = _playerCell;

		if (t != "altar")
		{
			await ApproachCellWithWalkAsync(approachOrigin, cell);
			_playerCell = cell;
			SetPlayerIdleVisual();
			SnapPlayer();
		}

		switch (t)
		{

			case "monster_str":
			case "monster_mag":

				await ResolveFightAsync(cell, ev, t == "monster_mag", approachOrigin);

				RunState.Instance.ClampHp();

				_spentBasic = true;

				RefreshHud();

				await CheckFailWinAsync();

				await FinishIfNeededAsync();

				return;



			case "treasure":


				EraseEvent(cell);
				GetNode<CanvasItem>("UICanvas/HUD/SkillChoose").Visible = true;
				var result = card_random(skillList);
				cardnum = result;
				for (int i = 0; i < 3; i++)
				{
					if (i >= result.Count)
					{
						break;
					}
					else if (result[i] > 100)
					{
						foreach (var item in parray)
						{
							var pskilldict = item.AsGodotDictionary();
							if (pskilldict["ID"].AsInt32() == result[i])
							{
								GetNode<TextureRect>("UICanvas/HUD/SkillChoose/Card" + (i + 1) + "/Button/SkillIcon").Texture = GD.Load<Texture2D>(pskilldict["address"].ToString());
								GetNode<Label>("UICanvas/HUD/SkillChoose/Card" + (i + 1) + "/SkillName").Text = pskilldict["name"].ToString();
								GetNode<Label>("UICanvas/HUD/SkillChoose/Card" + (i + 1) + "/SkillDescribe").Text = pskilldict["describe"].ToString();
								break;
							}
						}
					}
					else
					{
						foreach (var item in aarray)
						{
							var askilldict = item.AsGodotDictionary();
							if (askilldict["ID"].AsInt32() == result[i])
							{
								GetNode<TextureRect>("UICanvas/HUD/SkillChoose/Card" + (i + 1) + "/Button/SkillIcon").Texture = GD.Load<Texture2D>(askilldict["address"].ToString());
								GetNode<Label>("UICanvas/HUD/SkillChoose/Card" + (i + 1) + "/SkillName").Text = askilldict["name"].ToString();
								GetNode<Label>("UICanvas/HUD/SkillChoose/Card" + (i + 1) + "/SkillDescribe").Text = askilldict["describe"].ToString();
								break;
							}
						}
					}
				}
				break;



			case "altar":
				if (GetBool(ev, "altar_used"))
				{
					await ToastAsync("祭坛", "已使用过。");
					return;
				}




				Hud hud = _hudUi!;

				int pick = await hud.ModalThreeChoiceAsync("祭坛效果", "+1 力量", "+1 魔法", "+2 HP（不超上限）");


				switch (pick)


				{


					case 0:





						RunState.Instance.PlayerStr += 1;



						break;

					case 1:

						RunState.Instance.PlayerMagic += 1;

						break;



					default:

						if(RunState.Instance.PlayerHp + 2 > RunState.Instance.PlayerHpMax && pskillList.Contains(102))
						{
							SpawnGrassInRandomFog();
						}

						RunState.Instance.PlayerHp = Mathf.Min(RunState.Instance.PlayerHp + 2, RunState.Instance.PlayerHpMax);

						break;

				}

				ev["altar_used"] = true;

				_events[ck] = ev;
				RefreshBossEventIcon(ck);

				break;


			case "grass":
				if (GD.Randf() < 0.5f)
				{
					if (RunState.Instance.PlayerHp + 1 > RunState.Instance.PlayerHpMax && pskillList.Contains(102))
					{
						SpawnGrassInRandomFog();
					}
					RunState.Instance.PlayerHp = Mathf.Min(RunState.Instance.PlayerHp + 1, RunState.Instance.PlayerHpMax);
					await ToastAsync("草丛", "生命值 +1。");
				}
				else
					await ToastAsync("草丛", "无事发生（50%占位）。");

				if (pskillList.Contains(101))
				{
					grassskill = true;
				}

				if (pskillList.Contains(104))
				{
					grasscount++;
				}

				if (pskillList.Contains(105))
				{
					RunState.Instance.PlayerEnergy = Mathf.Min(RunState.Instance.PlayerEnergy + 2, RunState.Instance.PlayerEnergyMax);
				}

				EraseEvent(cell);
				break;


			case "corpse":
				if (pskillList.Contains(204))
				{
					corpseCount++;
					if(corpseCount >= 5)
					{
						Hud hud1 = _hudUi!;
						int pick1 = await hud1.ModalThreeChoiceAsync("祭坛效果", "+1 力量", "+1 魔法", "+2 HP（不超上限）");


						switch (pick1)


						{


							case 0:





								RunState.Instance.PlayerStr += 1;



								break;

							case 1:

								RunState.Instance.PlayerMagic += 1;

								break;



							default:

								if (RunState.Instance.PlayerHp + 2 > RunState.Instance.PlayerHpMax && pskillList.Contains(102))
								{
									SpawnGrassInRandomFog();
								}

								RunState.Instance.PlayerHp = Mathf.Min(RunState.Instance.PlayerHp + 2, RunState.Instance.PlayerHpMax);

								break;

						}
					}
				}

				if (GD.Randf() < 0.5f)
				{
					if (RunState.Instance.PlayerHp + 1 > RunState.Instance.PlayerHpMax && pskillList.Contains(102))
					{
						SpawnGrassInRandomFog();
					}
					RunState.Instance.PlayerHp = Mathf.Min(RunState.Instance.PlayerHp + 1, RunState.Instance.PlayerHpMax);
					if (pskillList.Contains(201))
					{
						RunState.Instance.PlayerEnergy = Mathf.Min(RunState.Instance.PlayerEnergy + 1, RunState.Instance.PlayerEnergyMax);
					}
					await ToastAsync("尸体", "+1 生命（50%）。");
				}
				else
				{
					RunState.Instance.PlayerHp -= 1;
					if (pskillList.Contains(202))
					{
						corpseHp = true;
					}
					await ToastAsync("尸体", "-1 生命（50%）。");
				}

				EraseEvent(cell);
				break;


			case "ruins":
				int rr = (int)(GD.Randi() % 3);
				switch (rr)
				{
					case 0:
						RunState.Instance.PlayerEnergy = Mathf.Min(RunState.Instance.PlayerEnergy + 2, RunState.Instance.PlayerEnergyMax);
						await ToastAsync("废墟", "占位：能量 +2。");
						break;

					case 1:
						if (RunState.Instance.PlayerHp + 1 > RunState.Instance.PlayerHpMax && pskillList.Contains(102))
						{
							SpawnGrassInRandomFog();
						}
						RunState.Instance.PlayerHp = Mathf.Min(RunState.Instance.PlayerHp + 1, RunState.Instance.PlayerHpMax);
						await ToastAsync("废墟", "占位：生命 +1。");
						break;

					default:
						RunState.Instance.PlayerHp -= 1;
						await ToastAsync("废墟", "占位：不幸，生命 -1。");
						break;

				}

				EraseEvent(cell);
				break;


			default:

				await ToastAsync("未知事件", t);

				break;

		}

		RunState.Instance.ClampHp();

		_spentBasic = true;

		RefreshHud();

		await CheckFailWinAsync();

		await FinishIfNeededAsync();

	}

	public async Task UseSkillAsync(int skillId)
	{
		if (skillId > 100)
		{
			skillList.Remove(skillId);
			pskillList.Add(skillId);
			var pname = "P" + (pskillList.Count);
			GetNode<CanvasItem>("UICanvas/HUD/PassiveSkillSlot/" + pname).Visible = true;
			foreach (var item in parray)
			{
				var pskilldict = item.AsGodotDictionary();
				int id = pskilldict["ID"].AsInt32();

				if (id == skillId)
				{
					string targetAddress = pskilldict["address"].AsString();
					GetNode<TextureRect>("UICanvas/HUD/PassiveSkillSlot/" + pname).Texture = GD.Load<Texture2D>(targetAddress);
					break;
				}
			}

		}
		else
		{
			//还没做
		}

	}



	static string GetString(Godot.Collections.Dictionary d, string key, string def = "")


	{


		return d.ContainsKey(key) ? d[key].AsString() : def;



	}





	async Task ResolveFightAsync(Vector2I cell, Godot.Collections.Dictionary ev, bool useMagic,
		Vector2I lossReturnCell)
	{
		int mv = GetInt(ev, "value", 1);
		int attr = useMagic ? RunState.Instance.PlayerMagic : RunState.Instance.PlayerStr;
		string label = useMagic ? "魔法" : "力量";
		string foe = GetString(ev, "name");
		if (string.IsNullOrEmpty(foe))
			foe = "怪物";
		string snippet = GetString(ev, "description");
		string extra = string.IsNullOrEmpty(snippet) ? "\n" : $"\n{snippet}\n";

		if (attr >= mv)
		{
			await ToastAsync($"{foe} · 战胜", $"{extra}{label}检定：你的 {attr} ≥ 战力 {mv}。");
			if(corpseHp == true)
			{
				if (RunState.Instance.PlayerHp + 1 > RunState.Instance.PlayerHpMax && pskillList.Contains(102))
				{
					SpawnGrassInRandomFog();
				}

				RunState.Instance.PlayerHp = Mathf.Min(RunState.Instance.PlayerHp + 1, RunState.Instance.PlayerHpMax);
				corpseHp = false;
			}
			EraseEvent(cell);
			if(pskillList.Contains(203))
			{
				var corpseEvent = new Godot.Collections.Dictionary
				{
					{ "type", "corpse" }
				};
				_events[HexGridUtil.CellKey(cell)] = corpseEvent;
				SpawnSingleEventIcon(cell, corpseEvent);
			}
			await EnergyAbsorbAsync();
			return;
		}

		await ToastAsync($"{foe} · 落败", $"{extra}{label}检定：你的 {attr} < 战力 {mv}。");
		int loss = Mathf.Max(mv - attr, 1);

		RunState.Instance.PlayerHp -= loss;

		EraseEvent(cell);

		await PlayerFightLossKnockbackAsync(lossReturnCell);

	}







	void EraseEvent(Vector2I cell)


	{

		string ck = HexGridUtil.CellKey(cell);

		if (TryMonsterEventAtKey(ck, out _))
			ReleaseMonsterNeighborFogLocks(ck);

		_events.Remove(ck);

		var icons = GetNode<Node2D>("World/EventIcons");

		foreach (Node ch in icons.GetChildren())
		{
			if (ch.HasMeta(CellKeyMeta) && ch.GetMeta(CellKeyMeta).AsString() == ck)
			{
				ch.QueueFree();
				break;
			}

		}

	}





	async Task FinishIfNeededAsync()


	{

		if (grassskill)
		{
			_spentBasic = false;
		}
		grassskill = false;

		if (grasscount >= 3)
		{
			_spentBasic = false;
		}
		else if (grasscount == 2)
		{
			grasscount++;
		}

		if (!_spentBasic)


			return;




		await ToSignal(GetTree().CreateTimer(0.05f), SceneTreeTimer.SignalName.Timeout);


		_turn = Turn.Boss;

		RefreshHud();

		await BossTurnAsync();





	}







	int ResolveBossEffectKind()
	{
		if (_bossSkillEffect is >= 1 and <= 99)
			return _bossSkillEffect;
		string clue = $"{_bossSkillDetail}\n{_bossSkillText}";
		if (clue.Contains('雾'))
			return 1;
		if (clue.Contains("战力") || clue.Contains("战斗力"))
			return 2;
		if (clue.Contains('转') || (clue.Contains('力') && clue.Contains('魔')))
			return 3;
		return 1;
	}

	void CommitBossSkillPreviewLock()
	{
		if (!_bossUsesTableSkill || _terrain == null)
			return;
		if (_bossLockedCellKeys.Count > 0)
			return;
		HashSet<string> keys = BossSkillPlanner.ResolveLockedCellKeys(_terrain, _valid, _playerCell, _bossSkillTarget,
			_bossSkillArea, out _);
		foreach (string k in keys)
			_bossLockedCellKeys.Add(k);
		RebuildBossWarningVisual();
	}

	void RebuildBossWarningVisual()
	{
		if (_bossWarn != null && _terrain != null)
			_bossWarn.RebuildFromKeys(_bossLockedCellKeys);
	}

	void RefreshBossEventIcon(string ck)
	{
		if (!_events.ContainsKey(ck))
			return;
		Node2D icons = GetNode<Node2D>("World/EventIcons");
		foreach (Node ch in icons.GetChildren())
		{
			if (!ch.HasMeta(CellKeyMeta) || ch.GetMeta(CellKeyMeta).AsString() != ck)
				continue;
			if (ch is Sprite2D spr)
			{
				Godot.Collections.Dictionary ev = _events[ck].AsGodotDictionary();
				spr.Texture = HexEventMarker.TextureForEventDict(ev);
				return;
			}
		}
	}

	static Godot.Collections.Dictionary BuildMonsterEncounterDict(MonsterTable.Row row) =>
		new()
		{
			["monster_id"] = row.Id,
			["type"] = row.IsMagic ? "monster_mag" : "monster_str",
			["value"] = row.Power,
			["icon"] = row.IconPath,
			["name"] = row.Name,
			["description"] = row.Description,
		};

	void SpawnSingleEventIcon(Node2D iconsRoot, string cellKey, Godot.Collections.Dictionary ev)
	{
		Vector2I cell = HexGridUtil.ParseKey(cellKey);

		var spr = new Sprite2D();
		spr.Texture = HexEventMarker.TextureForEventDict(ev);
		if (spr.Texture != null)
		{
			spr.Scale = new Vector2(HexEventMarker.EventIconSpriteScale, HexEventMarker.EventIconSpriteScale);
			spr.Offset = new Vector2(0f, -spr.Texture.GetHeight() * 0.05f);
		}

		spr.Name = $"Ev_{cell.X}_{cell.Y}";
		spr.SetMeta(CellKeyMeta, cellKey);
		spr.Position = _terrain!.MapToLocal(cell);
		iconsRoot.AddChild(spr);
	}

	static List<string> ShuffledTakePrefix(IReadOnlyList<string> source, int take)
	{
		var copy = new List<string>(source);
		for (int i = copy.Count - 1; i > 0; i--)
		{
			int j = (int)(GD.Randi() % (i + 1));
			(copy[i], copy[j]) = (copy[j], copy[i]);
		}

		int keep = Mathf.Min(Mathf.Max(0, take), copy.Count);
		return copy.GetRange(0, keep);
	}

	/// <summary>在已通过本次技能套上迷雾的空格（无其它事件）上随机放置战斗怪；不占玩家格。</summary>
	void SpawnRandomBossAddsInFogCells(IReadOnlyList<string> cellsWhereBossJustFogged, int count)
	{
		if (count <= 0 || MonsterTable.All.Count == 0)
			return;

		string pk = HexGridUtil.CellKey(_playerCell);
		var candidates = new List<string>();
		foreach (string ck in cellsWhereBossJustFogged)
		{
			if (ck == pk)
				continue;
			if (!_valid.ContainsKey(ck))
				continue;
			if (!(_fogState.ContainsKey(ck) && _fogState[ck].AsBool()))
				continue;
			if (_events.ContainsKey(ck))
				continue;
			candidates.Add(ck);
		}

		List<string> pick = ShuffledTakePrefix(candidates, count);
		Node2D iconsRoot = GetNode<Node2D>("World/EventIcons");

		foreach (string ck in pick)
		{
			MonsterTable.Row? row = MonsterTable.PickBossSummonMonsterRow();
			if (row == null)
				break;
			Godot.Collections.Dictionary ev = BuildMonsterEncounterDict(row);
			MonsterTable.EnrichMonsterEvent(ev);
			_events[ck] = ev;
			SpawnSingleEventIcon(iconsRoot, ck, ev);
		}
	}

	async Task ExecuteBossTableSkillAsync()
	{
		if (_terrain == null || _fog == null)
			return;
		if (_bossLockedCellKeys.Count == 0)
			CommitBossSkillPreviewLock();

		string tip = string.IsNullOrWhiteSpace(_bossSkillText)
			? "BOSS 释放了技能。"
			: _bossSkillText;
		await ToastAsync(_bossName, tip);

		if (_bossUsesTableSkill && BossSkillParsing.TryParseFogMonsterSkill(_bossSkillDetail, _bossSkillText,
				_bossTableId, out BossSkillParsing.FogMonsterSpec fm))
		{
			var lockedKeys = new List<string>(_bossLockedCellKeys);

			List<string> fogTargets = fm.FogRandomSubsetFromLocked is int fk && fk > 0
				? ShuffledTakePrefix(lockedKeys, fk)
				: lockedKeys;

			foreach (string ck in fogTargets)
			{
				if (!_valid.ContainsKey(ck))
					continue;
				Vector2I c = HexGridUtil.ParseKey(ck);
				_fogState[ck] = true;
				_fog.SetCell(c, true);
				OnMonsterCellBecameFogCovered(c);
			}

			_fogGoalTotal = Mathf.Max(_fogGoalTotal, CountTrue(_fogState));
			SpawnRandomBossAddsInFogCells(fogTargets, fm.MonsterSpawnCount);

			RefreshEventIconsFogVisibility();

			_bossLockedCellKeys.Clear();
			_bossWarn?.ClearAll();
			_bossMeter = 0f;
			await MaybeFogDamageAsync();
			return;
		}

		int fx = ResolveBossEffectKind();

		foreach (string ck in _bossLockedCellKeys)
		{
			if (!_valid.ContainsKey(ck))
				continue;
			Vector2I c = HexGridUtil.ParseKey(ck);
			switch (fx)
			{
				case 2:
					if (_events.ContainsKey(ck))
					{
						var ev = (Godot.Collections.Dictionary)_events[ck].AsGodotDictionary().Duplicate();
						string ty = GetString(ev, "type");
						if (ty is "monster_str" or "monster_mag")
						{
							ev["value"] = GetInt(ev, "value", 1) + 1;
							_events[ck] = ev;
							RefreshBossEventIcon(ck);
						}
					}

					break;
				case 3:
					if (_events.ContainsKey(ck))
					{
						var ev = (Godot.Collections.Dictionary)_events[ck].AsGodotDictionary().Duplicate();
						string ty = GetString(ev, "type");
						if (ty == "monster_str")
							ev["type"] = "monster_mag";
						else if (ty == "monster_mag")
							ev["type"] = "monster_str";
						else
							break;
						_events[ck] = ev;
						RefreshBossEventIcon(ck);
					}

					break;
				default:
					_fogState[ck] = true;
					_fog.SetCell(c, true);
					OnMonsterCellBecameFogCovered(c);
					break;
			}
		}

		RefreshEventIconsFogVisibility();

		_bossLockedCellKeys.Clear();
		_bossWarn?.ClearAll();
		_bossMeter = 0f;
		await MaybeFogDamageAsync();
	}

	async Task BossTurnAsync()
	{
		float before = _bossMeter;
		_bossMeter += _bossGain;
		float chargeMax = Mathf.Max(_bossChargeMax, 1f);
		float cap = Mathf.Max(_bossWarnMax + _bossChargeMax, 1f);
		if (_bossUsesTableSkill)
		{
			if (before < chargeMax && _bossMeter >= chargeMax && _bossLockedCellKeys.Count == 0)
			{
				CommitBossSkillPreviewLock();
				if (_bossMeter < cap)
					await ToastAsync("预警", $"{_bossName}：技能影响范围已固定。");
			}
			if (_bossMeter >= cap)
				await ExecuteBossTableSkillAsync();
		}
		else if (_bossMeter >= cap)
		{
			_bossMeter = 0f;
			string tip = string.IsNullOrWhiteSpace(_bossSkillText)
				? "技能占位：套上迷雾并可能造成迷雾结算。"
				: _bossSkillText;
			await ToastAsync(_bossName, tip);
			string pk = HexGridUtil.CellKey(_playerCell);
			_fogState[pk] = true;
			_fog!.SetCell(_playerCell, true);
			OnMonsterCellBecameFogCovered(_playerCell);
			RefreshEventIconsFogVisibility();
			await MaybeFogDamageAsync();
		}
		RunState.Instance.ClampHp();
		await CheckFailWinAsync();
		if (RunState.Instance.PlayerHp <= 0)
			return;
		_turn = Turn.Player;
		_spentBasic = false;
		RefreshHud();
	}

	async Task MaybeFogDamageAsync()


	{


		string ck = HexGridUtil.CellKey(_playerCell);


		if (!(_fogState.ContainsKey(ck) && _fogState[ck].AsBool()))


			return;

		if (FogNeighborAbsorptionLocked(ck))


			return;



		RunState.Instance.PlayerHp -= 1;


		_fogState[ck] = false;


		_fog!.SetCell(HexGridUtil.ParseKey(ck), false);


		NotifyMonsterFogRevealedIfNeeded(_playerCell);


		RefreshEventIconsFogVisibility();


		RunState.Instance.ClampHp();


		await ToastAsync("迷雾缠身", "-1 HP（占位：吸收规则）");


	}





	async Task CheckFailWinAsync()


	{


		if (RunState.Instance.PlayerHp <= 0)


		{


			RunState.Instance.PendingLevelPath = "";


			await ToastAsync("失败", "生命归零。");


			GetTree().ChangeSceneToFile(MainMenuScene);


			return;


		}





		if (RemainingFog() <= 0)


		{


			RunState.Instance.PendingLevelPath = "";


			await ToastAsync("胜利", "全图迷雾已清空（占位：回主菜单）。");


			GetTree().ChangeSceneToFile(MainMenuScene);


		}





	}





	void RefreshHud()


	{


		if (_hudUi == null)


			return;




		_hudUi.SetPlayerStats(


			RunState.Instance.PlayerHp,


			RunState.Instance.PlayerHpMax,



			RunState.Instance.PlayerEnergy,



			RunState.Instance.PlayerEnergyMax,



			RunState.Instance.PlayerStr,



			RunState.Instance.PlayerMagic



		);







		string turnTxt = _turn switch


		{

			Turn.Boss => "BOSS 回合",


			Turn.Player when !_spentBasic => "玩家回合（左键：相邻移动 / 相邻事件交互）",

			_ => "玩家回合",


		};




		_hudUi.SetTurnText(turnTxt);







		_hudUi.SetBossHudTitle(_bossName);


		float maxSpan = Mathf.Max(_bossWarnMax + _bossChargeMax, 1e-3f);


		float m = Mathf.Clamp(_bossMeter, 0f, maxSpan);


		float chargeCur = Mathf.Min(m, _bossChargeMax);


		float warnCur = Mathf.Max(0f, m - _bossChargeMax);


		_hudUi.SetBossPhaseMeters(chargeCur, Mathf.Max(_bossChargeMax, 1e-3f), warnCur, Mathf.Max(_bossWarnMax, 1e-3f));



		float left = RemainingFog();




		float remaining01 =
			Mathf.Clamp(left / Mathf.Max(_fogGoalTotal, 1), 0f, 1f);



		_hudUi.SetFogRemainingRatio(remaining01);





	}





	void FitCamera()


	{

		if (_camera == null || _terrain == null)
			return;



		System.Collections.Generic.List<Vector2> pts = [];


		foreach (Variant vk in _valid.Keys)



		{

			Vector2 p = _terrain.MapToLocal(HexGridUtil.ParseKey(vk.AsString()));

			pts.Add(p);



		}





		if (pts.Count == 0)



			return;



		Vector2 sum = Vector2.Zero;

		foreach (Vector2 p in pts)
			sum += p;

		Vector2 center = sum / pts.Count;



		_camera.Position = center;

		Rect2 rect = new(pts[0], Vector2.Zero);

		for (int i = 1; i < pts.Count; i++)







			rect = rect.Expand(pts[i]);





		float span = Mathf.Max(rect.Size.X, rect.Size.Y);


		float div = Mathf.Max(GameplayCameraFitDivisor, 1f);
		float loFit = GameplayZoomLoHi(out float hiFit);
		float z = Mathf.Clamp(span / div, loFit, hiFit);



		_camera.Zoom = new Vector2(z, z);



	}







	void SnapPlayer()


	{

		if (_playerSprite == null || _terrain == null)


			return;

		SetPlayerIdleVisual();

		_playerSprite.Position = PlayerWorldPositionForCell(_playerCell);





	}

	List<int> card_random(List<int> cards)
	{
		var shuffled = cards.OrderBy(x => GD.Randf()).ToList();
		List<int> result = shuffled.Take(3).ToList();
		//result[0] = 101; //测试00000000000000000000000000000000000000000000000
		return result;
	}

	public void sure_button_pressed()
	{
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Sure/SureSelect").Visible = true;
	}


	public async void sure_button_pressed_close()
	{
		if (cardnum.Count < cardchoose)
		{
			return;
		}
		if (cardnum[cardchoose - 1] > 100)
		{
			await UseSkillAsync(cardnum[cardchoose - 1]);
		}
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Sure/SureSelect").Visible = false;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose").Visible = false;
	}

	public void click_card1()
	{
		cardchoose = 1;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card1/CardGlow").Visible = false;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card1/CardSelect").Visible = true;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card2/CardGlow").Visible = true;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card2/CardSelect").Visible = false;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card3/CardGlow").Visible = true;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card3/CardSelect").Visible = false;

	}

	public void click_card2()
	{
		cardchoose = 2;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card2/CardGlow").Visible = false;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card2/CardSelect").Visible = true;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card1/CardGlow").Visible = true;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card1/CardSelect").Visible = false;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card3/CardGlow").Visible = true;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card3/CardSelect").Visible = false;

	}

	public void click_card3()
	{
		cardchoose = 3;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card3/CardGlow").Visible = false;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card3/CardSelect").Visible = true;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card1/CardGlow").Visible = true;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card1/CardSelect").Visible = false;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card2/CardGlow").Visible = true;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card2/CardSelect").Visible = false;

	}

	public void SpawnGrassInRandomFog()
	{
		// 1. 收集所有有迷雾的格子
		List<Vector2I> fogCells = new List<Vector2I>();

		foreach (Variant key in _fogState.Keys)
		{
			if (_fogState[key].AsBool())  // 有迷雾
			{
				Vector2I cell = HexGridUtil.ParseKey(key.AsString());
				fogCells.Add(cell);
			}
		}

		// 2. 如果没有迷雾格子，就不生成
		if (fogCells.Count == 0)
			return;

		// 3. 随机选择一个迷雾格子
		int randomIndex = (int)(GD.Randi() % (uint)fogCells.Count);
		Vector2I targetCell = fogCells[randomIndex];

		// 4. 生成草丛
		AddGrassAt(targetCell);
	}

	public void AddGrassAt(Vector2I cell)
	{
		string ck = HexGridUtil.CellKey(cell);

		// 检查格子是否有效
		if (!_valid.ContainsKey(ck)) return;

		// 如果已经有事件，不覆盖
		if (_events.ContainsKey(ck)) return;

		// 创建草丛事件数据
		Godot.Collections.Dictionary grassEvent = new Godot.Collections.Dictionary
	{
		{ "type", "grass" }
	};

		// 添加到事件字典
		_events[ck] = grassEvent;

		// 生成图标
		SpawnSingleEventIcon(cell, grassEvent);
	}

	public void SpawnSingleEventIcon(Vector2I cell, Godot.Collections.Dictionary eventData)
	{
		var icons = GetNode<Node2D>("World/EventIcons");

		var spr = new Sprite2D();
		spr.Texture = HexEventMarker.TextureForEventDict(eventData);

		if (spr.Texture != null)
		{
			spr.Scale = new Vector2(0.34f, 0.34f);
			spr.Offset = new Vector2(0f, -spr.Texture.GetHeight() * 0.05f);
		}

		string ck = HexGridUtil.CellKey(cell);
		spr.Name = $"Ev_{cell.X}_{cell.Y}";
		spr.SetMeta(CellKeyMeta, ck);
		spr.Position = _terrain!.MapToLocal(cell);

		// 如果格子有迷雾，图标应该隐藏
		spr.Visible = !CellHasFog(ck);

		icons.AddChild(spr);
	}
}
	internal static class PlayerSpriteAnchorLayout
	{
		/// <summary>纵向锚点在贴图自上而下的比例（约 0.8）；锚点至底约占约 <c>1 - FromTopFraction</c>（约 0.2）。</summary>
		internal const float FromTopFraction = 0.83f;

		internal static float WorldOffsetYAnchorBelowCenter(Texture2D? tex, float scaleAbsY)
		{
			if (tex == null || scaleAbsY <= 0f)
				return 0f;

			float h = tex.GetHeight();
			return -(FromTopFraction - 0.5f) * h * scaleAbsY;
		}
	}
