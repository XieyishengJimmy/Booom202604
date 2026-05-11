using Godot;
using System.Collections.Generic;

namespace Booom202604;

public partial class HighlightLayer : Node2D
{
	private TileMapLayer? _terrain;
	private readonly Dictionary<string, Sprite2D> _sprites = new();
	private readonly Texture2D _highlightTex = GD.Load<Texture2D>("res://Art/Map/1.png")!;

	public void Setup(TileMapLayer terrainLayer)
	{
		_terrain = terrainLayer;
	}

	public void ClearAll()
	{
		foreach (var kv in _sprites)
			kv.Value.QueueFree();
		_sprites.Clear();
	}

	public void RebuildFromKeys(IEnumerable<string> cellKeys)
	{
		ClearAll();
		if (_terrain == null) return;

		foreach (string ck in cellKeys)
			Add(HexGridUtil.ParseKey(ck));
	}

	private void Add(Vector2I cell)
	{
		if (_terrain == null) return;

		string ck = HexGridUtil.CellKey(cell);
		if (_sprites.ContainsKey(ck)) return;

		var s = new Sprite2D
		{
			Texture = _highlightTex,
			Centered = true,
			Modulate = new Color(0.2f, 0.8f, 0.3f, 0.55f),
			Scale = new Vector2(0.5f, 0.5f),
			ZIndex = 1,
			Position = _terrain.MapToLocal(cell),
		};
		AddChild(s);
		_sprites[ck] = s;
	}
}
