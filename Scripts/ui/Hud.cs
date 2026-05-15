using System.Threading.Tasks;
using Godot;

namespace Booom202604;

public partial class Hud : Control
{
	const int FillBottomToTop = 3;

	const int FillClockwise = 4;

	const int FillCounterClockwise = 5;

	TextureRect? _portrait;
	TextureProgressBar? _hpFill;
	TextureProgressBar? _energyFill;

	Label? _hpLabel;
	Label? _energyLabel;

	Label? _strCornerLabel;
	Label? _magCornerLabel;

	Label? _turnLabel;

	TextureRect? _bossActionBarButton;
	TextureProgressBar? _bossVerticalFill;
	TextureRect? _isWarningIcon;
	Label? _bossSubtitleLabel;

	TextureRect? _bossSkillIcon;
	TextureRect? _bossSkillIcon2;
	Texture2D? _defaultBossSkillIcon2Texture;
	Vector2 _bossSkillIcon2SlotSize;
	Vector2 _bossSkillIcon2SlotPosition;
	bool _bossSkillIcon2LayoutCached;

	CanvasItem? _screenEdgeWarning;
	Tween? _screenEdgeBreatheTween;
	bool _screenEdgeBreathingActive;

	TextureProgressBar? _mistRing;
	Label? _mistPctLabel;

	public override void _Ready()
	{
		_portrait = GetNodeOrNull<TextureRect>("%Portrait");
		_hpFill = GetNodeOrNull<TextureProgressBar>("%HpFill");
		_energyFill = GetNodeOrNull<TextureProgressBar>("%EnergyFill");
		_hpLabel = GetNodeOrNull<Label>("%HpLabel");
		_energyLabel = GetNodeOrNull<Label>("%EnergyLabel");
		_strCornerLabel = GetNodeOrNull<Label>("%StrLabel");
		_magCornerLabel = GetNodeOrNull<Label>("%MagLabel");
		_turnLabel = GetNodeOrNull<Label>("%TurnLabel");

		_bossActionBarButton = GetNodeOrNull<TextureRect>("%BossActionBarButton");
		_bossVerticalFill = GetNodeOrNull<TextureProgressBar>("%BossActionBarFill");
		_isWarningIcon = GetNodeOrNull<TextureRect>("%IsWarningIcon");
		_bossSubtitleLabel = GetNodeOrNull<Label>("%BossPhaseLabel");
		_bossSkillIcon = GetNodeOrNull<TextureRect>("%BossSkillIcon");
		_bossSkillIcon2 = GetNodeOrNull<TextureRect>("%BossSkillIcon2");

		_mistRing = GetNodeOrNull<TextureProgressBar>("%MistRingFill");
		_mistPctLabel = GetNodeOrNull<Label>("%MistPctLabel");

		_screenEdgeWarning = GetParent()?.GetNodeOrNull<CanvasItem>("ScreenEdgeWarning");

		// 迷雾环：0°=12 点，角度顺时针递增。7 点=210°，5 点=150°。
		// 满雾为「7→顺时针经 12→到 5」长弧 300°；雾减少时从 7 点侧沿顺时针收回至 5。
		// TextureProgressBar 从起点沿 fill 方向铺 ratio×弧长；用 FILL_COUNTER_CLOCKWISE、起点 5 点、300°，
		// 则 ratio 变小时弧从 7 点侧缩短（5 点端为弧起点固定侧）。见 Godot TextureProgressBar 径向填充。
		if (_mistRing != null)
		{
			_mistRing.FillMode = FillCounterClockwise;
			_mistRing.RadialInitialAngle = 150f;
			_mistRing.RadialFillDegrees = 300f;
		}

		ApplyBossMeterNativePixelSizes();
		Callable.From(DeferredBossSkillIconsSlotLayout).CallDeferred();
	}

	void DeferredBossSkillIconsSlotLayout()
	{
		EnsureBossSkillIcon2LayoutCache();
		EnforceBossSkillIconTextureSlot(_bossSkillIcon);
		EnforceBossSkillIconTextureSlot(_bossSkillIcon2);
	}

