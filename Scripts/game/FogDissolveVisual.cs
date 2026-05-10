using System;
using Godot;

namespace Booom202604;

/// <summary>
/// 迷雾消散：按序播放 Cloud1～Cloud6，默认 10fps（每帧 0.1s），结束后自毁。
/// </summary>
public partial class FogDissolveVisual : Node2D
{
	public const float FrameSeconds = 0.1f;

	public event Action? Finished;

	Texture2D[] _frames = [];
	Vector2 _scale = Vector2.One;
	Color _modulate = Colors.White;

	int _frameIndex;
	Sprite2D? _sprite;
	Timer? _timer;

	public void Start(Texture2D[] frames, Vector2 scale, Color modulate)
	{
		_frames = frames;
		_scale = scale;
		_modulate = modulate;
	}

	public override void _Ready()
	{
		if (_frames.Length == 0)
		{
			Finished?.Invoke();
			QueueFree();
			return;
		}

		ZIndex = 0;

		_sprite = new Sprite2D
		{
			Texture = _frames[0],
			Centered = true,
			Scale = _scale,
			Modulate = _modulate,
		};
		AddChild(_sprite);

		_timer = new Timer
		{
			WaitTime = FrameSeconds,
			OneShot = false,
			Autostart = true,
		};
		AddChild(_timer);
		_timer.Timeout += OnFrameTick;
	}

	void OnFrameTick()
	{
		if (_sprite == null || _timer == null)
			return;

		_frameIndex++;

		if (_frameIndex >= _frames.Length)
		{
			_timer.Stop();
			Finished?.Invoke();
			QueueFree();
			return;
		}

		_sprite.Texture = _frames[_frameIndex];
	}
}
