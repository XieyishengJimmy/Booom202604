using System.Collections.Generic;
using Godot;

namespace Booom202604;

/// <summary>BOSS 预警阶段的范围高亮；与迷雾层分离。贴图与六角地块锚点一致。子精灵勿设正 <c>ZIndex</c>，以免压过 <c>gameplay.tscn</c> 中位于其后的障碍/迷雾/事件/玩家。</summary>
public partial class BossWarningLayer : Node2D
{
	static readonly Texture2D AttackTex = GD.Load<Texture2D>("res://Art/UI/map/AttackIndicator.png")!;

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

		Vector2 sc = HexMapIndicatorOverlay.ComputeSpriteScaleMatchTilemap(_terrain.TileSet, AttackTex);
		var s = new Sprite2D
		{
			Texture = AttackTex,
			Centered = true,
			Modulate = new Color(1f, 0.42f, 0.32f, 0.72f),
			Scale = sc,
			Position = HexMapIndicatorOverlay.HexCellAnchorWorld(_terrain, cell),
		};
		AddChild(s);
		_sprites[ck] = s;
	}
}
