using System.Threading.Tasks;
using Godot;

namespace Booom202604;

public partial class Hud : Control
{
	const int FillBottomToTop = 3;

	const int FillCounterClockwise = 5;

	TextureRect? _portrait;
	TextureProgressBar? _hpFill;
	TextureProgressBar? _energyFill;

	Label? _hpLabel;
	Label? _energyLabel;

	Label? _strCornerLabel;
	Label? _magCornerLabel;

	Label? _turnLabel;

	TextureRect? _bossTrack;
	TextureProgressBar? _bossVerticalFill;
	TextureRect? _warnDivider;
	CanvasItem? _isWarningCanvas;
	Label? _bossSubtitleLabel;

	TextureRect? _bossSkillIcon;

	TextureProgressBar? _mistRing;
	Label? _mistPctLabel;

	float _dividerXSnap = float.NaN;

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

		_bossTrack = GetNodeOrNull<TextureRect>("%BossSkillTrack");
		_bossVerticalFill = GetNodeOrNull<TextureProgressBar>("%BossActionBarFill");
		_warnDivider = GetNodeOrNull<TextureRect>("%BossWarnDividerMarker");
		_isWarningCanvas = GetNodeOrNull<CanvasItem>("%IsWarningIcon");
		_bossSubtitleLabel = GetNodeOrNull<Label>("%BossPhaseLabel");
		_bossSkillIcon = GetNodeOrNull<TextureRect>("%BossSkillIcon");

		_mistRing = GetNodeOrNull<TextureProgressBar>("%MistRingFill");
		_mistPctLabel = GetNodeOrNull<Label>("%MistPctLabel");
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

	/// <summary>占位：未来按 Boss 技能 ID / 资源配置切换 %BossSkillIcon。</summary>
	public void SetBossSkillIcon(Texture2D? tex)
	{
		if (_bossSkillIcon != null)
			_bossSkillIcon.Texture = tex;
	}

	public void SetBossPhaseMeters(float chargeCur, float chargeMax, float warnCur, float warnMax)
	{
		float cMx = Mathf.Max(chargeMax, 1e-3f);
		float wMx = Mathf.Max(warnMax, 1e-3f);
		float denom = Mathf.Max(cMx + wMx, 1e-3f);
		float cur = Mathf.Clamp(chargeCur, 0f, cMx) + Mathf.Clamp(warnCur, 0f, wMx);
		float pct = Mathf.Clamp(cur / denom, 0f, 1f);
		float splitFracUi = Mathf.Clamp(cMx / denom, 0f, 1f);

		if (_bossVerticalFill != null)
		{
			_bossVerticalFill.MinValue = 0f;
			_bossVerticalFill.MaxValue = 100f;
			_bossVerticalFill.Value = 100f * pct;
			_bossVerticalFill.SetDeferred(TextureProgressBar.PropertyName.FillMode, FillBottomToTop);
		}

		var wrapBar = _bossVerticalFill?.GetParent() as Control;
		if (wrapBar != null && _warnDivider != null)
		{
			if (float.IsNaN(_dividerXSnap))

				_dividerXSnap = _warnDivider.Position.X;

			float h = Mathf.Max(wrapBar.Size.Y, 1f);

			float yCenter = h * (1f - splitFracUi);

			_warnDivider.Position = new Vector2(_dividerXSnap, yCenter - _warnDivider.Size.Y * 0.5f);
		}

		if (_isWarningCanvas != null)
			_isWarningCanvas.Visible = warnCur > 1e-4f;
	}

	public void SetFogRemainingRatio(float remaining01)
	{
		float r = Mathf.Clamp(remaining01, 0f, 1f);

		if (_mistRing != null)
		{
			_mistRing.MinValue = 0f;
			_mistRing.MaxValue = 100f;
			_mistRing.Value = 100f * r;
			_mistRing.SetDeferred(TextureProgressBar.PropertyName.FillMode, FillCounterClockwise);
			_mistRing.SetDeferred(TextureProgressBar.PropertyName.RadialInitialAngle, Mathf.DegToRad(225f));
			_mistRing.SetDeferred(TextureProgressBar.PropertyName.RadialFillDegrees, Mathf.DegToRad(285f));
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

