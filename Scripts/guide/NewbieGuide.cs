using Godot;

public partial class NewbieGuide : Node
{
	[Export] public Control? Skill1;
	[Export] public Control? Skill2;

	[Export] public Control? Boss1;
	[Export] public Control? Boss2;

	[Export] public GuideMask? GuideMask;
	[Export] public Label? GuideTipLabel;
	[Export] public TextureRect? GuideTipImg1;
	[Export] public TextureRect? GuideTipImg2;

	private int _curStep;
	private bool _guideEnd; // 结束标记，防止卡死

	private readonly string[] _stepTexts =
	{
		"左键点击高亮格子可以移动角色，驱散迷雾获得能量",
		"地图格上不同事件会产生不同效果",
		"使用技能消耗能量",
		"boss行动条满后释放技能",
		"驱散场上所有迷雾击败boss获得胜利"
	};

	public override void _Ready()
	{
		_curStep = 0;
		if (GuideTipLabel != null) GuideTipLabel.ZIndex = 200;
		if (GuideTipImg1 != null) GuideTipImg1.ZIndex = 200;
		if (GuideTipImg2 != null) GuideTipImg2.ZIndex = 200;
		NextStep();
	}

	public override void _Input(InputEvent evt)
	{
		// 引导结束后，不再响应点击
		if (_guideEnd) return;

		if (evt is InputEventMouseButton b && b.Pressed && b.ButtonIndex == MouseButton.Left)
			NextStep();
	}

	private void NextStep()
	{
		if (_guideEnd) return;

		_curStep++;

		if (_curStep > 5)
		{
			_guideEnd = true;
			HideAllUI();
			ResetAllZ();
			return;
		}

		if (GuideMask != null)
		{
			GuideMask.Visible = true;
			GuideMask.ZIndex = 50;
		}

		if (GuideTipLabel != null)
		{
			GuideTipLabel.Visible = true;
			GuideTipLabel.Text = _stepTexts[_curStep - 1];
		}

		GuideTipImg1?.Hide();
		GuideTipImg2?.Hide();
		ResetAllZ();

		switch (_curStep)
		{
			case 1:
				GuideTipImg1?.Show();
				break;
			case 2:
				GuideTipImg2?.Show();
				break;
			case 3:
				SetZ(Skill1, 100);
				SetZ(Skill2, 100);
				break;
			case 4:
				SetZ(Boss1, 100);
				SetZ(Boss2, 100);
				break;
			case 5:
				// 最后一步：只隐藏遮罩，不卡死
				if (GuideMask != null) GuideMask.Visible = false;
				break;
		}
	}

	private void HideAllUI()
	{
		if (GuideMask != null) GuideMask.Visible = false;
		if (GuideTipLabel != null) GuideTipLabel.Visible = false;
		if (GuideTipImg1 != null) GuideTipImg1.Visible = false;
		if (GuideTipImg2 != null) GuideTipImg2.Visible = false;
	}

	private void ResetAllZ()
	{
		SetZ(Skill1, 0);
		SetZ(Skill2, 0);
		SetZ(Boss1, 0);
		SetZ(Boss2, 0);
	}

	private void SetZ(Node2D? n, int z)
	{
		if (n != null) n.ZIndex = z;
	}

	private void SetZ(Control? c, int z)
	{
		if (c != null) c.ZIndex = z;
	}
}
