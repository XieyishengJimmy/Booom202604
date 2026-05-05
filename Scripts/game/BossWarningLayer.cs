using System.Collections.Generic;
using Godot;

namespace Booom202604;

/// <summary>BOSS 预警阶段的范围高亮；与迷雾层分离。</summary>
public partial class BossWarningLayer : Node2D
{
	readonly Texture2D _ringTex = GD.Load<Texture2D>("res://Art/Map/1.png")!;

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

		var s = new Sprite2D
		{
			Texture = _ringTex,
			Centered = true,
			Modulate = new Color(1f, 0.35f, 0.2f, 0.55f),
			Scale = new Vector2(0.5f, 0.5f),
			ZIndex = 1,
			Position = _terrain.MapToLocal(cell),
		};
		AddChild(s);
		_sprites[ck] = s;
	}
}
