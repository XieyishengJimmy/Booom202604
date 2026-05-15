using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using System.Linq;

namespace Booom202604;

/// <summary>
/// 主闯关场景：事件格、BOSS、被动/宝箱技能等逻辑均在此挂载。
/// </summary>
public partial class Gameplay : Node2D
{
	private const string DefaultLevel = "res://levels/starter_level.json";
	private const string MainMenuScene = "res://Scenes/main_menu.tscn";
	private const string GameplayScenePath = "res://Scenes/gameplay.tscn";
	private const string VictoryScreenScene = "res://Scenes/victory_screen.tscn";
	private const string FailScreenScene = "res://Scenes/fail_screen.tscn";
	private const string CampaignPortalEventTypeName = "campaign_portal";

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

	/// <summary>主动技能槽 <c>SingleSkillN/SkillIcon</c> 下用于等比缩放显示的技能图节点（与 <c>UI.tscn</c> 中槽位视觉一致）。</summary>
	const string ActiveSkillIconTexRel = "/SkillIcon/IconTex";

	/// <summary>主动槽法力角标根节点 <c>SingleSkillN/Cost</c>（内含 <c>CostBg</c>、<c>CostValue</c>）。</summary>
	const string ActiveSkillCostRel = "/Cost";

	TileMapLayer? _terrain;
	FogLayer? _fog;
	BossWarningLayer? _bossWarn;
	InteractionHintLayer? _interactionHints;
	BlockLayer? _blocks;
	Sprite2D? _playerSprite;
	Hud? _hudUi;
	TextureRect? _bossCornerSplash;

	const string HpFloatFontPath = "res://思源黑体+SourceHanSansCN-Normal.otf";

	/// <summary>技能悬停 Tooltip（<c>TooltipCanvas/MessageLabel</c>）：相对场景原默认 16pt 放大约 50%。</summary>
	const int SkillTooltipFontSize = 24;

	const float HpFloatTipHoldSeconds = 1f;

	const float HpFloatTipFadeSeconds = 0.55f;

	const float HpFloatTipFadeLiftPixels = 76f;

	const int HpFloatTipFontSize = 26;

	static readonly Color HpFloatHealColor = new(0.22f, 0.82f, 0.38f, 1f);
	static readonly Color HpFloatDamageColor = new(0.92f, 0.24f, 0.22f, 1f);

	Font? _hpFloatFont;
	bool _hpFloatFontWarned;
	int? _lastHpForMapFloatTip;

	sealed class HpFloatTipRun
	{
		public Label Lbl = null!;
		public ulong SpawnTickMs;
		public bool FadeStarted;
		public ulong FadeStartTickMs;
		public Color BaseModulate;
	}

	readonly List<HpFloatTipRun> _hpFloatTipRuns = new();

	const float PlayerMoveTweenSeconds = 0.5f;
	const float PlayerFightKnockbackSeconds = 0.3f;
	const float PlayerWalkFramesPerSecond = 10f;

	const int PlayerInjuredSpriteCount = 6;

	/// <summary>
	/// 受击序列播放帧率：<strong>每秒</strong>切换的帧数（每帧停留 <c>1/此值</c> 秒）。
	/// </summary>
	const float PlayerInjuredFramesPerSecond = 12f;

	/// <summary>战败击退：受伤动画开始后，经此时间再开始向后位移。</summary>
	const float PlayerInjuredKnockbackDelaySeconds = 0.5f;

	static float PlayerInjuredFrameHoldSeconds =>
		1f / Mathf.Max(1e-3f, PlayerInjuredFramesPerSecond);

	static readonly string[] PlayerInjuredTexturePaths =
	[
		"res://Art/Player/injured01.png",
		"res://Art/Player/injured02.png",
		"res://Art/Player/injured03.png",
		"res://Art/Player/injured04.png",
		"res://Art/Player/injured05.png",
		"res://Art/Player/injured06.png",
	];

	const int PlayerIdleSpriteCount = 6;

	/// <summary>待机动画：<c>每秒 6 帧</c>（整段循环约 1 秒一轮）。</summary>
	const float PlayerIdleFramesPerSecond = 6f;

	static float PlayerIdleFrameHoldSeconds =>
		1f / Mathf.Max(1e-3f, PlayerIdleFramesPerSecond);

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

	Texture2D? _playerIdleFallbackTex;
	readonly Texture2D?[] _playerIdleFrames = new Texture2D?[PlayerIdleSpriteCount];

	bool _playerIdleAnimActive;
	int _idleFrameIndex;
	float _idleFrameAccum;

	readonly Texture2D?[] _playerWalkFrames = new Texture2D?[4];
	readonly Texture2D?[] _playerInjuredFrames = new Texture2D?[PlayerInjuredSpriteCount];
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
	string _loadedLevelPath = "";

	/// <summary>清雾胜利后已在场上生成宝箱与传送门，等待相邻交互（主线最后一关不出现宝箱/传送门，直接结算）。</summary>
	bool _campaignVictoryPickupPhase;

	/// <summary>本局已尝试过清雾胜利收尾（宝箱/传送门或终局直接去胜利界面），避免重复触发。</summary>
	bool _campaignVictoryExitsSpawned;

	/// <summary>已切至失败/结算场景，避免重复跳转或继续驱动本场景逻辑。</summary>
	bool _gameEnding;

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
	string _bossAiDescription = "";
	int _bossTableId;

	readonly HashSet<string> _bossLockedCellKeys = [];

	int _fogGoalTotal = 1;

	/// <summary>怪物公布后邻居一圈迷雾：不能被「移动顺带吸收」「迷雾缠身」驱散； refcount 为多怪共享格。</summary>
	readonly Dictionary<string, int> _fogNeighborAbsorptionLockRef = [];

	/// <summary>每只已亮相怪物格子 key → 曾为其加锁的邻居迷雾格。</summary>
	readonly Dictionary<string, HashSet<string>> _monsterNeighborFogLocksByAnchor = [];

	static readonly StringName CellKeyMeta = EventWorldIconFactory.CellKeyMetaName;

	/// <summary>逻辑上已无迷雾，但消散动画尚未播完的格（_world 上与仍有迷雾同属「遮挡物件」）。</summary>
	readonly HashSet<string> _fogRevealVisualPendingCells = [];

	bool CellHasFog(string ck) =>
		_fogState.ContainsKey(ck) && _fogState[ck].AsBool();

	bool CellOccludesFoggedWorldDecor(string ck) =>
		CellHasFog(ck) || _fogRevealVisualPendingCells.Contains(ck);

	void WireFogRevealVisualHandlers()
	{
		if (_fog == null)
			return;

		_fog.FogRevealAnimationStarted += OnFogRevealAnimationStarted;
		_fog.FogRevealAnimationFinished += OnFogRevealAnimationFinished;
	}

	void OnFogRevealAnimationStarted(string cellKey)
	{
		_fogRevealVisualPendingCells.Add(cellKey);
		RefreshEventIconsFogVisibility();
	}

	void OnFogRevealAnimationFinished(string cellKey)
	{
		_fogRevealVisualPendingCells.Remove(cellKey);
		RefreshEventIconsFogVisibility();
	}

	void RefreshEventIconsFogVisibility()
	{
		var icons = GetNodeOrNull<Node2D>("World/EventIcons");
		if (icons == null)
			return;

		Node2D? badgeOverlay = GetNodeOrNull<Node2D>("World/MonsterBadgeOverlay");

		foreach (Node ch in icons.GetChildren())
		{
			if (!ch.HasMeta(CellKeyMeta))
				continue;
			string ck = ch.GetMeta(CellKeyMeta).AsString();
			bool show = !CellOccludesFoggedWorldDecor(ck);
			if (ch is CanvasItem cv)
				cv.Visible = show;
			if (badgeOverlay != null && ch is Node2D nd &&
				nd.GetNodeOrNull<Sprite2D>(EventWorldIconFactory.MonsterBodyNodeName) != null)
				SyncMonsterBadgeFogVisibility(badgeOverlay, ck, show);
		}

		RefreshBlockSpritesFogVisibility();
	}

	void SyncMonsterBadgeFogVisibility(Node2D overlay, string cellKey, bool cellUnobstructed)
	{
		bool showPhys = false;
		bool showMag = false;
		if (_events.TryGetValue(cellKey, out Variant evVar))
		{
			Godot.Collections.Dictionary ev = evVar.AsGodotDictionary();
			string ty = GetString(ev, "type");
			showPhys = cellUnobstructed && ty == "monster_str";
			showMag = cellUnobstructed && ty == "monster_mag";
		}

		foreach (Node ch in overlay.GetChildren())
		{
			if (!ch.HasMeta(CellKeyMeta) || ch.GetMeta(CellKeyMeta).AsString() != cellKey)
				continue;
			if (ch is not CanvasItem cv)
				continue;
			string kind = ch.HasMeta(EventWorldIconFactory.StatBadgeKindMeta)
				? ch.GetMeta(EventWorldIconFactory.StatBadgeKindMeta).AsString()
				: "";
			if (kind == EventWorldIconFactory.StatBadgeKindPhysValue || ch.Name == EventWorldIconFactory.PhysBadgeNodeName)
				cv.Visible = showPhys;
			else if (kind == EventWorldIconFactory.StatBadgeKindMagValue || ch.Name == EventWorldIconFactory.MagBadgeNodeName)
				cv.Visible = showMag;
			else
				cv.Visible = false;
		}
	}

	void RefreshBlockSpritesFogVisibility()
	{
		if (_blocks == null)
			return;

		_blocks.ApplyFogVisibility(CellOccludesFoggedWorldDecor);
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

	/// <summary>仅技能等显式驱散：优先解除怪物邻格吸收锁，再清除该格迷雾（技能驱散优先于锁定）。</summary>
	public void DispelFogBySkill(Vector2I cell)
	{
		string ck = HexGridUtil.CellKey(cell);
		if (!_valid.ContainsKey(ck))
			return;

		// 必须先于「是否有雾」判断：否则落脚格雾已被其它逻辑清掉但锁引用仍在时，无法解除锁定。
		StripAllMonsterNeighborAssociationsForFogKey(ck);
		if (_terrain != null)
			_fog?.SetAbsorptionLockedVisual(cell, false);

		if (!(_fogState.ContainsKey(ck) && _fogState[ck].AsBool()))
			return;

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
	Godot.Collections.Dictionary aconfigDict = new Godot.Collections.Dictionary();
	Godot.Collections.Dictionary pconfigDict = new Godot.Collections.Dictionary();
	Godot.Collections.Dictionary acdDict = new Godot.Collections.Dictionary();
	Godot.Collections.Dictionary apowerDict = new Godot.Collections.Dictionary();


	public Godot.Collections.Array aarray = Json.ParseString(FileAccess.GetFileAsString("res://Data/activeskill.json")).AsGodotArray();
	public Godot.Collections.Array parray = Json.ParseString(FileAccess.GetFileAsString("res://Data/passiveskill.json")).AsGodotArray();

	public List<int> cardnum = [];
	public int cardchoose = 0;

	public bool grassskill = false;
	/// <summary>被动 103：走入尸体格后视同「快速行动」，本回合仍可再动。</summary>
	bool _corpseExtraAction;
	public int grasscount = 0;
	public bool corpseHp = false;
	public int corpseCount = 0;
	public int skillchoose = 0;
	public bool ischose = false;

	public bool _isWaitingForTarget = false;
	public HashSet<string> _highlightedCells = new HashSet<string>();
	public bool _isWaitingForHighlightClick = false;
	public TaskCompletionSource<Vector2I>? _clickTcs;
	public Vector2I _lastHoverCell = Vector2I.Zero;
	public HighlightLayer? _highlightLayer;
	readonly HashSet<string> _cachedNeighborHintKeys = [];
	public readonly Dictionary<string, Sprite2D> _sprites = new();
	public readonly Texture2D _highlightTex = GD.Load<Texture2D>("res://Art/Role/player.png")!;

	public bool strBuff = false;
	public bool magicBuff = false;
	int _strBuffFightBonusAmt;
	int _magBuffFightBonusAmt;
	int _passive108GrassTriggers;
	public int fastRun = 0;

	private Panel? _tooltip;
	private Label? _tooltipLabel;

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
		_bossCornerSplash = GetNodeOrNull<TextureRect>("%BossCornerSplash");
		if (_bossCornerSplash != null)
			GetViewport().SizeChanged += OnViewportSizeChangedForBossCorner;

		var portraitTex = GD.Load<Texture2D>("res://Art/Player/head.png");
		if (portraitTex != null)
			_hudUi.SetPortrait(portraitTex);

		_hpFloatFont = ResourceLoader.Load<Font>(HpFloatFontPath);
		if (_hpFloatFont == null && !_hpFloatFontWarned)
		{
			_hpFloatFontWarned = true;
			GD.PrintErr($"[Gameplay] 未找到生命值飘字字体：{HpFloatFontPath}（请将思源黑体 OTF 放在项目根目录）");
		}

		_fog = GetNode<FogLayer>("World/FogRoot");
		_bossWarn = GetNodeOrNull<BossWarningLayer>("World/BossWarningRoot");
		_blocks = GetNode<BlockLayer>("World/BlockRoot");
		WireFogRevealVisualHandlers();

		LoadPlayerTexturesForWorldSprite();

		string path = RunState.Instance.PendingLevelPath;
		if (string.IsNullOrWhiteSpace(path))
			path = LevelJsonPath;

		_loadedLevelPath = LevelCatalog.NormalizeResPath(path);

		Godot.Collections.Dictionary lvl = LevelIo.LoadFromFile(path);
		if (lvl.Count == 0)
			lvl = LevelIo.LoadFromFile(DefaultLevel);

		List<int> activeList = new List<int>();
		List<int> passiveList = new List<int>();
		List<int> aconfigList = new List<int>();
		List<int> pconfigList = new List<int>();
		List<int> acdList = new List<int>();
		List<int> apowerList = new List<int>();
		foreach (var item in parray)
		{
			var pskilldict = item.AsGodotDictionary();
			int pskillid = pskilldict["ID"].AsInt32();
			passiveList.Add(pskillid);
			if (pskilldict.ContainsKey("config"))
			{
				pconfigList.Add(ReadPassiveConfigVariant(pskilldict["config"]));
			}
			else
			{
				pconfigList.Add(0);
			}
			
		}
		for (int i = 0; i < passiveList.Count; i++)
		{
			int id = passiveList[i];
			int value = pconfigList[i];
			pconfigDict[id] = value;
		}

		foreach (var item in aarray)
		{
			var askilldict = item.AsGodotDictionary();
			int askillid = askilldict["ID"].AsInt32();
			activeList.Add(askillid);
			acdList.Add(askilldict["cd"].AsInt32());
			apowerList.Add(askilldict["power"].AsInt32());
			if (askilldict.ContainsKey("config"))
			{
				aconfigList.Add(askilldict["config"].AsInt32());
			}
			else 
			{
				aconfigList.Add(0);
			}
			
		}
		for (int i = 0; i < activeList.Count; i++)
		{
			int id = activeList[i];
			int value = aconfigList[i];
			aconfigDict[id] = value;
		}
		for (int i = 0; i < activeList.Count; i++)
		{
			int id = activeList[i];
			int value = acdList[i];
			acdDict[id] = value;
		}
		for (int i = 0; i < activeList.Count; i++)
		{
			int id = activeList[i];
			apowerDict[id] = apowerList[i];
		}

		_tooltip = GetNodeOrNull<Panel>("TooltipCanvas/Tooltip");
		_tooltipLabel = _tooltip?.GetNodeOrNull<Label>("MessageLabel");

		if (_tooltip != null && _tooltipLabel != null)
		{
			// 设置九宫格背景
			SetupTooltipStyle();
			_tooltip.Visible = false;
		}

		skillList.Clear();
		pskillList.Clear();
		askillList.Clear();
		if (!RunState.Instance.TryConsumeCampaignSkillInto(skillList, pskillList, askillList))
		{
			foreach (int pid in passiveList)
				skillList.Add(pid);
			foreach (int aid in activeList)
				skillList.Add(aid);
			// 新开局：默认已装备主动技能 9，并从随机获取池中移除以免重复获得。
			askillList.Add(9);
			skillList.Remove(9);
		}

		ApplyLevel(lvl);
		RebuildLearnedSkillsHud();
		RecalculatePlayerEnergyMaxFromPassives();
		SnapPlayer();
		RefreshHud();
		powerCheck();
		FitCamera();
		cardchoose = 0;
		if (GetNodeOrNull<Button>("UICanvas/HUD/SkillChoose/Sure") is { } skillPickSureInit)
			skillPickSureInit.Disabled = true;

		Callable.From(DeferredInitialFogManaSoftLockProbe).CallDeferred();
		_highlightLayer = GetNode<HighlightLayer>("World/HighlightRoot");
		_highlightLayer.Setup(_terrain);
		_interactionHints = GetNodeOrNull<InteractionHintLayer>("World/InteractionHintRoot");
		_interactionHints?.Setup(_terrain!);
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

	static int ReadPassiveConfigVariant(Variant cfgVar)
	{
		return cfgVar.VariantType switch
		{
			Variant.Type.Int => cfgVar.AsInt32(),
			Variant.Type.Float => (int)cfgVar.AsDouble(),
			Variant.Type.String when int.TryParse(cfgVar.AsString().Trim(),
				System.Globalization.NumberStyles.Integer,
				System.Globalization.CultureInfo.InvariantCulture, out int parsed) => parsed,
			_ => 0,
		};
	}

	/// <summary>三选一描述：中文等无空格文本须 WordSmart 换行。</summary>
	static void ApplySkillPickDescribe(Label lab, string describe)
	{
		lab.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		lab.ClipContents = false;
		lab.Text = describe;
	}

	/// <summary>玩家 <c>Sprite2D.Scale</c> 与 <c>Scenes/map.tscn</c> 中 Idel01/地砖一致（<c>1,1</c>）。</summary>
	void SyncPlayerSpriteScaleToTerrain()
	{
		if (_playerSprite == null)
			return;

		_playerSprite.Scale = TerrainTilesetFactory.PlayerWorldScaleMapSceneReference;
	}


	void ApplyLevel(Godot.Collections.Dictionary d)
	{
		int terrainVar = TerrainTilesetFactory.ResolveTerrainVariantFromLevel(d);
		_terrain!.TileSet = TerrainTilesetFactory.CreateHexTileset(terrainVar);
		TerrainTilesetFactory.ApplyTerrainPresentation(_terrain);
		SyncPlayerSpriteScaleToTerrain();
		_terrain.Clear();
		_valid.Clear();
		_fogState.Clear();
		_blockState.Clear();
		_events.Clear();

		foreach (Node ch in GetNode("World/EventIcons").GetChildren())
			ch.QueueFree();

		Node2D? badgeOv = GetNodeOrNull<Node2D>("World/MonsterBadgeOverlay");
		if (badgeOv != null)
		{
			foreach (Node ch in badgeOv.GetChildren())
				ch.QueueFree();
		}

		_campaignVictoryPickupPhase = false;
		_campaignVictoryExitsSpawned = false;

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
		_bossAiDescription = "";
		_bossTableId = 0;
		_bossLockedCellKeys.Clear();
		_bossWarn?.ClearAll();
		SyncScreenEdgeWarningWithBossMapPreview();
		_fogNeighborAbsorptionLockRef.Clear();
		_monsterNeighborFogLocksByAnchor.Clear();
		_fogRevealVisualPendingCells.Clear();

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
				_bossAiDescription = string.IsNullOrWhiteSpace(br.AiDescription) ? "" : br.AiDescription;
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

				_bossAiDescription = "";

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

				_bossAiDescription = "";

				_bossWarnMax = 50f;
				_bossChargeMax = 50f;
				_bossGain = Mathf.Max(_bossGain, 1f);
				_bossName = "BOSS";
				_bossSkillText = "";
			}
		}

		ApplyBossCornerSplashForCurrentBossConfig();

		_bossWarn?.Setup(_terrain!);


		_bossMeter = 0f;

	}

