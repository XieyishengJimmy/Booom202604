using Godot;
using System.Collections.Generic;

namespace Booom202604;

public partial class HighlightLayer : Node2D
{
	private TileMapLayer? _terrain;
	private readonly Dictionary<string, Sprite2D> _sprites = new();
	private static readonly Texture2D ClickTex = GD.Load<Texture2D>("res://Art/UI/map/ClickIndicator.png")!;

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

		Vector2 sc = HexMapIndicatorOverlay.ComputeSpriteScaleMatchTilemap(_terrain.TileSet, ClickTex);
		var s = new Sprite2D
		{
			Texture = ClickTex,
			Centered = true,
			Modulate = new Color(0.25f, 0.95f, 0.45f, 0.78f),
			Scale = sc,
			ZIndex = 1,
			Position = HexMapIndicatorOverlay.ClickIndicatorWorldPosition(_terrain, cell),
		};
		AddChild(s);
		_sprites[ck] = s;
	}
}
