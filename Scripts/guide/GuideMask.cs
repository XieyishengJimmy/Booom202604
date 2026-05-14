using Godot;

public partial class GuideMask : ColorRect
{
	public override void _Ready()
	{
		Size = GetViewport().GetVisibleRect().Size;
		Color = new Color(0, 0, 0, 0.8f);
	}
}
