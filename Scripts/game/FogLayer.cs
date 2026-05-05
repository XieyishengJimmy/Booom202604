using System.Collections.Generic;
using Godot;

namespace Booom202604;

public partial class FogLayer : Node2D
{
	TileMapLayer? _terrain;
	readonly Texture2D _fogTexture = GD.Load<Texture2D>("res://Art/Map/1.png")!;
	readonly Dictionary<string, Sprite2D> _sprites = [];

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

	void Add(Vector2I cell)
	{
		if (_terrain == null)
			return;

		string ck = HexGridUtil.CellKey(cell);
		if (_sprites.ContainsKey(ck))
			return;

		var s = new Sprite2D
		{
			Texture = _fogTexture,
			Centered = true,
			Modulate = new Color(0.12f, 0.14f, 0.2f, 0.88f),
			Scale = new Vector2(0.52f, 0.52f),
			Position = _terrain.MapToLocal(cell),
		};

		AddChild(s);
		_sprites[ck] = s;
	}

	void Remove(string ck)
	{
		if (!_sprites.Remove(ck, out Sprite2D? s) || s == null)
			return;

		s.QueueFree();
	}

	void ClearAll()
	{
		foreach (KeyValuePair<string, Sprite2D> kv in _sprites)
			kv.Value.QueueFree();

		_sprites.Clear();
	}
}
