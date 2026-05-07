using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

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

	List<int> skillList = new List<int>();
	List<int> askillList = new List<int>();
	List<int> pskillList = new List<int>();

	public Godot.Collections.Array aarray = Json.ParseString(FileAccess.GetFileAsString("res://skill/activeskill.json")).AsGodotArray();
	public Godot.Collections.Array parray = Json.ParseString(FileAccess.GetFileAsString("res://skill/passiveskill.json")).AsGodotArray();

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

		Texture2D? ptex = GD.Load<Texture2D>("res://Art/Role/player.png");
		if (_playerSprite != null && ptex != null)
		{
			_playerSprite.Texture = ptex;
			_playerSprite.Scale = new Vector2(0.42f, 0.42f);
		}

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

	void ApplyLevel(Godot.Collections.Dictionary d)
	{
		int terrainVar = TerrainTilesetFactory.ResolveTerrainVariantFromLevel(d);
		_terrain!.TileSet = TerrainTilesetFactory.CreateHexTileset(terrainVar);
		TerrainTilesetFactory.ApplyTerrainPresentation(_terrain);
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




		_playerCell = dst;



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



			_fogState[nk] = false;

			_fog!.SetCell(n, false);
			gained++;
		}

		RunState.Instance.PlayerEnergy = Mathf.Min(RunState.Instance.PlayerEnergy + gained, RunState.Instance.PlayerEnergyMax);

		if (gained > 0)
			await ToastAsync("吸收迷雾", $"获得能量 +{gained}（仅移动连带吸收计数）。");

		RefreshEventIconsFogVisibility();

	}




	async Task TryInteractAsync(Vector2I cell)



	{

		string ck = HexGridUtil.CellKey(cell);

		if (!_events.ContainsKey(ck))
			return;

		Godot.Collections.Dictionary ev = (Godot.Collections.Dictionary)_events[ck].AsGodotDictionary().Duplicate();

		string t = GetString(ev, "type");

		switch (t)
		{

			case "monster_str":
			case "monster_mag":

				await ResolveFightAsync(cell, ev, t == "monster_mag");

				RunState.Instance.ClampHp();

				_spentBasic = true;

				RefreshHud();

				await CheckFailWinAsync();

				await FinishIfNeededAsync();

				return;



			case "treasure":


				EraseEvent(cell);
				GetNode<CanvasItem>("UICanvas/HUD/SkillChoose").Visible = true;
				//随机
				//另一边选择+确定
				//另一边存入数组

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
					RunState.Instance.PlayerHp = Mathf.Min(RunState.Instance.PlayerHp + 1, RunState.Instance.PlayerHpMax);
					await ToastAsync("草丛", "生命值 +1。");
				}
				else
					await ToastAsync("草丛", "无事发生（50%占位）。");

				EraseEvent(cell);
				break;


			case "corpse":
				if (GD.Randf() < 0.5f)
				{
					RunState.Instance.PlayerHp = Mathf.Min(RunState.Instance.PlayerHp + 1, RunState.Instance.PlayerHpMax);
					await ToastAsync("尸体", "+1 生命（50%）。");
				}
				else
				{
					RunState.Instance.PlayerHp -= 1;
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
			var pname = "P" + pskillList.Count;
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





	async Task ResolveFightAsync(Vector2I cell, Godot.Collections.Dictionary ev, bool useMagic)
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
			EraseEvent(cell);
			await EnterCellAfterWinAsync(cell);
			return;
		}

		await ToastAsync($"{foe} · 落败", $"{extra}{label}检定：你的 {attr} < 战力 {mv}。");
		int loss = Mathf.Max(mv - attr, 1);

		RunState.Instance.PlayerHp -= loss;

		EraseEvent(cell);

	}





	async Task EnterCellAfterWinAsync(Vector2I cell)


	{

		_playerCell = cell;

		SnapPlayer();

		await EnergyAbsorbAsync();

	}





	void EraseEvent(Vector2I cell)


	{

		string ck = HexGridUtil.CellKey(cell);

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




		RunState.Instance.PlayerHp -= 1;


		_fogState[ck] = false;


		_fog!.SetCell(HexGridUtil.ParseKey(ck), false);


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




		_playerSprite.Position = _terrain.MapToLocal(_playerCell);


		if (_playerSprite.Texture != null)


			_playerSprite.Position += new Vector2(0f, -_playerSprite.Texture.GetHeight() * 0.06f);





	}

	public void sure_button_pressed()
	{
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Sure/SureSelect").Visible = true;
	}
		
	
	public void sure_button_pressed_close()
	{
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Sure/SureSelect").Visible = false;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose").Visible = false;
	}
		







}