	void EnsureBossSkillIcon2LayoutCache()
	{
		if (_bossSkillIcon2LayoutCached || _bossSkillIcon2 == null)
			return;

		_defaultBossSkillIcon2Texture = _bossSkillIcon2.Texture as Texture2D;
		_bossSkillIcon2SlotPosition = _bossSkillIcon2.Position;
		// 槽位固定为场景 BossSkillIcon2 设计尺寸；勿用 KeepSize（会按 256 等贴图最小边撑开控件）。
		_bossSkillIcon2SlotSize = new Vector2(97f, 98f);

		_bossSkillIcon2LayoutCached = true;
	}

	/// <summary>
	/// BOSS 双技能槽位以 <c>BossSkillIcon2</c> 在场景中的矩形为准：贴图等比缩放进槽，尺寸与位置与 Icon2 一致。
	/// </summary>
	void EnforceBossSkillIconTextureSlot(TextureRect? slot)
	{
		if (slot == null || !_bossSkillIcon2LayoutCached)
			return;

		// IgnoreSize：控件可小于贴图；KeepAspectCentered：大图缩放进 97×98 槽内。
		slot.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
		slot.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
		slot.CustomMinimumSize = _bossSkillIcon2SlotSize;
		slot.Size = _bossSkillIcon2SlotSize;
		slot.Position = _bossSkillIcon2SlotPosition;
	}

	/// <summary>按表 <c>skill_icon</c> 切换第二个 BOSS 技能图标；槽位像素尺寸与场景默认一致。</summary>
	public void ApplyBossSkillIcon2FromTablePath(string? resPath)
	{
		EnsureBossSkillIcon2LayoutCache();
		if (_bossSkillIcon2 == null || !_bossSkillIcon2LayoutCached)
			return;

		if (string.IsNullOrWhiteSpace(resPath))
		{
			RestoreBossSkillIcon2DefaultTexture();
			return;
		}

		string p = resPath.Trim();
		if (!ResourceLoader.Exists(p))
		{
			GD.PushWarning($"[Hud] BOSS 技能图标资源不存在：{p}");
			RestoreBossSkillIcon2DefaultTexture();
			return;
		}

		Texture2D? tex = GD.Load<Texture2D>(p);
		if (tex == null)
		{
			RestoreBossSkillIcon2DefaultTexture();
			return;
		}

		_bossSkillIcon2.Texture = tex;
		EnforceBossSkillIconTextureSlot(_bossSkillIcon2);
		EnforceBossSkillIconTextureSlot(_bossSkillIcon);
	}

	public void ClearBossSkillIcon2ToDefault() =>
		RestoreBossSkillIcon2DefaultTexture();

	void RestoreBossSkillIcon2DefaultTexture()
	{
		EnsureBossSkillIcon2LayoutCache();
		if (_bossSkillIcon2 == null || !_bossSkillIcon2LayoutCached)
			return;

		_bossSkillIcon2.Texture = _defaultBossSkillIcon2Texture;
		EnforceBossSkillIconTextureSlot(_bossSkillIcon2);
		EnforceBossSkillIconTextureSlot(_bossSkillIcon);
	}

	/// <summary>BOSS 外框与填充条按贴图像素 1:1（与 UI 场景一致），不拉伸变形。</summary>
	void ApplyBossMeterNativePixelSizes()
	{
		if (_bossActionBarButton?.Texture != null)
		{
			Texture2D t = _bossActionBarButton.Texture;
			_bossActionBarButton.ExpandMode = TextureRect.ExpandModeEnum.KeepSize;
			_bossActionBarButton.StretchMode = TextureRect.StretchModeEnum.Scale;
			Vector2 s = new(t.GetWidth(), t.GetHeight());
			_bossActionBarButton.CustomMinimumSize = s;
			_bossActionBarButton.Size = s;
		}

		// Boss 竖条 BossActionBarFill：矩形以 gameplay.tscn 为准；勿强制为贴图像素宽高，否则会与槽错位并透出下层外框线。

		// 未填充区域默认透明，会透出下层 WaveBarBorder_dark 的内侧描边（易被看成黄/金线框）。
		// 用与槽内深色接近的纯色 under 铺满进度条矩形，避免透出。
		if (_bossVerticalFill != null)
		{
			using var underImg = Image.CreateEmpty(2, 2, false, Image.Format.Rgba8);
			underImg.Fill(new Color(0.11f, 0.09f, 0.08f, 1f));
			var underTex = ImageTexture.CreateFromImage(underImg);
			_bossVerticalFill.TextureUnder = underTex;
			_bossVerticalFill.TintUnder = Colors.White;
		}
	}

