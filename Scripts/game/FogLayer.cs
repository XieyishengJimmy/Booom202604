using System.Collections.Generic;
using Godot;

namespace Booom202604;

public partial class FogLayer : Node2D
{
	static readonly Texture2D FogTexture =
		ResourceLoader.Exists("res://Art/Map/1.png") ? GD.Load<Texture2D>("res://Art/Map/1.png")! : null!;

	static readonly Texture2D? LockTexture =
		ResourceLoader.Exists("res://Art/Icon/lock.png") ? GD.Load<Texture2D>("res://Art/Icon/lock.png") : null;

	TileMapLayer? _terrain;
	readonly Dictionary<string, Sprite2D> _sprites = [];
	readonly Dictionary<string, Sprite2D> _lockOverlays = [];

	public void Setup(TileMapLayer terrainLayer)
	{
		_terrain = terrainLayer;
	}

	public void Rebuild(Godot.Collections.Dictionary fogState)
	{
		ClearAll();

		foreach (Variant keyVar in fogState.Keys)
		{
			if (!fogState[keyVar].AsBool())
				continue;

			string ck = keyVar.AsString();
			Add(HexGridUtil.ParseKey(ck));
		}
	}

	public void SetCell(Vector2I cell, bool fogOn)
	{
		string ck = HexGridUtil.CellKey(cell);
		if (fogOn)
			Add(cell);
		else
			Remove(ck);
	}

	/// <summary>在仍为「有迷雾」的格子上叠加锁图标（吸收锁定提示）。</summary>
	public void SetAbsorptionLockedVisual(Vector2I cell, bool locked)
	{
		if (_terrain == null || LockTexture == null)
			return;

		string ck = HexGridUtil.CellKey(cell);
		if (locked)
		{
			if (!_sprites.ContainsKey(ck))
				return;
			if (_lockOverlays.ContainsKey(ck))
				return;

			var s = new Sprite2D
			{
				Texture = LockTexture,
				Centered = true,
				Scale = new Vector2(0.38f, 0.38f),
				Position = _terrain.MapToLocal(cell) + new Vector2(0f, -6f),
				ZIndex = 2,
			};
			AddChild(s);
			_lockOverlays[ck] = s;
		}
		else if (_lockOverlays.Remove(ck, out Sprite2D? lo) && lo != null)
			lo.QueueFree();
	}

	void Add(Vector2I cell)
	{
		if (_terrain == null || FogTexture == null)
			return;

		string ck = HexGridUtil.CellKey(cell);
		if (_sprites.ContainsKey(ck))
			return;

		var s = new Sprite2D
		{
			Texture = FogTexture,
			Centered = true,
			Modulate = new Color(0.12f, 0.14f, 0.2f, 0.88f),
			Scale = new Vector2(0.52f, 0.52f),
			Position = _terrain.MapToLocal(cell),
			ZIndex = 0,
		};

		AddChild(s);
		_sprites[ck] = s;
	}

	void Remove(string ck)
	{
		if (_lockOverlays.Remove(ck, out Sprite2D? lockSpr) && lockSpr != null)
			lockSpr.QueueFree();

		if (!_sprites.Remove(ck, out Sprite2D? s) || s == null)
			return;

		s.QueueFree();
	}

	void ClearAll()
	{
		foreach (KeyValuePair<string, Sprite2D> kv in _sprites)
			kv.Value.QueueFree();

		_sprites.Clear();

		foreach (KeyValuePair<string, Sprite2D> kv in _lockOverlays)
			kv.Value.QueueFree();

		_lockOverlays.Clear();
	}
}
