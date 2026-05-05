using Godot;

namespace Booom202604;

public partial class RunState : Node
{
	public static RunState Instance { get; private set; } = null!;

	public int PlayerHpMax { get; set; } = 10;
	public int PlayerHp { get; set; } = 10;

	public int PlayerStr { get; set; } = 2;

	public int PlayerMagic { get; set; } = 2;

	public int PlayerEnergyMax { get; set; } = 10;
	public int PlayerEnergy { get; set; }

	public string PendingLevelPath { get; set; } = "";

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
		PlayerStr = 2;

		PlayerMagic = 2;
	}

	public void PrepareLevelStart()
	{
		PlayerEnergy = 0;

	}

	public void ClampHp()
	{
		PlayerHp = Mathf.Clamp(PlayerHp, 0, PlayerHpMax);
	}

}