	/// <summary>与 UI 场景一致：预警图标宽 63、高 45；X 中心 138；Y 为「蓄力段:总段」分界——从 BossActionBarButton 底边向上 (蓄力/总)×条高，再垂直居中图标。</summary>
	void LayoutIsWarningIconByChargeRatio(float chargeMeterMax, float warnMeterMax)
	{
		if (_bossActionBarButton == null || _isWarningIcon == null)
			return;

		float denom = Mathf.Max(chargeMeterMax + warnMeterMax, 1e-6f);
		float cMx = Mathf.Max(chargeMeterMax, 0f);

		float btnTop = _bossActionBarButton.Position.Y;
		float btnH = Mathf.Max(_bossActionBarButton.Size.Y, 1f);
		float btnBottom = btnTop + btnH;
		float splitY = btnBottom - (cMx / denom) * btnH;

		const float IconW = 63f;
		const float IconH = 45f;
		const float UiCenterX = 138f;

		_isWarningIcon.ExpandMode = TextureRect.ExpandModeEnum.KeepSize;
		_isWarningIcon.StretchMode = TextureRect.StretchModeEnum.Scale;
		_isWarningIcon.CustomMinimumSize = new Vector2(IconW, IconH);
		_isWarningIcon.Size = new Vector2(IconW, IconH);
		_isWarningIcon.Position = new Vector2(UiCenterX - IconW * 0.5f, splitY - IconH * 0.5f);
		_isWarningIcon.Visible = true;
	}

	public void SetPortrait(Texture2D? tex)
	{
		if (_portrait != null)
			_portrait.Texture = tex;
	}

	public void SetPlayerStats(int hp, int hpMax, int energy, int energyMax, int pstrength, int pmagic)
	{
		float hMx = Mathf.Max(hpMax, 1);
		float eMx = Mathf.Max(energyMax, 1);

		if (_hpFill != null)
		{
			_hpFill.MaxValue = hMx;
			_hpFill.Value = Mathf.Clamp(hp, 0, hpMax);
		}

		if (_energyFill != null)
		{
			_energyFill.MaxValue = eMx;
			_energyFill.Value = Mathf.Clamp(energy, 0, energyMax);
		}

		if (_hpLabel != null)
			_hpLabel.Text = $"{hp}/{hpMax}";

		if (_energyLabel != null)
			_energyLabel.Text = $"{energy}/{energyMax}";

		if (_strCornerLabel != null)
			_strCornerLabel.Text = $"{pstrength}";

		if (_magCornerLabel != null)
			_magCornerLabel.Text = $"{pmagic}";
	}

	public void SetTurnText(string txt)
	{
		if (_turnLabel != null)
			_turnLabel.Text = txt;
	}

	public void SetBossHudTitle(string name)
	{
		if (_bossSubtitleLabel != null)
			_bossSubtitleLabel.Text = string.IsNullOrWhiteSpace(name) ? "BOSS" : name;
	}

	/// <summary>按资源切换 BOSS 行动条顶部双图标（场景默认已绑图时可不传）。</summary>
	public void SetBossSkillIcons(Texture2D? primary, Texture2D? secondary)
	{
		EnsureBossSkillIcon2LayoutCache();
		if (_bossSkillIcon != null && primary != null)
		{
			_bossSkillIcon.Texture = primary;
			EnforceBossSkillIconTextureSlot(_bossSkillIcon);
		}

		if (_bossSkillIcon2 != null && secondary != null)
		{
			_bossSkillIcon2.Texture = secondary;
			EnforceBossSkillIconTextureSlot(_bossSkillIcon2);
		}
	}

	/// <summary>兼容旧调用：仅更新第一个 BOSS 技能图标。</summary>
	public void SetBossSkillIcon(Texture2D? tex)
	{
		if (_bossSkillIcon == null)
			return;
		EnsureBossSkillIcon2LayoutCache();
		_bossSkillIcon.Texture = tex;
		EnforceBossSkillIconTextureSlot(_bossSkillIcon);
	}

