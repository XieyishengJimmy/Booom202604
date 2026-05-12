using System.Collections.Generic;
using Godot;

namespace Booom202604;

/// <summary>玩家回合：相邻可移动 / 可交互格指示；贴图 <c>ClickIndicator.png</c>。与主动技能选格 <see cref="HighlightLayer"/>（<c>Skilldicator.png</c>）分离，勿混用。</summary>
public partial class InteractionHintLayer : Node2D
{
	static readonly Texture2D Tex = GD.Load<Texture2D>("res://Art/UI/map/ClickIndicator.png")!;

	TileMapLayer? _terrain;
	readonly Dictionary<string, Sprite2D> _sprites = [];

	public void Setup(TileMapLayer terrainLayer) =>
		_terrain = terrainLayer;

	public void ClearAll()
	{
		foreach (KeyValuePair<string, Sprite2D> kv in _sprites)
			kv.Value.QueueFree();

		_sprites.Clear();
	}

	public void RebuildFromKeys(IEnumerable<string> cellKeys)
	{
		ClearAll();

		if (_terrain == null)
			return;

		foreach (string ck in cellKeys)
			Add(HexGridUtil.ParseKey(ck));
	}

	void Add(Vector2I cell)
	{
		if (_terrain == null)
			return;

		string ck = HexGridUtil.CellKey(cell);
		if (_sprites.ContainsKey(ck))
			return;

		Vector2 sc = HexMapIndicatorOverlay.ComputeSpriteScaleMatchTilemap(_terrain.TileSet, Tex);
		var s = new Sprite2D
		{
			Texture = Tex,
			Centered = true,
			Modulate = new Color(1f, 1f, 1f, 0.82f),
			Scale = sc,
			ZIndex = 0,
			Position = HexMapIndicatorOverlay.ClickIndicatorWorldPosition(_terrain, cell),
		};
		AddChild(s);
		_sprites[ck] = s;
	}
}