	void OnViewportSizeChangedForBossCorner()
	{
		if (_bossCornerSplash is { Visible: true, Texture: not null })
			LayoutBossCornerSplashPixels();
	}

	void ApplyBossCornerSplashForCurrentBossConfig()
	{
		if (_bossUsesTableSkill && BossTable.TryGet(_bossTableId, out BossTable.Row? br) && br != null)
			RefreshBossCornerSplashFromTableRow(br);
		else
			RefreshBossCornerSplashFromTableRow(null);
	}

	/// <summary>右下角对齐窗口右下角；贴图 1:1 像素尺寸，路径为表字段 boss_image_id 对应 <c>res://Art/BOSS/</c> 下 PNG。</summary>
	void RefreshBossCornerSplashFromTableRow(BossTable.Row? br)
	{
		if (_bossCornerSplash == null)
			return;

		int imageId = br == null ? 0 : BossTable.ResolveCornerSplashPngNumericId(br);
		if (imageId <= 0)
		{
			_bossCornerSplash.Texture = null;
			_bossCornerSplash.Visible = false;
			return;
		}

		string resPath = $"res://Art/BOSS/{imageId}.png";
		if (!ResourceLoader.Exists(resPath))
		{
			GD.PushWarning($"[Gameplay] BOSS 立绘不存在：{resPath}（解析编号={imageId}，表 boss_image_id={br?.BossImageId ?? 0}）");
			_bossCornerSplash.Texture = null;
			_bossCornerSplash.Visible = false;
			return;
		}

		Texture2D? tex = GD.Load<Texture2D>(resPath);
		if (tex == null)
		{
			_bossCornerSplash.Visible = false;
			return;
		}

		_bossCornerSplash.Texture = tex;
		_bossCornerSplash.MouseFilter = Control.MouseFilterEnum.Ignore;
		_bossCornerSplash.ExpandMode = TextureRect.ExpandModeEnum.KeepSize;
		_bossCornerSplash.StretchMode = TextureRect.StretchModeEnum.Scale;
		LayoutBossCornerSplashPixels();
		_bossCornerSplash.Visible = true;
	}

	void LayoutBossCornerSplashPixels()
	{
		if (_bossCornerSplash == null || _bossCornerSplash.Texture is not Texture2D tex)
			return;

		Vector2 sz = new(tex.GetWidth(), tex.GetHeight());
		Vector2 vp = GetViewport().GetVisibleRect().Size;
		_bossCornerSplash.CustomMinimumSize = sz;
		_bossCornerSplash.Size = sz;
		_bossCornerSplash.Position = vp - sz;
	}

	void SpawnEventIcons()