	public void SetBossPhaseMeters(float chargeCur, float chargeMax, float warnCur, float warnMax)
	{
		float cMx = Mathf.Max(chargeMax, 1e-3f);
		float wMx = Mathf.Max(warnMax, 1e-3f);
		float denom = Mathf.Max(cMx + wMx, 1e-3f);
		float cur = Mathf.Clamp(chargeCur, 0f, cMx) + Mathf.Clamp(warnCur, 0f, wMx);

		if (_bossVerticalFill != null)
		{
			_bossVerticalFill.MinValue = 0f;
			_bossVerticalFill.MaxValue = denom;
			_bossVerticalFill.Value = cur;
			_bossVerticalFill.SetDeferred(TextureProgressBar.PropertyName.FillMode, FillBottomToTop);
		}

		LayoutIsWarningIconByChargeRatio(cMx, wMx);
	}

	/// <summary>与地图上 BOSS 预期技能范围（<see cref="BossWarningLayer"/>）同步：有锁定格即显示屏边预警呼吸，清空即隐藏。</summary>
	public void SetBossMapSkillPreviewActive(bool active) =>
		UpdateScreenEdgeWarningBreathing(active);

	void KillScreenEdgeBreatheTween()
	{
		if (_screenEdgeBreatheTween != null && GodotObject.IsInstanceValid(_screenEdgeBreatheTween))
			_screenEdgeBreatheTween.Kill();
		_screenEdgeBreatheTween = null;
	}

	void UpdateScreenEdgeWarningBreathing(bool inWarnPhase)
	{
		if (_screenEdgeWarning == null)
			return;

		if (!inWarnPhase)
		{
			KillScreenEdgeBreatheTween();
			_screenEdgeBreathingActive = false;
			_screenEdgeWarning.Visible = false;
			_screenEdgeWarning.Modulate = Colors.White;
			return;
		}

		_screenEdgeWarning.Visible = true;
		if (_screenEdgeBreathingActive)
			return;

		_screenEdgeBreathingActive = true;
		KillScreenEdgeBreatheTween();
		var tw = CreateTween();
		tw.SetLoops(0);
		tw.TweenProperty(_screenEdgeWarning, "modulate", new Color(1f, 1f, 1f, 0.6f), 1.5f);
		tw.TweenProperty(_screenEdgeWarning, "modulate", Colors.White, 1.5f);
		_screenEdgeBreatheTween = tw;
	}

	public void SetFogRemainingRatio(float remaining01)
	{
		float r = Mathf.Clamp(remaining01, 0f, 1f);

		if (_mistRing != null)
		{
			_mistRing.MinValue = 0f;
			_mistRing.MaxValue = 100f;
			_mistRing.Value = 100f * r;
		}

		if (_mistPctLabel != null)
			_mistPctLabel.Text = $"{Mathf.RoundToInt(100f * r)}%";
	}

	public async Task ToastAsync(string title, string body)
	{
		var dlg = new AcceptDialog { Title = title, DialogText = body };
		AddChild(dlg);

		void Close() => dlg.QueueFree();

		dlg.Confirmed += Close;
		dlg.Canceled += Close;
		dlg.CloseRequested += Close;

		dlg.PopupCentered(new Vector2I(460, 200));
		await ToSignal(dlg, Node.SignalName.TreeExited);
	}

	public async Task<int> ModalThreeChoiceAsync(string title, string optA, string optB, string optC)
	{
		var w = new Window
		{
			Title = title,
			Unresizable = true,
			Size = new Vector2I(560, 260),
			InitialPosition = Window.WindowInitialPosition.CenterPrimaryScreen,
			Disable3D = true,
		};

		AddChild(w);

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(LayoutPreset.FullRect);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_left", 14);
		margin.AddThemeConstantOverride("margin_right", 14);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		w.AddChild(margin);

		var vb = new VBoxContainer();
		margin.AddChild(vb);

		int pick = -1;

		var b1 = new Button { Text = optA };
		var b2 = new Button { Text = optB };
		var b3 = new Button { Text = optC };

		b1.Pressed += () => { pick = 0; w.QueueFree(); };
		b2.Pressed += () => { pick = 1; w.QueueFree(); };
		b3.Pressed += () => { pick = 2; w.QueueFree(); };

		vb.AddChild(b1);
		vb.AddChild(b2);
		vb.AddChild(b3);

		w.CloseRequested += () =>
		{
			if (pick < 0)
				pick = 0;
			w.QueueFree();
		};

		w.PopupCentered();
		await ToSignal(w, Node.SignalName.TreeExited);
		return Mathf.Clamp(pick, 0, 2);
	}
}
