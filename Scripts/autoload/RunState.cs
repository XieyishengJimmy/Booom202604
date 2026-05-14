using System.Collections.Generic;
using Godot;

namespace Booom202604;

public partial class RunState : Node
{
	public static RunState Instance { get; private set; } = null!;

	/// <summary>从编辑器按「运行」启动时为 true；独立导出的 exe 为 false。<br/>
	/// 勿用 <see cref="Engine.IsEditorHint"/> 区分：可玩窗口在子进程里该值常为 false。</summary>
	public static bool IsEditorPlaySession => OS.HasFeature("editor");

	/// <summary>导出包封面「开始游戏」应进入的关卡：主线 <c>campaign_order</c> 最小者（不含 ≥999 实训关）；无主线表时退回 starter。</summary>
	public static string ResolveShippedEntryLevelPath()
	{
		var main = LevelCatalog.EnumerateMainCampaignPathsOrdered();
		if (main.Count > 0)
			return main[0];
		return "res://levels/starter_level.json";
	}

	public int PlayerHpMax { get; set; } = 10;
	public int PlayerHp { get; set; } = 10;

	public int PlayerStr { get; set; } = 1;

	public int PlayerMagic { get; set; } = 1;

	public int PlayerEnergyMax { get; set; } = 10;
	public int PlayerEnergy { get; set; }

	public string PendingLevelPath { get; set; } = "";

	/// <summary>主菜单勾选「调试模式」。为 true 时显示能量吸收、草丛/废墟/战斗检定等吐司；false 仅保留关卡流程与操作阻断提示。</summary>
	public bool DebugModeVerboseToasts { get; set; } = false;

	readonly List<int> _carrySkillDeck = [];
	readonly List<int> _carryPassivesEquipped = [];
	readonly List<int> _carryActivesEquipped = [];
	bool _campaignSkillCarryPending;

	public override void _EnterTree()
	{
		base._EnterTree();
		Instance = this;
	}

	public override void _ExitTree()
	{
		if (Instance == this)
			Instance = null!;
		base._ExitTree();
	}

	public void ResetRunStats()
	{
		PlayerHp = PlayerHpMax;
		PlayerStr = 1;

		PlayerMagic = 1;
	}

	/// <summary>每进入一个关卡：<c>PlayerEnergy = 0</c>（不继承法力）；生命值与技能快照由闯关逻辑单独保留。</summary>
	public void PrepareLevelStart()
	{
		PlayerEnergy = 0;

	}

	/// <summary>主菜单/试玩/失败返回：重置角色局外属性并丢弃跨关卡技能快照。</summary>
	public void PrepareReturnToMainMenu()
	{
		PendingLevelPath = "";
		ResetRunStats();
		PlayerEnergy = 0;
		ClearCampaignSkillCarry();
	}

	public void StoreCampaignSkillSnapshot(IReadOnlyList<int> deck, IReadOnlyList<int> passivesEquipped,
		IReadOnlyList<int> activesEquipped)
	{
		_carrySkillDeck.Clear();
		foreach (int id in deck)
			_carrySkillDeck.Add(id);
		_carryPassivesEquipped.Clear();
		foreach (int id in passivesEquipped)
			_carryPassivesEquipped.Add(id);
		_carryActivesEquipped.Clear();
		foreach (int id in activesEquipped)
			_carryActivesEquipped.Add(id);
		_campaignSkillCarryPending = true;
	}

	void ClearCampaignSkillCarry()
	{
		_campaignSkillCarryPending = false;
		_carrySkillDeck.Clear();
		_carryPassivesEquipped.Clear();
		_carryActivesEquipped.Clear();
	}

	/// <summary>若为「传送门衔接的下一关」，将快照写入关卡内列表（并清空快照）。否则返回 false，由关卡用表格重新生成初始牌池。</summary>
	public bool TryConsumeCampaignSkillInto(List<int> skillDeckDst, List<int> passivesEquippedDst, List<int> activesEquippedDst)
	{
		skillDeckDst.Clear();
		passivesEquippedDst.Clear();
		activesEquippedDst.Clear();

		if (!_campaignSkillCarryPending)
			return false;

		skillDeckDst.AddRange(_carrySkillDeck);
		passivesEquippedDst.AddRange(_carryPassivesEquipped);
		activesEquippedDst.AddRange(_carryActivesEquipped);
		ClearCampaignSkillCarry();
		return true;
	}

	public void ClampHp()
	{
		PlayerHp = Mathf.Clamp(PlayerHp, 0, PlayerHpMax);
	}

}