	{


		var icons = GetNode<Node2D>("World/EventIcons");


		foreach (Variant vk in _events.Keys)



		{

			string ckStr = vk.AsString();

			Godot.Collections.Dictionary ev = _events[vk].AsGodotDictionary();

			var host = EventWorldIconFactory.BuildIconRoot(ev, HexEventMarker.EventIconSpriteScale);

			Vector2I cell = HexGridUtil.ParseKey(ckStr);

			host.Name = $"Ev_{cell.X}_{cell.Y}";

			host.SetMeta(CellKeyMeta, ckStr);

			host.Position = _terrain!.MapToLocal(cell);

			icons.AddChild(host);

			TryReparentMonsterStatBadges(host, ckStr);

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

	void GoToFailScreenFromGameplay()
	{
		if (_gameEnding)
			return;

		_gameEnding = true;

		RunState.Instance.PrepareReturnToMainMenu();

		GetTree().ChangeSceneToFile(FailScreenScene);
	}


	/// <summary>已装备或仍在牌池中的主动技能（ID≤100）全部高于当前法力时为 true；若无任何主动则 false（仍可能靠被动选牌破局）。</summary>
	bool PlayerCannotAffordAnyAvailableActiveSkill()
	{
		HashSet<int> ids = [];

		foreach (int id in askillList)
		{
			if (id <= 100)
				ids.Add(id);
		}

		foreach (int id in skillList)
		{
			if (id <= 100)
				ids.Add(id);
		}

		if (ids.Count == 0)
			return false;

		int e = RunState.Instance.PlayerEnergy;

		foreach (int id in ids)
		{
			int cost = apowerDict.ContainsKey(id) ? apowerDict[id].AsInt32() : 999_999;

			if (e >= cost)
				return false;
		}

		return true;
	}


	bool PlayerHasAdjacentMoveOrInteract()
	{
		if (_terrain == null)
			return false;

		foreach (Vector2I n in HexGridUtil.Neighbors(_terrain, _playerCell))
		{
			string nk = HexGridUtil.CellKey(n);

			if (!_valid.ContainsKey(nk))
				continue;

			if (CellHasFog(nk))
				continue;

			if (_events.ContainsKey(nk))
				return true;

			if (_blockState.ContainsKey(nk) && _blockState[nk].AsBool())
			{
				if (pskillList.Contains(107))
					return true;
				continue;
			}

			return true;
		}

		if (!Passive208ExtraMoveRangeUnlocked())
			return false;

		foreach (Vector2I n1 in HexGridUtil.Neighbors(_terrain, _playerCell))
		{
			if (!CellIs208WalkThroughIntermediate(n1))
				continue;
			foreach (Vector2I n2 in HexGridUtil.Neighbors(_terrain, n1))
			{
				if (HexGridUtil.IsSameCell(n2, _playerCell))
					continue;
				if (!CellIs208EmptyMoveDestination(n2))
					continue;
				return true;
			}
		}

		return false;
	}

	/// <summary>与 <see cref="PlayerHasAdjacentMoveOrInteract"/> 对单邻格判定一致（含被动 107：邻格障碍可走/可撞）。</summary>
	bool NeighborCellIsInteractableFromPlayer(Vector2I n)
	{
		if (_terrain == null)
			return false;

		string nk = HexGridUtil.CellKey(n);
		if (!_valid.ContainsKey(nk))
			return false;

		if (CellHasFog(nk))
			return false;

		if (_events.ContainsKey(nk))
			return true;

		if (_blockState.ContainsKey(nk) && _blockState[nk].AsBool())
			return pskillList.Contains(107);

		return true;
	}

	const int BaseCampaignPlayerEnergyMax = 10;

	void RecalculatePlayerEnergyMaxFromPassives()
	{
		int m = BaseCampaignPlayerEnergyMax;
		if (pskillList.Contains(206) && pconfigDict.ContainsKey(206))
			m += Mathf.Max(0, pconfigDict[206].AsInt32());
		RunState.Instance.PlayerEnergyMax = m;
		RunState.Instance.PlayerEnergy = Mathf.Min(RunState.Instance.PlayerEnergy, m);
	}

	bool Passive208ExtraMoveRangeUnlocked() => pskillList.Contains(208);

	bool CellIs208WalkThroughIntermediate(Vector2I c)
	{
		string ck = HexGridUtil.CellKey(c);
		if (!_valid.ContainsKey(ck) || CellHasFog(ck))
			return false;
		if (_blockState.ContainsKey(ck) && _blockState[ck].AsBool())
			return false;
		if (_events.ContainsKey(ck))
			return false;
		return true;
	}

	bool CellIs208EmptyMoveDestination(Vector2I c)
	{
		string ck = HexGridUtil.CellKey(c);
		if (!_valid.ContainsKey(ck) || CellHasFog(ck))
			return false;
		if (_blockState.ContainsKey(ck) && _blockState[ck].AsBool())
			return false;
		if (_events.ContainsKey(ck))
			return false;
		return true;
	}

	bool TryGet208TwoHopMid(Vector2I from, Vector2I dst, out Vector2I mid)
	{
		mid = default;
		if (_terrain == null || !CellIs208EmptyMoveDestination(dst))
			return false;

		foreach (Vector2I b in HexGridUtil.Neighbors(_terrain, from))
		{
			if (HexGridUtil.IsSameCell(b, dst))
				continue;
			if (!CellIs208WalkThroughIntermediate(b))
				continue;
			foreach (Vector2I x in HexGridUtil.Neighbors(_terrain, b))
			{
				if (HexGridUtil.IsSameCell(x, dst))
				{
					mid = b;
					return true;
				}
			}
		}

		return false;
	}

	bool HexCellsAreAdjacent(Vector2I from, Vector2I to)
	{
		if (_terrain == null)
			return false;
		foreach (Vector2I n in HexGridUtil.Neighbors(_terrain, from))
		{
			if (HexGridUtil.IsSameCell(n, to))
				return true;
		}

		return false;
	}

	/// <summary>本回合左键目标是否可达（相邻事件交互，或相邻/被动208二格空移；被动107相邻障碍格可走并撞毁）。</summary>
	bool CanPlayerActOnCellThisTurn(Vector2I cell)
	{
		if (_terrain == null)
			return false;

		string ck = HexGridUtil.CellKey(cell);
		if (!_valid.ContainsKey(ck) || CellHasFog(ck))
			return false;

		if (_events.ContainsKey(ck))
			return HexCellsAreAdjacent(_playerCell, cell) && NeighborCellIsInteractableFromPlayer(cell);

		if (_blockState.ContainsKey(ck) && _blockState[ck].AsBool())
			return pskillList.Contains(107) && HexCellsAreAdjacent(_playerCell, cell);

		if (HexCellsAreAdjacent(_playerCell, cell))
			return true;

		return Passive208ExtraMoveRangeUnlocked() && TryGet208TwoHopMid(_playerCell, cell, out _);
	}

	void SyncAdjacentInteractionHints()
	{
		if (_interactionHints == null || _terrain == null)
			return;

		if (_busyPlayerAction || _gameEnding || _isWaitingForHighlightClick)
		{
			if (_cachedNeighborHintKeys.Count != 0)
			{
				_cachedNeighborHintKeys.Clear();
				_interactionHints.ClearAll();
			}

			return;
		}

		if (_turn != Turn.Player || _spentBasic)
		{
			if (_cachedNeighborHintKeys.Count != 0)
			{
				_cachedNeighborHintKeys.Clear();
				_interactionHints.ClearAll();
			}

			return;
		}

		var keys = new HashSet<string>();
		foreach (Vector2I n in HexGridUtil.Neighbors(_terrain, _playerCell))
		{
			if (!NeighborCellIsInteractableFromPlayer(n))
				continue;

			keys.Add(HexGridUtil.CellKey(n));
		}

		if (Passive208ExtraMoveRangeUnlocked())
		{
			foreach (Vector2I n1 in HexGridUtil.Neighbors(_terrain, _playerCell))
			{
				if (!CellIs208WalkThroughIntermediate(n1))
					continue;
				foreach (Vector2I n2 in HexGridUtil.Neighbors(_terrain, n1))
				{
					if (HexGridUtil.IsSameCell(n2, _playerCell))
						continue;
					if (!CellIs208EmptyMoveDestination(n2))
						continue;
					keys.Add(HexGridUtil.CellKey(n2));
				}
			}
		}

		if (keys.SetEquals(_cachedNeighborHintKeys))
			return;

		_cachedNeighborHintKeys.Clear();
		foreach (string k in keys)
			_cachedNeighborHintKeys.Add(k);

		_interactionHints.RebuildFromKeys(_cachedNeighborHintKeys);
	}


	bool SoftLockSkillChoiceBlocksFailTransition()
	{
		var panel = GetNodeOrNull<CanvasItem>("UICanvas/HUD/SkillChoose");

		return panel != null && panel.Visible;
	}


	async Task MaybeTransitionToFailScreenForFogManaDeadlockAsync()
	{
		if (_gameEnding || _campaignVictoryPickupPhase)
			return;

		if (SoftLockSkillChoiceBlocksFailTransition())
			return;

		if (RemainingFog() <= 0)
			return;

		if (_turn != Turn.Player || _spentBasic)
			return;

		if (PlayerHasAdjacentMoveOrInteract())
			return;

		if (!PlayerCannotAffordAnyAvailableActiveSkill())
			return;

		GoToFailScreenFromGameplay();

		await Task.CompletedTask;
	}


	void DeferredInitialFogManaSoftLockProbe()
	{
		if (!IsInsideTree() || _gameEnding)
			return;

		_ = MaybeTransitionToFailScreenForFogManaDeadlockAsync();
	}








	HashSet<Vector2I> GatherValidCoordinates()
	{

		var s = new HashSet<Vector2I>();


		foreach (Variant vk in _valid.Keys)


			s.Add(HexGridUtil.ParseKey(vk.AsString()));


		return s;


	}




	bool CellEligibleForVictorySpawn(Vector2I c, Vector2I anchorCell, HashSet<Vector2I> extraForbidden)


	{

		string ck = HexGridUtil.CellKey(c);


		if (!_valid.ContainsKey(ck))


			return false;




		if (_blockState.ContainsKey(ck) && _blockState[ck].AsBool())


			return false;




		if (_events.ContainsKey(ck))


			return false;




		if (HexGridUtil.IsSameCell(c, anchorCell))


			return false;




		return !extraForbidden.Contains(c);


	}


	/// <summary>关卡 <c>block</c> 障碍格：BOSS 技能不得在此处套迷雾、生成怪物或改写事件。</summary>
	bool TerrainCellMarkedBlocked(string ck) =>
		_blockState.ContainsKey(ck) && _blockState[ck].AsBool();

	/// <summary>技能 7「空间折跃」可落点：版图内、无迷雾、非障碍；若有事件则不得为祭坛或战斗怪。</summary>
	bool IsSkill7TeleportDestination(Vector2I cell)
	{
		string ck = HexGridUtil.CellKey(cell);
		if (!_valid.ContainsKey(ck))
			return false;
		if (CellHasFog(ck))
			return false;
		if (TerrainCellMarkedBlocked(ck))
			return false;
		if (!_events.ContainsKey(ck))
			return true;
		string t = GetString(_events[ck].AsGodotDictionary(), "type");
		return t is not ("altar" or "monster_str" or "monster_mag");
	}



	void AttachRuntimeEvent(Vector2I cell, Godot.Collections.Dictionary ev)


	{

		string ck = HexGridUtil.CellKey(cell);


		Godot.Collections.Dictionary copy = (Godot.Collections.Dictionary)ev.Duplicate();




		copy["x"] = cell.X;


		copy["y"] = cell.Y;




		_events[ck] = copy;




		var icons = GetNode<Node2D>("World/EventIcons");


		DestroyEventIconsForCellKey(icons, ck);

		var host = EventWorldIconFactory.BuildIconRoot(copy, HexEventMarker.EventIconSpriteScale);
		host.Name = $"Ev_{cell.X}_{cell.Y}";
		host.SetMeta(CellKeyMeta, ck);
		host.Position = _terrain!.MapToLocal(cell);
		icons.AddChild(host);

		TryReparentMonsterStatBadges(host, ck);

	}




	async Task SpawnVictoryChestAndPortalAsync(Vector2I anchorCell)

	{

		HashSet<Vector2I> allowed = GatherValidCoordinates();

		List<Vector2I> neighbors = HexGridUtil.Neighbors(_terrain!, anchorCell);



		neighbors.Sort((a, b) => string.Compare(HexGridUtil.CellKey(a), HexGridUtil.CellKey(b), StringComparison.Ordinal));



		Vector2I? chestCell = null;



		HashSet<Vector2I> forbid = [];



		foreach (Vector2I n in neighbors)

		{

			if (!CellEligibleForVictorySpawn(n, anchorCell, forbid))

				continue;



			chestCell = n;

			break;

		}



		if (chestCell.HasValue)

			forbid.Add(chestCell.Value);



		Dictionary<Vector2I, int> depths = HexGridUtil.BfsStepsFrom(allowed, _terrain!, anchorCell);



		var portalOrdered = new List<(Vector2I C, int D)>();

		foreach (KeyValuePair<Vector2I, int> kv in depths)

		{

			Vector2I cPos = kv.Key;



			int d = kv.Value;

			if (d < 2)

				continue;



			if (!CellEligibleForVictorySpawn(cPos, anchorCell, forbid))

				continue;



			portalOrdered.Add((cPos, d));

		}



		portalOrdered.Sort((a, b) =>

		{

			int cmp = a.D.CompareTo(b.D);

			if (cmp != 0)

				return cmp;

			return string.Compare(HexGridUtil.CellKey(a.C), HexGridUtil.CellKey(b.C), StringComparison.Ordinal);

		});



		Vector2I? portalCell = portalOrdered.Count > 0 ? portalOrdered[0].C : null;



		string hint = "";



		if (chestCell.HasValue)

		{

			Vector2I c = chestCell.Value;

			Godot.Collections.Dictionary tev = new()

			{

				["type"] = "treasure",

				["value"] = 0f,

			};

			AttachRuntimeEvent(c, tev);

		}

		else

			hint = "相邻无可用空格，未生成宝箱。";



		if (portalCell.HasValue)

		{

			Vector2I pCell = portalCell.Value;

			Godot.Collections.Dictionary pev = new()

			{

				["type"] = CampaignPortalEventTypeName,

				["icon"] = "res://Art/Icon/Portal.png",

				["value"] = 0f,

			};

			AttachRuntimeEvent(pCell, pev);

		}

		else

			hint = string.IsNullOrEmpty(hint)

				? "未能在距你≥两步格处放置传送门。"

				: hint + " 传送门同上。";



		await ToastAsync("胜利", string.IsNullOrEmpty(hint)

			? "清雾完成！相邻开箱领奖，走远一步进入传送门前往下一关。"

			: "清雾完成。" + hint);



		RefreshEventIconsFogVisibility();

	}

	/// <summary>数字键 1–6：触发当前已解锁的第 1–6 个主动技能槽（与 HUD 按钮一致）。</summary>
	bool TryTriggerActiveSkillHotkey(int slot1To6)
	{
		if (_busyPlayerAction || _gameEnding || _isWaitingForHighlightClick)
			return false;
		if (_turn != Turn.Player || _spentBasic)
			return false;
		if (GetNodeOrNull<CanvasItem>("UICanvas/HUD/SkillChoose") is { Visible: true })
			return false;
		if (slot1To6 < 1 || slot1To6 > askillList.Count || slot1To6 > 6)
			return false;
		if (!CanUseActiveSkillSlot(slot1To6))
			return false;

		switch (slot1To6)
		{
			case 1: click_active1(); return true;
			case 2: click_active2(); return true;
			case 3: click_active3(); return true;
			case 4: click_active4(); return true;
			case 5: click_active5(); return true;
			case 6: click_active6(); return true;
			default: return false;
		}
	}


	public override void _UnhandledInput(InputEvent @event)


	{

		if (_camera != null && HandleGameplayCameraInput(@event))
		{
			GetViewport()?.SetInputAsHandled();

			return;
		}

		if (@event is InputEventKey keyEv && keyEv.Pressed && !keyEv.Echo)
		{
			int? hotSlot = keyEv.Keycode switch
			{
				Key.Key1 => 1,
				Key.Key2 => 2,
				Key.Key3 => 3,
				Key.Key4 => 4,
				Key.Key5 => 5,
				Key.Key6 => 6,
				_ => null,
			};
			if (hotSlot is int slot && TryTriggerActiveSkillHotkey(slot))
			{
				GetViewport()?.SetInputAsHandled();
				return;
			}
		}

		if (@event is InputEventMouseButton mb1 && mb1.Pressed && mb1.ButtonIndex == MouseButton.Left)
		{
			Vector2 local = _terrain!.GetLocalMousePosition();

			Vector2I cell = _terrain.LocalToMap(local);

			if (_isWaitingForHighlightClick)
			{
				OnCellClicked(cell);
				GetViewport()?.SetInputAsHandled();
				return;
			}

			_ = BeginClickTurnAsync(cell);
		}
		if (@event is InputEventMouseButton mb2 && mb2.Pressed && mb2.ButtonIndex == MouseButton.Right)
		{
			if (_isWaitingForHighlightClick)
			{
				CancelSkillWait();
				return;
			}
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

			if (mb.ButtonIndex is MouseButton.Middle)
				return true;
		}

		if (@event is InputEventMouseMotion mm && (Input.IsMouseButtonPressed(MouseButton.Middle)
	|| (Input.IsMouseButtonPressed(MouseButton.Right) && !_isWaitingForHighlightClick)))
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


			await MaybeTransitionToFailScreenForFogManaDeadlockAsync();


			return;


		}




		if (!CanPlayerActOnCellThisTurn(cell))
		{
			await MaybeTransitionToFailScreenForFogManaDeadlockAsync();
			return;
		}




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



		bool verbose = RunState.Instance?.DebugModeVerboseToasts ?? true;
		if (!verbose && !(title is "受阻" or "失败" or "胜利" or "传送门" or "闯关"))
			return;

		await _hudUi.ToastAsync(title, msg);



	}





	void ClearBossForCampaignVictory()
	{
		_bossMeter = 0f;
		_bossLockedCellKeys.Clear();
		_bossWarn?.ClearAll();
		SyncScreenEdgeWarningWithBossMapPreview();
		RefreshBossCornerSplashFromTableRow(null);
		RefreshHud();
	}

	void SetActiveSkillCostUi(int slot1To6, bool equipped, int skillId)
	{
		if (slot1To6 < 1 || slot1To6 > 6)
			return;
		string root = "UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill" + slot1To6;
		if (GetNodeOrNull<CanvasItem>(root + ActiveSkillCostRel) is not { } costRoot)
			return;
		if (!equipped)
		{
			costRoot.Visible = false;
			return;
		}

		costRoot.Visible = true;
		if (costRoot.GetNodeOrNull<Label>("CostValue") is { } lab)
		{
			int p = apowerDict.ContainsKey(skillId) ? apowerDict[skillId].AsInt32() : 0;
			lab.Text = p.ToString();
		}
	}

	void RebuildLearnedSkillsHud()
	{
		for (int i = 1; i <= 10; i++)
		{
			string key = $"UICanvas/HUD/PassiveSkillSlot/P{i}";
			if (GetNodeOrNull<CanvasItem>(key) is { } pn)
				pn.Visible = false;
		}

		for (int pi = 0; pi < pskillList.Count && pi < 10; pi++)
		{
			int sid = pskillList[pi];
			string slot = "P" + (pi + 1);
			var vis = GetNode<CanvasItem>("UICanvas/HUD/PassiveSkillSlot/" + slot);
			vis.Visible = true;
			foreach (var item in parray)
			{
				var pskilldict = item.AsGodotDictionary();
				if (pskilldict["ID"].AsInt32() != sid)
					continue;
				string targetAddress = pskilldict["address"].AsString();
				GetNode<TextureRect>("UICanvas/HUD/PassiveSkillSlot/" + slot).Texture = GD.Load<Texture2D>(targetAddress);
				break;
			}
		}

		for (int j = 1; j <= 6; j++)
		{
			string root = "UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill" + j;
			if (GetNodeOrNull<TextureRect>(root + "/SkillHi") is { } shi)
				shi.Visible = false;
			if (GetNodeOrNull<TextureRect>(root + ActiveSkillIconTexRel) is { } itex)
				itex.Texture = null;
			if (GetNodeOrNull<Button>(root + "/SkillIcon") is { } iconBtn)
				iconBtn.Icon = null;
			SetActiveSkillCostUi(j, false, 0);
		}

		for (int ai = 0; ai < askillList.Count && ai < 6; ai++)
		{
			int sid = askillList[ai];
			string aname = "SingleSkill" + (ai + 1);
			string rootPath = "UICanvas/HUD/ActiveSkillSlot/SkillArea/" + aname;
			foreach (var item in aarray)
			{
				var askilldict = item.AsGodotDictionary();
				if (askilldict["ID"].AsInt32() != sid)
					continue;
				string targetAddress = askilldict["address"].AsString();
				GetNode<TextureRect>(rootPath + ActiveSkillIconTexRel).Texture = GD.Load<Texture2D>(targetAddress);
				GetNode<TextureRect>(rootPath + "/SkillHi").Visible = true;
				SetActiveSkillCostUi(ai + 1, true, sid);
				break;
			}
		}
	}

	async Task TryMoveAsync(Vector2I dst)


	{


		if (HexGridUtil.IsSameCell(dst, _playerCell))


			return;




		string dk = HexGridUtil.CellKey(dst);





		if (_blockState.ContainsKey(dk) && _blockState[dk].AsBool())
		{
			if (!pskillList.Contains(107))
			{
				SpawnPlayerHeadFloatTip("不可通过", Colors.White);

				await MaybeTransitionToFailScreenForFogManaDeadlockAsync();

				return;
			}

			_blockState[dk] = false;
			_blocks?.SetBlocks(_blockState);
		}




		if (_events.ContainsKey(dk))


		{





			await ToastAsync("受阻", "该格有事件占位，不能直接走入。请先相邻触发。");



			await MaybeTransitionToFailScreenForFogManaDeadlockAsync();


			return;



		}


		Vector2I origin = _playerCell;
		bool use208TwoHop = false;
		Vector2I hopMid = default;

		if (!HexCellsAreAdjacent(origin, dst))
		{
			if (!Passive208ExtraMoveRangeUnlocked() || !TryGet208TwoHopMid(origin, dst, out hopMid))
				return;

			use208TwoHop = true;
		}

		if (use208TwoHop)
		{
			await ApproachCellWithWalkAsync(origin, hopMid);
			_playerCell = hopMid;
			SetPlayerIdleVisual();
			SnapPlayer();
			await ApproachCellWithWalkAsync(hopMid, dst);
		}
		else
			await ApproachCellWithWalkAsync(origin, dst);

		_playerCell = dst;


		SetPlayerIdleVisual();


		SnapPlayer();




		await EnergyAbsorbAsync();

		if (pskillList.Contains(206))
		{
			RunState.Instance.PlayerEnergy = Mathf.Min(RunState.Instance.PlayerEnergy + 1, RunState.Instance.PlayerEnergyMax);
			powerCheck();
		}

		await MaybeTriggerPassive207AdjacentGrassAfterMoveAsync(dst);






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
		powerCheck();

		if (gained > 0)
			await ToastAsync("吸收迷雾", $"获得能量 +{gained}（仅相邻移动后吸收）。");

		RefreshEventIconsFogVisibility();

	}

	void LoadPlayerTexturesForWorldSprite()
	{
		_playerIdleFallbackTex = null;
		foreach (string p in PlayerIdleTextureCandidates)
		{
			if (ResourceLoader.Exists(p))
			{
				_playerIdleFallbackTex = GD.Load<Texture2D>(p);
				break;
			}
		}

		for (int i = 0; i < PlayerIdleSpriteCount; i++)
		{
			int num = i + 1;
			string dd = num < 10 ? $"0{num}" : $"{num}";
			string tryIdle = $"res://Art/Player/idle{dd}.png";
			string tryIdel = $"res://Art/Player/idel{dd}.png";
			_playerIdleFrames[i] = ResourceLoader.Exists(tryIdle)
				? GD.Load<Texture2D>(tryIdle)
				: ResourceLoader.Exists(tryIdel)
					? GD.Load<Texture2D>(tryIdel)
					: null;
		}

		for (int i = 0; i < 4; i++)
		{
			string path = PlayerWalkFramePaths[i];
			_playerWalkFrames[i] = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
		}

		for (int i = 0; i < PlayerInjuredSpriteCount; i++)
		{
			string path = PlayerInjuredTexturePaths[i];
			_playerInjuredFrames[i] = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
		}

		if (_playerSprite != null)
		{
			_playerSprite.FlipH = false;
			SetPlayerIdleVisual();
		}
	}

	Texture2D? GetPlayerIdleFrameOrFallback(int zeroBasedFrame)
	{
		int ix = Mathf.Clamp(zeroBasedFrame, 0, PlayerIdleSpriteCount - 1);
		return _playerIdleFrames[ix] ?? _playerIdleFallbackTex ?? _playerWalkFrames[0];
	}

	void StopPlayerIdleLoop()
	{
		_playerIdleAnimActive = false;
		_idleFrameAccum = 0f;
	}

	void TickPlayerIdleLoop(float dt)
	{
		if (!_playerIdleAnimActive || _playerSprite == null)
			return;

		_idleFrameAccum += dt;

		float hold = PlayerIdleFrameHoldSeconds;

		while (_idleFrameAccum >= hold)
		{
			_idleFrameAccum -= hold;
			_idleFrameIndex = (_idleFrameIndex + 1) % PlayerIdleSpriteCount;
			_playerSprite.Texture = GetPlayerIdleFrameOrFallback(_idleFrameIndex);
		}
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
		TickPlayerIdleLoop((float)delta);
		SyncAdjacentInteractionHints();
		UpdateHpFloatTipsFollowing();
		if (_isWaitingForHighlightClick)
		{
			Vector2I currentHover = GetMouseHoverCell();

			// 只有悬停格子改变时才更新高亮
			if (currentHover != _lastHoverCell)
			{
				_lastHoverCell = currentHover;
				UpdateHighlightForCell(currentHover);
			}
		}
	}

	void SetPlayerIdleVisual()
	{
		if (_playerSprite == null)
			return;

		_playerIdleAnimActive = true;
		_idleFrameIndex = 0;
		_idleFrameAccum = 0f;
		_playerSprite.Texture = GetPlayerIdleFrameOrFallback(0);
		SyncPlayerSpriteScaleToTerrain();
	}

	void SetPlayerWalkVisualFrame(int frameIndex)
	{
		if (_playerSprite == null)
			return;

		StopPlayerIdleLoop();

		int i = ((frameIndex % 4) + 4) % 4;
		_playerSprite.Texture = _playerWalkFrames[i] ?? GetPlayerIdleFrameOrFallback(0);
	}

	bool PlayerInjuredSequenceAvailable() =>
		_playerSprite != null
		&& _playerInjuredFrames.Any(t => t != null);

	void SetPlayerInjuredVisualFrame(int zeroBasedIndex)
	{
		if (_playerSprite == null)
			return;

		StopPlayerIdleLoop();

		int fi = Mathf.Clamp(zeroBasedIndex, 0, PlayerInjuredSpriteCount - 1);
		Texture2D? frame = _playerInjuredFrames[fi] ?? GetPlayerIdleFrameOrFallback(0) ?? _playerWalkFrames[0];
		if (frame != null)
			_playerSprite.Texture = frame;
	}

	async Task PlayPlayerInjuredVisualSequenceIfAvailableAsync()
	{
		if (!PlayerInjuredSequenceAvailable())
			return;

		float frameDt = Mathf.Max(1e-3f, PlayerInjuredFrameHoldSeconds);

		for (int fi = 0; fi < PlayerInjuredSpriteCount; fi++)
		{
			if (_playerSprite == null)
				return;

			SetPlayerInjuredVisualFrame(fi);

			await ToSignal(GetTree().CreateTimer(frameDt), SceneTreeTimer.SignalName.Timeout);
		}

		SetPlayerIdleVisual();
	}

	float PlayerSpriteAnchorOffsetYWorld() =>
		PlayerSpriteAnchorLayout.WorldOffsetYAnchorBelowCenter(
			_playerSprite?.Texture ?? GetPlayerIdleFrameOrFallback(0) ?? _playerWalkFrames[0],
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

	async Task ApproachCellWithWalkAsync(Vector2I fromCell, Vector2I toCell, bool playWalkSfxDuringMove = true)
	{
		if (_playerSprite == null || _terrain == null)
			return;

		if (HexGridUtil.IsSameCell(fromCell, toCell))
			return;

		ApplyPlayerFacingForAdjacentStep(fromCell, toCell);

		if (playWalkSfxDuringMove)
			GameSfx.PlayWalk();

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

		StopPlayerIdleLoop();

		// 不退回翻面：保持走向交互目标后的朝向；受伤帧按「每秒 N 帧」节拍推进，延迟后再击退。
		Vector2 from = PlayerWorldPositionForCell(_playerCell);
		Vector2 to = PlayerWorldPositionForCell(returnCell);

		bool injured = PlayerInjuredSequenceAvailable();
		float injuredClipLen = PlayerInjuredSpriteCount * PlayerInjuredFrameHoldSeconds;

		float totalDur;
		if (!injured)
			totalDur = PlayerFightKnockbackSeconds;
		else
		{
			float movementSpan = PlayerInjuredKnockbackDelaySeconds + PlayerFightKnockbackSeconds;
			totalDur = Mathf.Max(movementSpan, injuredClipLen);
		}

		float elapsed = 0f;
		const float step = 1f / 60f;

		if (injured)
			SetPlayerInjuredVisualFrame(0);

		while (elapsed < totalDur)
		{
			await ToSignal(GetTree().CreateTimer(step), SceneTreeTimer.SignalName.Timeout);
			elapsed += step;

			if (!injured)
			{
				float uKnock = Mathf.Clamp(elapsed / PlayerFightKnockbackSeconds, 0f, 1f);
				_playerSprite.Position = from.Lerp(to, uKnock);
			}
			else if (elapsed < PlayerInjuredKnockbackDelaySeconds)
				_playerSprite.Position = from;
			else
			{
				float uKnock = Mathf.Clamp(
					(elapsed - PlayerInjuredKnockbackDelaySeconds) / PlayerFightKnockbackSeconds, 0f, 1f);
				_playerSprite.Position = from.Lerp(to, uKnock);
			}

			if (injured)
			{
				int fi = Mathf.Clamp((int)(elapsed / PlayerInjuredFrameHoldSeconds), 0,
					PlayerInjuredSpriteCount - 1);
				SetPlayerInjuredVisualFrame(fi);
			}
		}

		_playerCell = returnCell;
		SetPlayerIdleVisual();
		SnapPlayer();
	}

	/// <summary>草丛格子结算（走入触发或被动 207 邻格自动触发）；含 <see cref="EraseEvent"/>。</summary>
	async Task ApplyGrassTileEffectsAsync(Vector2I cell)
	{
		string ck = HexGridUtil.CellKey(cell);
		if (!_events.TryGetValue(ck, out Variant evVar))
			return;

		Godot.Collections.Dictionary ev = evVar.AsGodotDictionary();
		if (GetString(ev, "type") != "grass")
			return;

		bool grassHealHappen = pskillList.Contains(106) || GD.Randf() < 0.5f;
		if (grassHealHappen)
		{
			if (RunState.Instance.PlayerHp + 2 > RunState.Instance.PlayerHpMax && pskillList.Contains(102))
			{
				for (int i = 0; i < pconfigDict[102].AsInt32(); i++)
					SpawnGrassInRandomFog();
			}

			RunState.Instance.PlayerHp = Mathf.Min(RunState.Instance.PlayerHp + 1, RunState.Instance.PlayerHpMax);
			await ToastAsync("草丛", "生命值 +1。");
		}
		else
			await ToastAsync("草丛", "无事发生（50%占位）。");

		if (pskillList.Contains(101))
			grassskill = true;

		if (pskillList.Contains(104))
			grasscount++;

		if (pskillList.Contains(105))
		{
			RunState.Instance.PlayerEnergy = Mathf.Min(RunState.Instance.PlayerEnergy + pconfigDict[105].AsInt32(),
				RunState.Instance.PlayerEnergyMax);
			powerCheck();
		}

		if (pskillList.Contains(108))
		{
			int need = pconfigDict.ContainsKey(108) && pconfigDict[108].AsInt32() > 0
				? pconfigDict[108].AsInt32()
				: 6;
			_passive108GrassTriggers++;
			if (_passive108GrassTriggers >= need)
			{
				_passive108GrassTriggers = 0;
				RunState.Instance.PlayerMagic += 1;
				powerCheck();
			}
		}

		EraseEvent(cell);
	}

	async Task MaybeTriggerPassive207AdjacentGrassAfterMoveAsync(Vector2I landedCell)
	{
		if (!pskillList.Contains(207) || _terrain == null)
			return;

		foreach (Vector2I n in HexGridUtil.Neighbors(_terrain, landedCell))
		{
			string nk = HexGridUtil.CellKey(n);
			if (!_events.ContainsKey(nk))
				continue;
			Godot.Collections.Dictionary evNeighbor = _events[nk].AsGodotDictionary();
			if (GetString(evNeighbor, "type") != "grass")
				continue;
			await ApplyGrassTileEffectsAsync(n);
		}
	}

	async Task TryInteractAsync(Vector2I cell)



	{

		string ck = HexGridUtil.CellKey(cell);

		if (!_events.ContainsKey(ck))
			return;

		Godot.Collections.Dictionary ev = (Godot.Collections.Dictionary)_events[ck].AsGodotDictionary().Duplicate();

		string t = GetString(ev, "type");
		if (t is "monster_str" or "monster_mag")
			MonsterTable.SyncMonsterEventFightValue(ev);

		Vector2I approachOrigin = _playerCell;

		if (t != "altar")
		{
			bool walkSfx = t is not ("monster_str" or "monster_mag");
			await ApproachCellWithWalkAsync(approachOrigin, cell, walkSfx);
			_playerCell = cell;
			SetPlayerIdleVisual();
			SnapPlayer();
		}

		switch (t)
		{

			case "monster_str":
			case "monster_mag":
			{ 
				bool hadStrBuff = strBuff;
				bool hadMagicBuff = magicBuff;
				int strBonus = hadStrBuff ? _strBuffFightBonusAmt : 0;
				int magBonus = hadMagicBuff ? _magBuffFightBonusAmt : 0;
				if (strBonus != 0)
					RunState.Instance.PlayerStr += strBonus;
				if (magBonus != 0)
					RunState.Instance.PlayerMagic += magBonus;

				await ResolveFightAsync(cell, ev, approachOrigin);

				if (strBonus != 0)
					RunState.Instance.PlayerStr -= strBonus;
				if (magBonus != 0)
					RunState.Instance.PlayerMagic -= magBonus;
				if (hadStrBuff)
				{
					strBuff = false;
					_strBuffFightBonusAmt = 0;
				}

				if (hadMagicBuff)
				{
					magicBuff = false;
					_magBuffFightBonusAmt = 0;
				}
			}

				RunState.Instance.ClampHp();

				// 与 TryMoveAsync 一致：移动落位后吸收邻格迷雾。走入怪物格并战胜后仍站在该格，不经 TryMoveAsync，须在此补吸收。
				if (HexGridUtil.IsSameCell(_playerCell, cell))
				{
					await EnergyAbsorbAsync();
					await ToSignal(GetTree().CreateTimer(0.06f), SceneTreeTimer.SignalName.Timeout);
				}

				_spentBasic = true;

				RefreshHud();

				await CheckFailWinAsync();

				await FinishIfNeededAsync();

				return;

			case "treasure":


				EraseEvent(cell);
				cardchoose = 0;
				if (GetNodeOrNull<Button>("UICanvas/HUD/SkillChoose/Sure") is { } surePickStart)
					surePickStart.Disabled = true;
				GetNode<CanvasItem>("UICanvas/HUD/SkillChoose").Visible = true;
				var result = card_random(skillList);
				cardnum = result;
				for (int i = 0; i < 3; i++)
				{
					GetNode<Control>("UICanvas/HUD/SkillChoose/Card" + (i + 1) + "/Cost").Visible = false;
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
								ApplySkillPickDescribe(GetNode<Label>("UICanvas/HUD/SkillChoose/Card" + (i + 1) + "/SkillDescribe"), pskilldict["describe"].AsString());
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
								ApplySkillPickDescribe(GetNode<Label>("UICanvas/HUD/SkillChoose/Card" + (i + 1) + "/SkillDescribe"), askilldict["describe"].AsString());
								GetNode<Control>("UICanvas/HUD/SkillChoose/Card" + (i + 1) + "/Cost").Visible = true;
								GetNode<Label>("UICanvas/HUD/SkillChoose/Card" + (i + 1) + "/Cost/CostValue").Text = askilldict["power"].AsInt32().ToString();
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
							for(int i = 0; i < pconfigDict[102].AsInt32(); i++)
							{
								SpawnGrassInRandomFog();
							}
						}

						RunState.Instance.PlayerHp = Mathf.Min(RunState.Instance.PlayerHp + 2, RunState.Instance.PlayerHpMax);

						break;

				}

				ev["altar_used"] = true;

				_events[ck] = ev;
				RefreshBossEventIcon(ck);

				GameSfx.PlayWalk();

				break;


			case "grass":
				await ApplyGrassTileEffectsAsync(cell);
				break;


			case "corpse":
				await ApplyCorpseEventEffectsAsync();
				EraseEvent(cell);
				if (pskillList.Contains(103))
					_corpseExtraAction = true;
				break;


			case "ruins":
				int rr = (int)(GD.Randi() % 3);
				switch (rr)
				{
					case 0:
						RunState.Instance.PlayerEnergy = Mathf.Min(RunState.Instance.PlayerEnergy + 2, RunState.Instance.PlayerEnergyMax);
						powerCheck();
						await ToastAsync("废墟", "占位：能量 +2。");
						break;

					case 1:
						if (RunState.Instance.PlayerHp + 2 > RunState.Instance.PlayerHpMax && pskillList.Contains(102))
						{
							for (int i = 0; i < pconfigDict[102].AsInt32(); i++)
							{
								SpawnGrassInRandomFog();
							}
						}
						RunState.Instance.PlayerHp = Mathf.Min(RunState.Instance.PlayerHp + 1, RunState.Instance.PlayerHpMax);
						await ToastAsync("废墟", "占位：生命 +1。");
						break;

					default:
						RunState.Instance.PlayerHp -= 1;
						await PlayPlayerInjuredVisualSequenceIfAvailableAsync();
						await ToastAsync("废墟", "占位：不幸，生命 -1。");
						break;

				}

				EraseEvent(cell);
				break;

			case CampaignPortalEventTypeName:
				EraseEvent(cell);
				_campaignVictoryPickupPhase = false;
				string? nextMain = LevelCatalog.ResolveNextMainCampaignLevelPath(_loadedLevelPath);
				if (!string.IsNullOrEmpty(nextMain))
				{
					RunState.Instance.StoreCampaignSkillSnapshot(skillList, pskillList, askillList);
					RunState.Instance.PendingLevelPath = nextMain;
					await ToastAsync("传送门", "前往下一关。");
					GetTree().ChangeSceneToFile(GameplayScenePath);
				}
				else if (LevelCatalog.IsTerminalMainCampaignLevel(_loadedLevelPath))
				{
					RunState.Instance.PrepareReturnToMainMenu();
					GetTree().ChangeSceneToFile(VictoryScreenScene);
				}
				else
				{
					RunState.Instance.PrepareReturnToMainMenu();
					await ToastAsync("闯关", "没有可用的下一关。请在关卡编辑器为各关配置「闯关序号」。");
					GetTree().ChangeSceneToFile(MainMenuScene);
				}

				return;

			default:


				await ToastAsync("未知事件", t);



				break;


		}




		RunState.Instance.ClampHp();

		// 与 TryMoveAsync / 战斗胜利落位一致：走到事件格后吸收邻格迷雾；祭坛未改玩家格时仍吸收当前格邻雾。
		if (t is not ("monster_str" or "monster_mag") && t != CampaignPortalEventTypeName)
		{
			if (t == "altar" || HexGridUtil.IsSameCell(_playerCell, cell))
			{
				await EnergyAbsorbAsync();
				await ToSignal(GetTree().CreateTimer(0.06f), SceneTreeTimer.SignalName.Timeout);
			}
		}

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

			if (skillId == 206)
				RecalculatePlayerEnergyMaxFromPassives();
			RefreshHud();
			powerCheck();

		}
		else
		{
			skillList.Remove(skillId);
			askillList.Add(skillId);
			var aname = "SingleSkill" + (askillList.Count);
			foreach (var item in aarray)
			{
				var askilldict = item.AsGodotDictionary();
				int id = askilldict["ID"].AsInt32();

				if (id == skillId)
				{
					string targetAddress = askilldict["address"].AsString();
					string slotRoot = "UICanvas/HUD/ActiveSkillSlot/SkillArea/" + aname;
					GetNode<TextureRect>(slotRoot + ActiveSkillIconTexRel).Texture = GD.Load<Texture2D>(targetAddress);
					if (GetNodeOrNull<Button>(slotRoot + "/SkillIcon") is { } skBtn)
						skBtn.Icon = null;
					GetNode<TextureRect>(slotRoot + "/SkillHi").Visible = true;
					GetNode<TextureRect>(slotRoot + "/SkillCdCover").Visible = false;
					break;
				}
			}

			SetActiveSkillCostUi(askillList.Count, true, skillId);
			powerCheck();
		}

	}



