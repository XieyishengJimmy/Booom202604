using Godot;
using System.Collections.Generic;

namespace Booom202604;

public partial class HighlightLayer : Node2D
{
	private TileMapLayer? _terrain;
	private readonly Dictionary<string, Sprite2D> _sprites = new();
	/// <summary>主动技能选格高亮；贴图 <c>Skilldicator.png</c>。锚点与 <see cref="BossWarningLayer"/> 一致；<b>勿</b>复用 Attack 的橙红 <c>Modulate</c>，以免 Skilldicator 被染成预警色。</summary>
	private static readonly Texture2D SkillTex = GD.Load<Texture2D>("res://Art/UI/map/Skilldicator.png")!;

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

		Vector2 sc = HexMapIndicatorOverlay.ComputeSpriteScaleMatchTilemap(_terrain.TileSet, SkillTex);
		var s = new Sprite2D
		{
			Texture = SkillTex,
			Centered = true,
			Modulate = new Color(1f, 1f, 1f, 0.78f),
			Scale = sc,
			Position = HexMapIndicatorOverlay.HexCellAnchorWorld(_terrain, cell),
		};
		AddChild(s);
		_sprites[ck] = s;
	}
}
