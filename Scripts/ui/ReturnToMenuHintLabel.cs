using Godot;

namespace Booom202604;

/// <summary>
/// 结算页底部提示：白色字体的透明度按正弦缓变，整段起伏周期约 2 秒（峰谷间隔约 1 秒）。
/// </summary>
public partial class ReturnToMenuHintLabel : Label
{
	const float BreathePeriodSeconds = 2f;
	const float AlphaMin = 0.38f;
	const float AlphaMax = 1f;

	public override void _Process(double delta)
	{
		float t = (float)Time.GetTicksMsec() * 0.001f;
		float breathe = 0.5f + 0.5f * Mathf.Sin(t * (Mathf.Tau / BreathePeriodSeconds));
		float a = Mathf.Lerp(AlphaMin, AlphaMax, breathe);
		Modulate = new Color(1f, 1f, 1f, a);
	}
}