	static string GetString(Godot.Collections.Dictionary d, string key, string def = "")


	{


		return d.ContainsKey(key) ? d[key].AsString() : def;



	}

	/// <summary>与走入尸体格相同的效果（被动 201/202/204 与 50% ±生命）；不含 <see cref="EraseEvent"/>。</summary>
	async Task ApplyCorpseEventEffectsAsync()
	{
		if (pskillList.Contains(204))
		{
			corpseCount++;
			if (corpseCount >= pconfigDict[204].AsInt32())
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
							for (int i = 0; i < pconfigDict[102].AsInt32(); i++)
								SpawnGrassInRandomFog();
						}

						RunState.Instance.PlayerHp = Mathf.Min(RunState.Instance.PlayerHp + 2, RunState.Instance.PlayerHpMax);
						break;
				}

				corpseCount = 0;
			}
		}

		if (GD.Randf() < 0.5f)
		{
			if (RunState.Instance.PlayerHp + 2 > RunState.Instance.PlayerHpMax && pskillList.Contains(102))
			{
				for (int i = 0; i < pconfigDict[102].AsInt32(); i++)
					SpawnGrassInRandomFog();
			}

			RunState.Instance.PlayerHp = Mathf.Min(RunState.Instance.PlayerHp + 1, RunState.Instance.PlayerHpMax);
			if (pskillList.Contains(201))
			{
				int extraHeal = Mathf.Max(1, pconfigDict[201].AsInt32());
				RunState.Instance.PlayerHp = Mathf.Min(RunState.Instance.PlayerHp + extraHeal, RunState.Instance.PlayerHpMax);
			}

			await ToastAsync("尸体", "+1 生命（50%）。");
		}
		else
		{
			RunState.Instance.PlayerHp -= 1;
			if (pskillList.Contains(202))
				corpseHp = true;

			await PlayPlayerInjuredVisualSequenceIfAvailableAsync();
			await ToastAsync("尸体", "-1 生命（50%）。");
		}
	}



	async Task ResolveFightAsync(Vector2I cell, Godot.Collections.Dictionary ev, Vector2I lossReturnCell)
	{
		bool useMagic = GetString(ev, "type") == "monster_mag";
		int monsterMv = useMagic ? GetInt(ev, "value_mag", GetInt(ev, "value", 1)) : GetInt(ev, "value_str", GetInt(ev, "value", 1));
		int attr = useMagic ? RunState.Instance.PlayerMagic : RunState.Instance.PlayerStr;
		string label = useMagic ? "魔法" : "力量";
		string foe = GetString(ev, "name");
		if (string.IsNullOrEmpty(foe))
			foe = "怪物";
		string snippet = GetString(ev, "description");
		string extra = string.IsNullOrEmpty(snippet) ? "\n" : $"\n{snippet}\n";

		if (attr >= monsterMv)
		{
			GameSfx.PlayAttack();
			await ToastAsync($"{foe} · 战胜", $"{extra}{label}对决：你的{label} {attr} ≥ 怪物{label}战力 {monsterMv}。");
			if (corpseHp == true)
			{
				if (RunState.Instance.PlayerHp + pconfigDict[202].AsInt32() > RunState.Instance.PlayerHpMax && pskillList.Contains(102))
				{
					for (int i = 0; i < pconfigDict[102].AsInt32(); i++)
					{
						SpawnGrassInRandomFog();
					}
				}

				RunState.Instance.PlayerHp = Mathf.Min(RunState.Instance.PlayerHp + pconfigDict[202].AsInt32(), RunState.Instance.PlayerHpMax);
				corpseHp = false;
			}
			EraseEvent(cell);
			if (pskillList.Contains(203))
				await ApplyCorpseEventEffectsAsync();

			return;
		}

		GameSfx.PlayLose();
		await ToastAsync($"{foe} · 落败", $"{extra}{label}对决：你的{label} {attr} < 怪物{label}战力 {monsterMv}。");
		int loss = Mathf.Max(monsterMv - attr, 1);

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
		DestroyEventIconsForCellKey(icons, ck);

	}

	void ReplaceMonsterCellWithGrass(Vector2I cell)
	{
		string ck = HexGridUtil.CellKey(cell);
		if (!TryMonsterEventAtKey(ck, out _))
			return;

		ReleaseMonsterNeighborFogLocks(ck);
		Node2D icons = GetNode<Node2D>("World/EventIcons");
		DestroyEventIconsForCellKey(icons, ck);
		var grassEv = new Godot.Collections.Dictionary { ["type"] = "grass" };
		_events[ck] = grassEv;
		SpawnSingleEventIcon(cell, grassEv);
		RefreshEventIconsFogVisibility();
	}

	/// <summary>技能 11「听天由命」可落点：版图内、非障碍；不得为祭坛、战斗怪或闯关传送门（允许迷雾格；落脚后 <c>DispelFogBySkill(dst)</c> 驱散脚下雾）。</summary>
	bool IsSkill11TeleportDestination(Vector2I cell)
	{
		string ck = HexGridUtil.CellKey(cell);
		if (!_valid.ContainsKey(ck))
			return false;
		if (TerrainCellMarkedBlocked(ck))
			return false;
		if (!_events.ContainsKey(ck))
			return true;
		string t = GetString(_events[ck].AsGodotDictionary(), "type");
		if (t == CampaignPortalEventTypeName)
			return false;
		return t is not ("altar" or "monster_str" or "monster_mag");
	}

	Vector2I? PickRandomSkill11TeleportDestination()
	{
		var list = new List<Vector2I>();
		foreach (Variant vk in _valid.Keys)
		{
			Vector2I c = HexGridUtil.ParseKey(vk.AsString());
			if (IsSkill11TeleportDestination(c))
				list.Add(c);
		}

		if (list.Count == 0)
			return null;
		return list[(int)(GD.Randi() % (uint)list.Count)];
	}

	void PurgeAllMonsterEncounterEventsFromMap()
	{
		List<string> snapshot = [];
		foreach (Variant vk in _events.Keys)
			snapshot.Add(vk.AsString());

		foreach (string ck in snapshot)
		{
			if (!_events.ContainsKey(ck))
				continue;
			if (!TryMonsterEventAtKey(ck, out _))
				continue;
			EraseEvent(HexGridUtil.ParseKey(ck));
		}

		RefreshEventIconsFogVisibility();
	}





	async Task FinishIfNeededAsync()


	{

		if (grassskill)
		{
			_spentBasic = false;
		}
		grassskill = false;

		if (_corpseExtraAction)
		{
			_spentBasic = false;
		}
		_corpseExtraAction = false;

		if (pskillList.Contains(104))
		{
			if (grasscount >= pconfigDict[104].AsInt32()+1)
			{
				_spentBasic = false;
			}
			else if (grasscount == pconfigDict[104].AsInt32())
			{
				grasscount++;
			}
		}

		if (fastRun > 0)
		{
			_spentBasic = false;
			fastRun--;
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
		HashSet<string> keys = BossSkillPlanner.ResolveLockedCellKeys(_terrain, _valid, _blockState, _playerCell,
			_bossSkillTarget, _bossSkillArea, _bossTableId, out _);
		foreach (string k in keys)
			_bossLockedCellKeys.Add(k);
		RebuildBossWarningVisual();
		SyncScreenEdgeWarningWithBossMapPreview();
	}

	void SyncScreenEdgeWarningWithBossMapPreview() =>
		_hudUi?.SetBossMapSkillPreviewActive(_bossLockedCellKeys.Count > 0);

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
			if (ch is Node2D nd)
			{
				Godot.Collections.Dictionary ev = _events[ck].AsGodotDictionary();
				Node2D? overlay = GetNodeOrNull<Node2D>("World/MonsterBadgeOverlay");
				EventWorldIconFactory.RefreshIconFromEvent(nd, ev, overlay, CellKeyMeta, ck);
			}
		}
	}

	/// <summary>移除该格索引下所有事件图标节点（避免出现重复 Sprite 导致 EraseEvent 只删掉其一）。</summary>
	void DestroyEventIconsForCellKey(Node2D iconsRoot, string ck)
	{
		Node2D? badgeOverlay = GetNodeOrNull<Node2D>("World/MonsterBadgeOverlay");
		foreach (Node ch in iconsRoot.GetChildren())
		{
			if (!ch.HasMeta(CellKeyMeta) || ch.GetMeta(CellKeyMeta).AsString() != ck)
				continue;
			ch.QueueFree();
		}

		if (badgeOverlay != null)
		{
			foreach (Node ch in badgeOverlay.GetChildren())
			{
				if (!ch.HasMeta(CellKeyMeta) || ch.GetMeta(CellKeyMeta).AsString() != ck)
					continue;
				ch.QueueFree();
			}
		}
	}

	void TryReparentMonsterStatBadges(Node2D host, string cellKey)
	{
		Node2D? ov = GetNodeOrNull<Node2D>("World/MonsterBadgeOverlay");
		if (ov == null)
			return;
		if (host.GetNodeOrNull<Sprite2D>(EventWorldIconFactory.MonsterBodyNodeName) == null)
			return;
		EventWorldIconFactory.ReparentMonsterStatBadges(host, ov, CellKeyMeta, cellKey);
	}

	static Godot.Collections.Dictionary BuildMonsterEncounterDict(MonsterTable.Row row)
	{
		var d = new Godot.Collections.Dictionary { ["monster_id"] = row.Id };
		MonsterTable.EnrichMonsterEvent(d);
		return d;
	}

	void SpawnSingleEventIcon(Node2D iconsRoot, string cellKey, Godot.Collections.Dictionary ev)
	{
		DestroyEventIconsForCellKey(iconsRoot, cellKey);
		Vector2I cell = HexGridUtil.ParseKey(cellKey);

		var host = EventWorldIconFactory.BuildIconRoot(ev, HexEventMarker.EventIconSpriteScale);
		host.Name = $"Ev_{cell.X}_{cell.Y}";
		host.SetMeta(CellKeyMeta, cellKey);
		host.Position = _terrain!.MapToLocal(cell);
		iconsRoot.AddChild(host);
		TryReparentMonsterStatBadges(host, cellKey);
	}

	/// <summary>被动 205「尸横遍野」：BOSS 释放表驱动技能后，在当次预警范围内随机空事件格留下尸体。</summary>
	void SpawnPassive205CorpsesInBossScope(IReadOnlyList<string> bossSkillScopeCells)
	{
		if (_terrain == null || bossSkillScopeCells == null || bossSkillScopeCells.Count == 0 ||
			!pskillList.Contains(205))
			return;

		string pk = HexGridUtil.CellKey(_playerCell);
		var empties = new List<string>();

		foreach (string ck in bossSkillScopeCells)
		{
			if (!_valid.ContainsKey(ck))
				continue;
			if (TerrainCellMarkedBlocked(ck))
				continue;
			if (_events.ContainsKey(ck))
				continue;
			if (ck == pk)
				continue;
			empties.Add(ck);
		}

		if (empties.Count == 0)
			return;

		int desired = 1;
		if (pconfigDict.ContainsKey(205))
			desired = Mathf.Max(1, pconfigDict[205].AsInt32());

		Node2D iconsRoot = GetNode<Node2D>("World/EventIcons");
		foreach (string ck in ShuffledTakePrefix(empties, Mathf.Min(desired, empties.Count)))
		{
			var corpseEvent = new Godot.Collections.Dictionary
			{
				{ "type", "corpse" }
			};
			_events[ck] = corpseEvent;
			SpawnSingleEventIcon(iconsRoot, ck, corpseEvent);
		}
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

	/// <summary>在已通过本次技能套上迷雾的空格（无障碍、无其它事件）上随机放置战斗怪；不占玩家格。</summary>
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
			if (TerrainCellMarkedBlocked(ck))
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
			MonsterTable.Row? row = BossTable.TryGet(_bossTableId, out BossTable.Row? bb) && bb != null
				? MonsterTable.PickBossSummonMonsterRowFromBossSummonIds(bb.SummonMonsterIds)
				: null;
			row ??= MonsterTable.PickBossSummonMonsterRow();
			if (row == null)
				break;
			Godot.Collections.Dictionary ev = BuildMonsterEncounterDict(row);
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

		var passive205BossScopeCells = new List<string>(_bossLockedCellKeys);

		string tip = string.IsNullOrWhiteSpace(_bossSkillText)
			? "BOSS 释放了技能。"
			: _bossSkillText;
		GameSfx.PlayBossSkill();
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
				if (TerrainCellMarkedBlocked(ck))
					continue;
				Vector2I c = HexGridUtil.ParseKey(ck);
				_fogState[ck] = true;
				_fog.SetCell(c, true);
				OnMonsterCellBecameFogCovered(c);
			}

			_fogGoalTotal = Mathf.Max(_fogGoalTotal, CountTrue(_fogState));
			SpawnRandomBossAddsInFogCells(fogTargets, fm.MonsterSpawnCount);

			RefreshEventIconsFogVisibility();

			SpawnPassive205CorpsesInBossScope(passive205BossScopeCells);

			_bossLockedCellKeys.Clear();
			_bossWarn?.ClearAll();
			SyncScreenEdgeWarningWithBossMapPreview();
			_bossMeter = 0f;
			await MaybeFogDamageAsync();
		}
		else
		{

			int fx = ResolveBossEffectKind();

			foreach (string ck in _bossLockedCellKeys)
			{
				if (!_valid.ContainsKey(ck))
					continue;
				if (TerrainCellMarkedBlocked(ck))
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
								int fb = GetInt(ev, "value", 1);
								int vs = GetInt(ev, "value_str", fb) + 1;
								int vm = GetInt(ev, "value_mag", fb) + 1;
								ev["value_str"] = vs;
								ev["value_mag"] = vm;
								MonsterTable.SyncMonsterEventFightValue(ev);
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
							MonsterTable.SyncMonsterEventFightValue(ev);
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

			SpawnPassive205CorpsesInBossScope(passive205BossScopeCells);

			_bossLockedCellKeys.Clear();
			_bossWarn?.ClearAll();
			SyncScreenEdgeWarningWithBossMapPreview();
			_bossMeter = 0f;
			await MaybeFogDamageAsync();
		}
	}

	async Task BossTurnAsync()
	{
		if (_campaignVictoryPickupPhase)
		{
			RunState.Instance.ClampHp();
			_turn = Turn.Player;
			_spentBasic = false;
			RefreshHud();
			return;
		}

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
			GameSfx.PlayBossSkill();
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

		for (int i = 0; i < 6 && i < askillList.Count; i++)
		{
			if(GetNode<Label>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill" + (i + 1) + "/CdLabel").Visible)
			{
				int cdLeft = int.Parse(GetNode<Label>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill" + (i + 1) + "/CdLabel").Text);
				GetNode<Label>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill" + (i + 1) + "/CdLabel").Text = (cdLeft - 1).ToString();
				if (cdLeft - 1 == 0)
				{
					GetNode<Label>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill" + (i + 1) + "/CdLabel").Visible = false;	
					if(apowerDict.ContainsKey(askillList[i]) && RunState.Instance.PlayerEnergy >= apowerDict[askillList[i]].AsInt32())
					{
						GD.Print("能量回复cdcover=false");
						GetNode<TextureRect>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill" + (i + 1) + "/SkillCdCover").Visible = false;
						GetNode<TextureRect>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill" + (i + 1) + "/SkillHi").Visible = true;
					}
				}
			}
			
		}

		await MaybeTransitionToFailScreenForFogManaDeadlockAsync();


		if (_gameEnding)


			return;


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

		await PlayPlayerInjuredVisualSequenceIfAvailableAsync();

		await ToastAsync("迷雾缠身", "-1 HP（占位：吸收规则）");


	}





	async Task CheckFailWinAsync()


	{


		if (RunState.Instance.PlayerHp <= 0)


		{


			GoToFailScreenFromGameplay();


			return;


		}





		if (RemainingFog() <= 0)


		{
			if (!_campaignVictoryExitsSpawned)


			{


				_campaignVictoryExitsSpawned = true;


				if (LevelCatalog.IsTerminalMainCampaignLevel(_loadedLevelPath))

				{

					PurgeAllMonsterEncounterEventsFromMap();

					ClearBossForCampaignVictory();

					RunState.Instance.PrepareReturnToMainMenu();

					GetTree().ChangeSceneToFile(VictoryScreenScene);

					return;

				}


				await SpawnVictoryChestAndPortalAsync(_playerCell);


				PurgeAllMonsterEncounterEventsFromMap();

				ClearBossForCampaignVictory();


				_campaignVictoryPickupPhase = true;


				_spentBasic = false;



				return;



			}






			return;


		}




	}





	void RefreshHud()


	{


		if (_hudUi == null || RunState.Instance == null)


			return;



		int hpNow = RunState.Instance.PlayerHp;


		if (!RunState.Instance.DebugModeVerboseToasts &&


			_lastHpForMapFloatTip is { } prevHp &&


			prevHp != hpNow)


		{


			int d = hpNow - prevHp;


			if (d != 0)


				SpawnMapHpFloatTip(Mathf.Abs(d), d > 0);


		}


		_lastHpForMapFloatTip = hpNow;



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

		if (_bossUsesTableSkill && BossTable.TryGet(_bossTableId, out BossTable.Row? bossRow) && bossRow != null)
			_hudUi.ApplyBossSkillIcon2FromTablePath(bossRow.SkillIcon);
		else
			_hudUi.ClearBossSkillIcon2ToDefault();



		float left = RemainingFog();




		float remaining01 =
			Mathf.Clamp(left / Mathf.Max(_fogGoalTotal, 1), 0f, 1f);



		_hudUi.SetFogRemainingRatio(remaining01);





	}



	static float HpFloatTipQuadEaseOut01(float t)
	{
		t = Mathf.Clamp(t, 0f, 1f);
		return 1f - (1f - t) * (1f - t);
	}

	void UnregisterHpFloatTip(Label lbl)
	{
		for (int i = _hpFloatTipRuns.Count - 1; i >= 0; i--)
		{
			if (_hpFloatTipRuns[i].Lbl == lbl)
				_hpFloatTipRuns.RemoveAt(i);
		}
	}

	void TryLayoutHpFloatTipAtPlayer(Label lbl, in Color baseModulate, float lift, float alphaMul)
	{
		if (_playerSprite == null || !GodotObject.IsInstanceValid(_playerSprite))
			return;

		lbl.ResetSize();
		Rect2 r = _playerSprite.GetRect();
		Transform2D xf = _playerSprite.GetGlobalTransformWithCanvas();
		Vector2 topCanvas = xf * new Vector2(0f, r.Position.Y);
		Vector2 anchor = topCanvas - new Vector2(lbl.Size.X * 0.5f, lbl.Size.Y + 10f);
		lbl.GlobalPosition = anchor + new Vector2(0f, lift);
		Color c = baseModulate;
		c.A = baseModulate.A * Mathf.Clamp(alphaMul, 0f, 1f);
		lbl.Modulate = c;
	}

	void UpdateHpFloatTipsFollowing()
	{
		if (_hpFloatTipRuns.Count == 0)
			return;

		ulong now = Time.GetTicksMsec();

		for (int i = _hpFloatTipRuns.Count - 1; i >= 0; i--)
		{
			HpFloatTipRun run = _hpFloatTipRuns[i];
			Label lbl = run.Lbl;
			if (lbl == null || !GodotObject.IsInstanceValid(lbl) || !lbl.IsInsideTree())
			{
				_hpFloatTipRuns.RemoveAt(i);
				continue;
			}

			double ageSec = (now - run.SpawnTickMs) * 0.001;
			if (!run.FadeStarted && ageSec >= HpFloatTipHoldSeconds)
			{
				run.FadeStarted = true;
				run.FadeStartTickMs = now;
			}

			float lift = 0f;
			float alphaMul = 1f;
			if (run.FadeStarted)
			{
				double fadeAge = (now - run.FadeStartTickMs) * 0.001;
				float fadeT = Mathf.Clamp((float)(fadeAge / HpFloatTipFadeSeconds), 0f, 1f);
				float ease = HpFloatTipQuadEaseOut01(fadeT);
				lift = -HpFloatTipFadeLiftPixels * ease;
				alphaMul = 1f - ease;
				if (fadeT >= 1f)
				{
					lbl.QueueFree();
					continue;
				}
			}

			TryLayoutHpFloatTipAtPlayer(lbl, run.BaseModulate, lift, alphaMul);
		}
	}

	void SpawnPlayerHeadFloatTip(string text, Color fontColor)
	{
		if (_playerSprite == null)
			return;

		var uiLayer = GetNodeOrNull<CanvasLayer>("UICanvas");
		if (uiLayer == null)
			return;

		var lbl = new Label
		{
			Text = text,
			HorizontalAlignment = HorizontalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			TopLevel = true,
		};

		if (_hpFloatFont != null)
			lbl.AddThemeFontOverride("font", _hpFloatFont);

		lbl.AddThemeFontSizeOverride("font_size", HpFloatTipFontSize);
		lbl.AddThemeColorOverride("font_color", fontColor);
		lbl.AddThemeColorOverride("font_outline_color", Colors.Black);
		lbl.AddThemeConstantOverride("outline_size", 3);
		lbl.ZAsRelative = false;
		lbl.ZIndex = 200;

		uiLayer.AddChild(lbl);

		ulong tick = Time.GetTicksMsec();
		var run = new HpFloatTipRun
		{
			Lbl = lbl,
			SpawnTickMs = tick,
			BaseModulate = lbl.Modulate,
		};
		_hpFloatTipRuns.Add(run);
		lbl.TreeExited += () => UnregisterHpFloatTip(lbl);

		Callable.From(() =>
		{
			if (!GodotObject.IsInstanceValid(lbl) || !GodotObject.IsInstanceValid(_playerSprite))
				return;

			TryLayoutHpFloatTipAtPlayer(lbl, run.BaseModulate, 0f, 1f);
		}).CallDeferred();
	}

	void SpawnMapHpFloatTip(int magnitude, bool heal)
	{
		string body = heal ? $"生命值+{magnitude}" : $"生命值-{magnitude}";
		SpawnPlayerHeadFloatTip(body, heal ? HpFloatHealColor : HpFloatDamageColor);
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
		return result;
	}

	public void sure_button_pressed()
	{
		var panel = GetNodeOrNull<CanvasItem>("UICanvas/HUD/SkillChoose");
		if (panel == null || !panel.Visible)
			return;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Sure/SureSelect").Visible = true;
	}


	public async void sure_button_pressed_close()
	{
		var panel = GetNodeOrNull<CanvasItem>("UICanvas/HUD/SkillChoose");
		if (panel == null || !panel.Visible)
			return;
		// cardchoose==0（未点选任一卡牌）或与 cardnum 不同步时会 cardnum[-1] 崩；空格/手柄易在未选卡时触发「确定」。
		if (cardchoose < 1 || cardchoose > cardnum.Count)
			return;
		if (cardnum.Count == 0)
			return;

		await UseSkillAsync(cardnum[cardchoose - 1]);
		if (cardnum[cardchoose - 1] < 100)
		{
			if (askillList.Count >= 6)
			{
				for(int i = 0; i < aarray.Count; i++)
				{
					if(skillList.Contains(i+1))
					{
						GD.Print("删除"+(i+1));
						skillList.Remove(i + 1);
					}
				}
			}
		}
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Sure/SureSelect").Visible = false;
		if (GetNodeOrNull<Button>("UICanvas/HUD/SkillChoose/Sure") is { } sureBtnEnd)
			sureBtnEnd.Disabled = true;
		cardchoose = 0;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card1/CardSelect").Visible = false;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card2/CardSelect").Visible = false;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card3/CardSelect").Visible = false;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose").Visible = false;
	}

	public void click_card1()
	{
		cardchoose = 1;
		if (GetNodeOrNull<Button>("UICanvas/HUD/SkillChoose/Sure") is { } sure1)
			sure1.Disabled = false;
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
		if (GetNodeOrNull<Button>("UICanvas/HUD/SkillChoose/Sure") is { } sure2)
			sure2.Disabled = false;
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
		if (GetNodeOrNull<Button>("UICanvas/HUD/SkillChoose/Sure") is { } sure3)
			sure3.Disabled = false;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card3/CardGlow").Visible = false;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card3/CardSelect").Visible = true;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card1/CardGlow").Visible = true;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card1/CardSelect").Visible = false;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card2/CardGlow").Visible = true;
		GetNode<CanvasItem>("UICanvas/HUD/SkillChoose/Card2/CardSelect").Visible = false;

	}

	/// <summary>主动槽是否可用：该槽有已装备技能、非冷却中、当前法力不低于消耗。</summary>
	bool CanUseActiveSkillSlot(int slot1To6)
	{
		if (slot1To6 < 1 || slot1To6 > 6 || slot1To6 > askillList.Count)
			return false;
		string root = "UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill" + slot1To6;
		if (GetNodeOrNull<Label>(root + "/CdLabel") is { Visible: true })
			return false;
		int sid = askillList[slot1To6 - 1];
		int cost = apowerDict.ContainsKey(sid) ? apowerDict[sid].AsInt32() : 999_999;
		return RunState.Instance.PlayerEnergy >= cost;
	}

	/// <summary>场景仍为 SingleSkill1/SkillIcon 连接 button_down；主动技能管线已注释。</summary>
	public void powerCheck()
	{
		for (int i = 0; i < askillList.Count; i++)
		{
			if(!GetNode<Label>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill" + (i + 1) + "/CdLabel").Visible)
			{
				int cost = apowerDict.ContainsKey(askillList[i]) ? apowerDict[askillList[i]].AsInt32() : 999_999;
				if(RunState.Instance.PlayerEnergy < cost)
				{
					GetNode<TextureRect>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill" + (i + 1) + "/SkillCdCover").Visible = true;
					GetNode<TextureRect>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill" + (i + 1) + "/SkillHi").Visible = false;
				}
				else
				{
					GetNode<TextureRect>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill" + (i + 1) + "/SkillCdCover").Visible = false;
					GetNode<TextureRect>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill" + (i + 1) + "/SkillHi").Visible = true;
				}
			}
		}
	}

	bool TryGetChosenActiveSkillId(out int skillId)
	{
		skillId = 0;
		if (skillchoose < 1 || skillchoose > askillList.Count)
			return false;
		skillId = askillList[skillchoose - 1];
		return true;
	}

	void DeductEnergyAfterChosenActiveSkill()
	{
		if (!TryGetChosenActiveSkillId(out int sid) || !apowerDict.ContainsKey(sid))
			return;
		int cost = apowerDict[sid].AsInt32();
		RunState.Instance.PlayerEnergy = Mathf.Max(RunState.Instance.PlayerEnergy - cost, 0);
		RefreshHud();
		powerCheck();
	}

	/// <summary>主动技能结算后：清雾可能直接通关，与交互/移动路径一致须跑胜利判定。</summary>
	async Task AfterChosenActiveSkillResolvedAsync()
	{
		GameSfx.PlaySkill();
		RunState.Instance.ClampHp();
		RefreshHud();
		await CheckFailWinAsync();
		if (_gameEnding)
			return;
		await MaybeTransitionToFailScreenForFogManaDeadlockAsync();
		if (_gameEnding)
			return;
		await FinishIfNeededAsync();
	}

	/// <summary>异步 void 技能按钮入口：耗能必须与当前 <see cref="skillchoose"/> 槽位对应 <see cref="askillList"/> 技能一致。</summary>
	async void click_active1()
	{
		if (!CanUseActiveSkillSlot(1))
			return;
		skillchoose = 1;
		GetNode<TextureRect>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill1/SkillCdCover").Visible = true;
		GetNode<TextureRect>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill1/SkillHi").Visible = false;
		await useActive();

		if (ischose)
		{
			GetNode<Label>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill1/CdLabel").Visible = true;
			if (TryGetChosenActiveSkillId(out int sid) && acdDict.ContainsKey(sid))
				GetNode<Label>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill1/CdLabel").Text = acdDict[sid].ToString();
			DeductEnergyAfterChosenActiveSkill();
			ischose = false;
			await AfterChosenActiveSkillResolvedAsync();
		}
		else
		{
			GD.Print("ischose为false,cdcover=false");
			GetNode<CanvasItem>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill1/SkillCdCover").Visible = false;
			GetNode<CanvasItem>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill1/SkillHi").Visible = true;
		}
	}

	async void click_active2()
	{
		if (!CanUseActiveSkillSlot(2))
			return;
		skillchoose = 2;
		GetNode<TextureRect>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill2/SkillCdCover").Visible = true;
		GetNode<TextureRect>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill2/SkillHi").Visible = false;
		await useActive();

		if (ischose)
		{
			GetNode<Label>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill2/CdLabel").Visible = true;
			if (TryGetChosenActiveSkillId(out int sid) && acdDict.ContainsKey(sid))
				GetNode<Label>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill2/CdLabel").Text = acdDict[sid].ToString();
			DeductEnergyAfterChosenActiveSkill();
			ischose = false;
			await AfterChosenActiveSkillResolvedAsync();
		}
		else
		{
			GetNode<CanvasItem>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill2/SkillCdCover").Visible = false;
			GetNode<CanvasItem>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill2/SkillHi").Visible = true;
		}
	}

	async void click_active3()
	{
		if (!CanUseActiveSkillSlot(3))
			return;
		skillchoose = 3;
		GetNode<TextureRect>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill3/SkillCdCover").Visible = true;
		GetNode<TextureRect>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill3/SkillHi").Visible = false;
		await useActive();

		if (ischose)
		{
			GetNode<Label>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill3/CdLabel").Visible = true;
			if (TryGetChosenActiveSkillId(out int sid) && acdDict.ContainsKey(sid))
				GetNode<Label>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill3/CdLabel").Text = acdDict[sid].ToString();
			DeductEnergyAfterChosenActiveSkill();
			ischose = false;
			await AfterChosenActiveSkillResolvedAsync();
		}
		else
		{
			GetNode<CanvasItem>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill3/SkillCdCover").Visible = false;
			GetNode<CanvasItem>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill3/SkillHi").Visible = true;
		}
	}

	async void click_active4()
	{
		if (!CanUseActiveSkillSlot(4))
			return;
		skillchoose = 4;
		GetNode<TextureRect>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill4/SkillCdCover").Visible = true;
		GetNode<TextureRect>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill4/SkillHi").Visible = false;
		await useActive();

		if (ischose)
		{
			GetNode<Label>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill4/CdLabel").Visible = true;
			if (TryGetChosenActiveSkillId(out int sid) && acdDict.ContainsKey(sid))
				GetNode<Label>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill4/CdLabel").Text = acdDict[sid].ToString();
			DeductEnergyAfterChosenActiveSkill();
			ischose = false;
			await AfterChosenActiveSkillResolvedAsync();
		}
		else
		{
			GetNode<CanvasItem>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill4/SkillCdCover").Visible = false;
			GetNode<CanvasItem>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill4/SkillHi").Visible = true;
		}
	}

	async void click_active5()
	{
		if (!CanUseActiveSkillSlot(5))
			return;
		skillchoose = 5;
		GetNode<TextureRect>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill5/SkillCdCover").Visible = true;
		GetNode<TextureRect>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill5/SkillHi").Visible = false;
		await useActive();

		if (ischose)
		{
			GetNode<Label>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill5/CdLabel").Visible = true;
			if (TryGetChosenActiveSkillId(out int sid) && acdDict.ContainsKey(sid))
				GetNode<Label>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill5/CdLabel").Text = acdDict[sid].ToString();
			DeductEnergyAfterChosenActiveSkill();
			ischose = false;
			await AfterChosenActiveSkillResolvedAsync();
		}
		else
		{
			GetNode<CanvasItem>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill5/SkillCdCover").Visible = false;
			GetNode<CanvasItem>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill5/SkillHi").Visible = true;
		}
	}

	async void click_active6()
	{
		if (!CanUseActiveSkillSlot(6))
			return;
		skillchoose = 6;
		GetNode<TextureRect>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill6/SkillCdCover").Visible = true;
		GetNode<TextureRect>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill6/SkillHi").Visible = false;
		await useActive();

		if (ischose)
		{
			GetNode<Label>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill6/CdLabel").Visible = true;
			if (TryGetChosenActiveSkillId(out int sid) && acdDict.ContainsKey(sid))
				GetNode<Label>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill6/CdLabel").Text = acdDict[sid].ToString();
			DeductEnergyAfterChosenActiveSkill();
			ischose = false;
			await AfterChosenActiveSkillResolvedAsync();
		}
		else
		{
			GetNode<CanvasItem>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill6/SkillCdCover").Visible = false;
			GetNode<CanvasItem>("UICanvas/HUD/ActiveSkillSlot/SkillArea/SingleSkill6/SkillHi").Visible = true;
		}
	}

	async Task useActive()
	{
		int askill = askillList[skillchoose - 1];
		switch (askill)
		{
			case 1:
				Vector2I mouseCell1 = GetMouseHoverCell();

				Vector2I targetCell1 = await WaitForHighlightClick(null);
				if (targetCell1.X == -999 && targetCell1.Y == -999)
				{
					ischose = false;
					return;  // 取消技能，不继续执行
				}

				foreach (string cellKey in _highlightedCells)
					DispelFogBySkill(HexGridUtil.ParseKey(cellKey));
				ischose = true;
				_highlightedCells.Clear();
				return;
			case 2:
			case 9:
				Vector2I mouseCell2 = GetMouseHoverCell();

				Vector2I targetCell2 = await WaitForHighlightClick(null);
				if (targetCell2.X == -999 && targetCell2.Y == -999)
				{
					ischose = false;
					return;  // 取消技能，不继续执行
				}

				foreach (string cellKey in _highlightedCells)
					DispelFogBySkill(HexGridUtil.ParseKey(cellKey));
				ischose = true;
				_highlightedCells.Clear();
				return;

			case 3:
				SpawnGrassAtRandomVisibleCell();
				SpawnCorpseAtRandomVisibleCell();
				ischose = true;
				return;

			case 4:
			{
				int sid4 = askillList[skillchoose - 1];
				_strBuffFightBonusAmt = aconfigDict.ContainsKey(sid4) ? Mathf.Max(0, aconfigDict[sid4].AsInt32()) : 3;
				strBuff = true;
				ischose = true;
				return;
			}

			case 5:
			{
				int sid5 = askillList[skillchoose - 1];
				_magBuffFightBonusAmt = aconfigDict.ContainsKey(sid5) ? Mathf.Max(0, aconfigDict[sid5].AsInt32()) : 3;
				magicBuff = true;
				ischose = true;
				return;
			}

			case 6:
			{
				int sid6 = askillList[skillchoose - 1];
				fastRun = aconfigDict.ContainsKey(sid6) ? Mathf.Max(1, aconfigDict[sid6].AsInt32()) : 2;
				ischose = true;
				return;
			}

			case 7:
			{
				Vector2I targetCell7 = await WaitForHighlightClick(null);
				if (targetCell7.X == -999 && targetCell7.Y == -999)
				{
					ischose = false;
					return;  // 取消技能，不继续执行
				}

				_playerCell = targetCell7;
				SnapPlayer();
				string destKey = HexGridUtil.CellKey(targetCell7);
				if (_events.ContainsKey(destKey))
					await TryInteractAsync(targetCell7);
				ischose = true;
				_highlightedCells.Clear();
				return;
			}

			case 8:
				Vector2I mouseCell8 = GetMouseHoverCell();

				Vector2I targetCell8 = await WaitForHighlightClick(null);
				if (targetCell8.X == -999 && targetCell8.Y == -999)
				{
					ischose = false;
					return;  // 取消技能，不继续执行
				}
				foreach (string cellKey in _highlightedCells)
				{
					if (!_events.ContainsKey(cellKey))
						continue;
					var ev = (Godot.Collections.Dictionary)_events[cellKey].AsGodotDictionary().Duplicate();
					string type = GetString(ev, "type");
					if(type == "monster_str")
					{
						ev["type"] = "monster_mag";
					}
					else if(type == "monster_mag")
					{
						ev["type"] = "monster_str";
					}
					else
						continue;
					MonsterTable.SyncMonsterEventFightValue(ev);
					_events[cellKey] = ev;
				}

				foreach (string cellKey in new List<string>(_highlightedCells))
					RefreshBossEventIcon(cellKey);

				ischose = true;
				_highlightedCells.Clear();
				return;

			case 10:
			{
				Vector2I targetCell10 = await WaitForHighlightClick(null);
				if (targetCell10.X == -999 && targetCell10.Y == -999)
				{
					ischose = false;
					return;
				}

				string ck10 = HexGridUtil.CellKey(targetCell10);
				if (!_highlightedCells.Contains(ck10) || !TryMonsterEventAtKey(ck10, out _))
				{
					ischose = false;
					return;
				}

				ReplaceMonsterCellWithGrass(targetCell10);
				ischose = true;
				_highlightedCells.Clear();
				return;
			}

			case 11:
			{
				Vector2I? rnd = PickRandomSkill11TeleportDestination();
				if (rnd == null)
				{
					ischose = false;
					return;
				}

				Vector2I dst = rnd.Value;
				string destKey = HexGridUtil.CellKey(dst);

				_playerCell = dst;
				SetPlayerIdleVisual();
				SnapPlayer();
				if (_events.ContainsKey(destKey))
					await TryInteractAsync(dst);
				if (_terrain != null)
				{
					foreach (Vector2I n in HexGridUtil.Neighbors(_terrain, dst))
					{
						if (HexGridUtil.IsSameCell(n, dst))
							continue;
						DispelFogBySkill(n);
					}
				}

				// 驱散落脚格迷雾（含怪物吸收锁：DispelFogBySkill 内已优先拆锁）；TryInteract 等若临时清雾也可统一收尾。
				DispelFogBySkill(dst);

				ischose = true;
				return;
			}

		}

	}

	private void UpdateHighlightForCell(Vector2I centerCell)
	{
		HashSet<string> cellsToHighlight = new HashSet<string>();
		int askill = askillList[skillchoose - 1];
		switch (askill)
		{
			case 1:
				// 添加中心格子（边界检查）
				string centerKey1 = HexGridUtil.CellKey(centerCell);
				if (_valid.ContainsKey(centerKey1))
				{
					cellsToHighlight.Add(centerKey1);
				}

				// 添加周围6格（边界检查）
				foreach (Vector2I neighbor in HexGridUtil.Neighbors(_terrain!, centerCell))
				{
					string neighborKey = HexGridUtil.CellKey(neighbor);
					if (_valid.ContainsKey(neighborKey))
					{
						cellsToHighlight.Add(neighborKey);
					}
				}
				break;
			case 2:
			case 9:
			{
				string centerKey2 = HexGridUtil.CellKey(centerCell);
				int lineLen = aconfigDict.ContainsKey(askill) ? Mathf.Max(1, aconfigDict[askill].AsInt32()) : 1;
				if (_valid.ContainsKey(centerKey2))
				{
					foreach (Vector2I neighbor in HexGridUtil.Neighbors(_terrain!, _playerCell))
					{
						if (HexGridUtil.IsSameCell(neighbor, HexGridUtil.ParseKey(centerKey2)))
						{
							Vector2I direction = GetDirection(_playerCell, HexGridUtil.ParseKey(centerKey2));
							List<Vector2I> lineCells = GetLineCells(direction, lineLen);

							foreach (Vector2I cell in lineCells)
								cellsToHighlight.Add(HexGridUtil.CellKey(cell));
						}
					}
				}

				break;
			}
			case 7:
				string centerKey7 = HexGridUtil.CellKey(centerCell);
				HashSet<string> noFogCells7 = new HashSet<string>();

				foreach (Variant key in _fogState.Keys)
				{
					if (!_fogState[key].AsBool())
					{
						noFogCells7.Add(key.AsString());
					}
				}
				if (_valid.ContainsKey(centerKey7) && noFogCells7.Contains(centerKey7)
					&& IsSkill7TeleportDestination(HexGridUtil.ParseKey(centerKey7)))
				{
					cellsToHighlight.Add(centerKey7);
				}
				break;
			case 8:
			case 10:
				string centerKey8 = HexGridUtil.CellKey(centerCell);
				HashSet<string> noFogCells8 = new HashSet<string>();

				foreach (Variant key in _fogState.Keys)
				{
					if (!_fogState[key].AsBool())
					{
						noFogCells8.Add(key.AsString());
					}
				}
				if (_valid.ContainsKey(centerKey8) && noFogCells8.Contains(centerKey8))
				{
					if (_events.ContainsKey(centerKey8))
					{
						var ev = _events[centerKey8].AsGodotDictionary();
						string type = GetString(ev, "type");
						if (type == "monster_str" || type == "monster_mag")
						{
							cellsToHighlight.Add(centerKey8);
						}
					}
				}
				break;
		}

		// 更新高亮显示
		_highlightLayer?.RebuildFromKeys(cellsToHighlight);
		_highlightedCells = cellsToHighlight;
	}

	private Vector2I GetMouseHoverCell()
	{
		if (_terrain == null) return Vector2I.Zero;

		Vector2 mousePos = GetViewport().GetMousePosition();
		Vector2 worldPos = GetViewport().GetCanvasTransform().AffineInverse() * mousePos;
		Vector2 localPos = _terrain.ToLocal(worldPos);
		Vector2I cell = _terrain.LocalToMap(localPos);

		// 边界检查：如果格子无效，返回玩家自己的格子
		string cellKey = HexGridUtil.CellKey(cell);
		if (!_valid.ContainsKey(cellKey))
		{
			return _playerCell;
		}

		return cell;
	}

	public async Task<Vector2I> WaitForHighlightClick(HashSet<string>? cellKeys = null)
	{
		// 如果传入了固定高亮格子，就用固定的
		if (cellKeys != null)
		{
			_highlightedCells = cellKeys;
			_highlightLayer?.RebuildFromKeys(cellKeys);
		}

		_clickTcs = new TaskCompletionSource<Vector2I>();
		_isWaitingForHighlightClick = true;

		// 开始鼠标追踪（实时更新高亮）
		_lastHoverCell = GetMouseHoverCell();
		UpdateHighlightForCell(_lastHoverCell);

		try
		{
			Vector2I clickedCell = await _clickTcs.Task;
			return clickedCell;
		}
		catch (TaskCanceledException)
		{
			return new Vector2I(-999, -999);
		}
		finally
		{
			_isWaitingForHighlightClick = false;
			_highlightLayer?.ClearAll();
		}
	}

	public void OnCellClicked(Vector2I clickedCell)
	{
		// 如果正在等待高亮点击
		if (_isWaitingForHighlightClick)
		{
			string cellKey = HexGridUtil.CellKey(clickedCell);

			// 检查点击的格子是否在高亮列表中
			if (_highlightedCells.Contains(cellKey))
			{
				// 是高亮格子，继续执行
				_clickTcs?.SetResult(clickedCell);
			}
			return;
		}

		
	}

	private void CancelSkillWait()
	{
		// 先检查是否有等待中的任务
		if (_clickTcs != null && !_clickTcs.Task.IsCompleted)
		{
			_clickTcs.SetCanceled();  // 触发取消
		}

		// 清理状态
		_isWaitingForHighlightClick = false;

		// 清除高亮
		_highlightLayer?.ClearAll();
		_highlightedCells.Clear();

		// 重置技能选择变量
		ischose = false;

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

	private Vector2I GetDirection(Vector2I from, Vector2I to)
	{
		int dx = to.X - from.X;
		int dy = to.Y - from.Y;

		// 严格六边形六个方向（硬匹配，绝对不会丢方向）
		if (dx == 1 && dy == 0) return new Vector2I(1, 0);    // 右
		if (dx == 0 && dy == 1) return new Vector2I(0, 1);    // 右下
		if (dx == -1 && dy == 1) return new Vector2I(-1, 1);  // 左 ← 这个现在必触发
		if (dx == -1 && dy == 0) return new Vector2I(-1, 0);  // 左上
		if (dx == 0 && dy == -1) return new Vector2I(0, -1);  // 右上
		if (dx == 1 && dy == -1) return new Vector2I(1, -1); // 左下

		// 兜底：如果是邻居，强制返回正确方向（防止计算误差）
		var neighbors = HexGridUtil.Neighbors(_terrain, from);
		foreach (var n in neighbors)
		{
			if (n.X == to.X && n.Y == to.Y)
			{
				int ndx = n.X - from.X;
				int ndy = n.Y - from.Y;
				return new Vector2I(ndx, ndy);
			}
		}

		return Vector2I.Zero;
	}

	private List<Vector2I> GetLineCells(Vector2I direction, int length)
	{
		List<Vector2I> cells = new List<Vector2I>();

		for (int i = 1; i <= length; i++)
		{
			Vector2I cell = new Vector2I(
				_playerCell.X + direction.X * i,
				_playerCell.Y + direction.Y * i
			);

			string cellKey = HexGridUtil.CellKey(cell);
			if (_valid.ContainsKey(cellKey))
			{
				cells.Add(cell);
			}
			else
			{
				break;  // 超出边界就停止
			}
		}

		return cells;
	}

	public void AddCorpseAt(Vector2I cell)
	{
		string ck = HexGridUtil.CellKey(cell);
		if (!_valid.ContainsKey(ck)) return;
		if (_events.ContainsKey(ck)) return;

		Godot.Collections.Dictionary corpseEvent = new Godot.Collections.Dictionary
	{
		{ "type", "corpse" }
	};

		_events[ck] = corpseEvent;
		SpawnSingleEventIcon(cell, corpseEvent);
	}

	/// <summary>
	/// 在指定格子生成废墟
	/// </summary>
	public void AddRuinsAt(Vector2I cell)
	{
		string ck = HexGridUtil.CellKey(cell);
		if (!_valid.ContainsKey(ck)) return;
		if (_events.ContainsKey(ck)) return;

		Godot.Collections.Dictionary ruinsEvent = new Godot.Collections.Dictionary
	{
		{ "type", "ruins" }
	};

		_events[ck] = ruinsEvent;
		SpawnSingleEventIcon(cell, ruinsEvent);
	}

	private void SpawnGrassAtRandomVisibleCell()
	{
		List<Vector2I> visibleCells = GetAllVisibleCells();
		visibleCells.RemoveAll(cell => HexGridUtil.IsSameCell(cell, _playerCell));
		if (visibleCells.Count == 0) return;

		int randomIndex = (int)(GD.Randi() % (uint)visibleCells.Count);
		Vector2I targetCell = visibleCells[randomIndex];

		AddGrassAt(targetCell);
	}

	private List<Vector2I> GetAllVisibleCells()
	{
	List<Vector2I> visibleCells = new List<Vector2I>();
	
	foreach (Variant key in _valid.Keys)
	{
		string cellKey = key.AsString();
		if (!CellHasFog(cellKey))  // 没有迷雾 = 明亮
		{
			visibleCells.Add(HexGridUtil.ParseKey(cellKey));
		}
	}
	
	return visibleCells;
	}

	private void SpawnCorpseAtRandomVisibleCell()
	{
		List<Vector2I> visibleCells = GetAllVisibleCells();
		visibleCells.RemoveAll(cell => HexGridUtil.IsSameCell(cell, _playerCell));
		if (visibleCells.Count == 0) return;

		int randomIndex = (int)(GD.Randi() % (uint)visibleCells.Count);
		Vector2I targetCell = visibleCells[randomIndex];

		AddCorpseAt(targetCell);
	}

	public void SpawnSingleEventIcon(Vector2I cell, Godot.Collections.Dictionary eventData)
	{
		var icons = GetNode<Node2D>("World/EventIcons");
		string ck = HexGridUtil.CellKey(cell);
		SpawnSingleEventIcon(icons, ck, eventData);
		RefreshEventIconsFogVisibility();
	}

	private void SetupTooltipStyle()
	{
		if (_tooltip == null)
			return;

		Texture2D texture = GD.Load<Texture2D>("res://Art/UI/mainUI/WaveBar.png");

		if (texture == null)
		{
			GD.PrintErr("背景图加载失败：res://Art/UI/mainUI/WaveBar.png");
			return;
		}

		var styleBox = new StyleBoxTexture();
		styleBox.Texture = texture;

		// 九宫格边距（切割边框用）
		styleBox.TextureMarginLeft = 18;
		styleBox.TextureMarginTop = 18;
		styleBox.TextureMarginRight = 18;
		styleBox.TextureMarginBottom = 18;

		styleBox.AxisStretchHorizontal = 0; // 水平拉伸
		styleBox.AxisStretchVertical = 0;   // 垂直拉伸
		styleBox.DrawCenter = true;         // 显示中间（必须开！）


		// 内容内边距
		styleBox.ContentMarginLeft = 40;
		styleBox.ContentMarginTop = 15;
		styleBox.ContentMarginRight = 30;
		styleBox.ContentMarginBottom = 15;

		// 应用
		_tooltip.AddThemeStyleboxOverride("panel", styleBox);

		if (_tooltipLabel != null)
		{
			if (_hpFloatFont != null)
				_tooltipLabel.AddThemeFontOverride("font", _hpFloatFont);
			_tooltipLabel.AddThemeFontSizeOverride("font_size", SkillTooltipFontSize);
		}
	}

	private void OnSkillButtonMouseEnteredP1()
	{
		if (pskillList.Count < 1)
		{
			return;
		}
		foreach (var item in parray)
		{
			var pskilldict = item.AsGodotDictionary();
			if (pskilldict["ID"].AsInt32() == pskillList[0])
			{
				var skillname = pskilldict["name"].ToString();
				var skilldescribe = pskilldict["describe"].ToString();
				ShowTooltip(skillname+"\n"+skilldescribe);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredP2()
	{
		if(pskillList.Count < 2)
		{
			return;
		}
		foreach (var item in parray)
		{
			var pskilldict = item.AsGodotDictionary();
			if (pskilldict["ID"].AsInt32() == pskillList[1])
			{
				var skillname = pskilldict["name"].ToString();
				var skilldescribe = pskilldict["describe"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredP3()
	{
		if (pskillList.Count < 3)
		{
			return;
		}
		foreach (var item in parray)
		{
			var pskilldict = item.AsGodotDictionary();
			if (pskilldict["ID"].AsInt32() == pskillList[2])
			{
				var skillname = pskilldict["name"].ToString();
				var skilldescribe = pskilldict["describe"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredP4()
	{
		if (pskillList.Count < 4)
		{
			return;
		}
		foreach (var item in parray)
		{
			var pskilldict = item.AsGodotDictionary();
			if (pskilldict["ID"].AsInt32() == pskillList[3])
			{
				var skillname = pskilldict["name"].ToString();
				var skilldescribe = pskilldict["describe"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredP5()
	{
		if (pskillList.Count < 5)
		{
			return;
		}
		foreach (var item in parray)
		{
			var pskilldict = item.AsGodotDictionary();
			if (pskilldict["ID"].AsInt32() == pskillList[4])
			{
				var skillname = pskilldict["name"].ToString();
				var skilldescribe = pskilldict["describe"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredP6()
	{
		if (pskillList.Count < 6)
		{
			return;
		}
		foreach (var item in parray)
		{
			var pskilldict = item.AsGodotDictionary();
			if (pskilldict["ID"].AsInt32() == pskillList[5])
			{
				var skillname = pskilldict["name"].ToString();
				var skilldescribe = pskilldict["describe"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredP7()
	{
		if (pskillList.Count < 7)
		{
			return;
		}
		foreach (var item in parray)
		{
			var pskilldict = item.AsGodotDictionary();
			if (pskilldict["ID"].AsInt32() == pskillList[6])
			{
				var skillname = pskilldict["name"].ToString();
				var skilldescribe = pskilldict["describe"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredP8()
	{
		if (pskillList.Count < 8)
		{
			return;
		}
		foreach (var item in parray)
		{
			var pskilldict = item.AsGodotDictionary();
			if (pskilldict["ID"].AsInt32() == pskillList[7])
			{
				var skillname = pskilldict["name"].ToString();
				var skilldescribe = pskilldict["describe"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredP9()
	{
		if (pskillList.Count < 9)
		{
			return;
		}
		foreach (var item in parray)
		{
			var pskilldict = item.AsGodotDictionary();
			if (pskilldict["ID"].AsInt32() == pskillList[8])
			{
				var skillname = pskilldict["name"].ToString();
				var skilldescribe = pskilldict["describe"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredP10()
	{
		if (pskillList.Count < 10)
		{
			return;
		}
		foreach (var item in parray)
		{
			var pskilldict = item.AsGodotDictionary();
			if (pskilldict["ID"].AsInt32() == pskillList[9])
			{
				var skillname = pskilldict["name"].ToString();
				var skilldescribe = pskilldict["describe"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredP11()
	{
		if (pskillList.Count < 11)
		{
			return;
		}
		foreach (var item in parray)
		{
			var pskilldict = item.AsGodotDictionary();
			if (pskilldict["ID"].AsInt32() == pskillList[10])
			{
				var skillname = pskilldict["name"].ToString();
				var skilldescribe = pskilldict["describe"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredP12()
	{
		if (pskillList.Count < 12)
		{
			return;
		}
		foreach (var item in parray)
		{
			var pskilldict = item.AsGodotDictionary();
			if (pskilldict["ID"].AsInt32() == pskillList[11])
			{
				var skillname = pskilldict["name"].ToString();
				var skilldescribe = pskilldict["describe"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredP13()
	{
		if (pskillList.Count < 13)
		{
			return;
		}
		foreach (var item in parray)
		{
			var pskilldict = item.AsGodotDictionary();
			if (pskilldict["ID"].AsInt32() == pskillList[12])
			{
				var skillname = pskilldict["name"].ToString();
				var skilldescribe = pskilldict["describe"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredP14()
	{
		if (pskillList.Count < 14)
		{
			return;
		}
		foreach (var item in parray)
		{
			var pskilldict = item.AsGodotDictionary();
			if (pskilldict["ID"].AsInt32() == pskillList[13])
			{
				var skillname = pskilldict["name"].ToString();
				var skilldescribe = pskilldict["describe"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredP15()
	{
		if (pskillList.Count < 15)
		{
			return;
		}
		foreach (var item in parray)
		{
			var pskilldict = item.AsGodotDictionary();
			if (pskilldict["ID"].AsInt32() == pskillList[14])
			{
				var skillname = pskilldict["name"].ToString();
				var skilldescribe = pskilldict["describe"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredP16()
	{
		if (pskillList.Count < 16)
		{
			return;
		}
		foreach (var item in parray)
		{
			var pskilldict = item.AsGodotDictionary();
			if (pskilldict["ID"].AsInt32() == pskillList[15])
			{
				var skillname = pskilldict["name"].ToString();
				var skilldescribe = pskilldict["describe"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredA1()
	{
		if (askillList.Count < 1)
		{
			return;
		}
		foreach (var item in aarray)
		{
			var askilldict = item.AsGodotDictionary();
			if (askilldict["ID"].AsInt32() == askillList[0])
			{
				var skillname = askilldict["name"].ToString();
				var skilldescribe = askilldict["describe"].ToString();
				var skillcd = askilldict["cd"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe + "\ncd:" + skillcd);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredA2()
	{
		if (askillList.Count < 2)
		{
			return;
		}
		foreach (var item in aarray)
		{
			var askilldict = item.AsGodotDictionary();
			if (askilldict["ID"].AsInt32() == askillList[1])
			{
				var skillname = askilldict["name"].ToString();
				var skilldescribe = askilldict["describe"].ToString();
				var skillcd = askilldict["cd"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe + "\ncd:" + skillcd);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredA3()
	{
		if (askillList.Count < 3)
		{
			return;
		}
		foreach (var item in aarray)
		{
			var askilldict = item.AsGodotDictionary();
			if (askilldict["ID"].AsInt32() == askillList[2])
			{
				var skillname = askilldict["name"].ToString();
				var skilldescribe = askilldict["describe"].ToString();
				var skillcd = askilldict["cd"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe + "\ncd:" + skillcd);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredA4()
	{
		if (askillList.Count < 4)
		{
			return;
		}
		foreach (var item in aarray)
		{
			var askilldict = item.AsGodotDictionary();
			if (askilldict["ID"].AsInt32() == askillList[3])
			{
				var skillname = askilldict["name"].ToString();
				var skilldescribe = askilldict["describe"].ToString();
				var skillcd = askilldict["cd"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe + "\ncd:" + skillcd);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredA5()
	{
		if (askillList.Count < 5)
		{
			return;
		}
		foreach (var item in aarray)
		{
			var askilldict = item.AsGodotDictionary();
			if (askilldict["ID"].AsInt32() == askillList[4])
			{
				var skillname = askilldict["name"].ToString();
				var skilldescribe = askilldict["describe"].ToString();
				var skillcd = askilldict["cd"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe + "\ncd:" + skillcd);
				break;
			}
		}
	}

	private void OnSkillButtonMouseEnteredA6()
	{
		if (askillList.Count < 6)
		{
			return;
		}
		foreach (var item in aarray)
		{
			var askilldict = item.AsGodotDictionary();
			if (askilldict["ID"].AsInt32() == askillList[5])
			{
				var skillname = askilldict["name"].ToString();
				var skilldescribe = askilldict["describe"].ToString();
				var skillcd = askilldict["cd"].ToString();
				ShowTooltip(skillname + "\n" + skilldescribe + "\ncd:" + skillcd);
				break;
			}
		}
	}

	private void OnSkillButtonMouseExited()
	{
		HideTooltip();
	}

	private void OnBossSkillIconsSlotMouseEntered()
	{
		string text = !string.IsNullOrWhiteSpace(_bossSkillDetail)
			? _bossSkillDetail.Trim()
			: (!string.IsNullOrWhiteSpace(_bossSkillText) ? _bossSkillText.Trim() : "（无详情）");
		ShowTooltip(text);
	}

	private async void ShowTooltip(string text)
	{
		if (_tooltip == null || _tooltipLabel == null) return;

		_tooltipLabel.Text = text;
		_tooltipLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_tooltipLabel.CustomMinimumSize = new Vector2(200, 0);

		_tooltipLabel.ResetSize();

		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);

		Vector2 labelSize = _tooltipLabel.GetCombinedMinimumSize();
		if (labelSize.X < 50) labelSize.X = 200;

		// 设置 Panel 大小
		_tooltip.Size = labelSize + new Vector2(50, 50);

		// ✅ 关键：设置 Label 填满整个 Panel
		_tooltipLabel.Size = _tooltip.Size;
		_tooltipLabel.Position = Vector2.Zero;

		// 或者设置锚点
		_tooltipLabel.AnchorLeft = 0;
		_tooltipLabel.AnchorTop = 0;
		_tooltipLabel.AnchorRight = 1;
		_tooltipLabel.AnchorBottom = 1;
		_tooltipLabel.OffsetLeft = 15;
		_tooltipLabel.OffsetTop = 10;
		_tooltipLabel.OffsetRight = -15;
		_tooltipLabel.OffsetBottom = -10;

		// 文字居中
		_tooltipLabel.HorizontalAlignment = HorizontalAlignment.Center;
		_tooltipLabel.VerticalAlignment = VerticalAlignment.Center;

		// 位置设置
		Vector2 mousePos = GetViewport().GetMousePosition();
		_tooltip.Position = mousePos + new Vector2(20, 0);

		Vector2 screenSize = GetViewportRect().Size;
		if (_tooltip.Position.X + _tooltip.Size.X > screenSize.X)
			_tooltip.Position = new Vector2(mousePos.X - _tooltip.Size.X - 5, _tooltip.Position.Y);
		if (_tooltip.Position.Y + _tooltip.Size.Y > screenSize.Y)
			_tooltip.Position = new Vector2(_tooltip.Position.X, mousePos.Y - _tooltip.Size.Y - 5);

		_tooltip.Visible = true;
	}



	private void HideTooltip()
	{
		if (_tooltip != null)
			_tooltip.Visible = false;
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
