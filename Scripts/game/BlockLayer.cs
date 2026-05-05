using System.Collections.Generic;
using Godot;

namespace Booom202604;

public partial class BlockLayer : Node2D
{
	TileMapLayer? _terrain;
	readonly Dictionary<string, Sprite2D> _sprites = [];

	public void Setup(TileMapLayer terrainLayer)
	{
		_terrain = terrainLayer;
	}

	public void SetBlocks(Godot.Collections.Dictionary blockCells)
	{
		ClearAll();

		foreach (Variant keyVar in blockCells.Keys)
		{
			if (!blockCells[keyVar].AsBool())
				continue;

			string ck = keyVar.AsString();
			AddBlock(HexGridUtil.ParseKey(ck));
		}
	}

	void AddBlock(Vector2I cell)
	{
		if (_terrain == null)
			return;

		string ck = HexGridUtil.CellKey(cell);
		if (_sprites.ContainsKey(ck))
			return;

		var img = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
		img.Fill(new Color(0.82f, 0.18f, 0.22f, 0.35f));
		var tex = ImageTexture.CreateFromImage(img);

		var s = new Sprite2D
		{
			Texture = tex,
			Centered = true,
			Position = _terrain.MapToLocal(cell),
			Scale = new Vector2(1.1f, 1.1f),
		};

		AddChild(s);
		_sprites[ck] = s;
	}

	void ClearAll()
	{
		foreach (KeyValuePair<string, Sprite2D> kv in _sprites)
			kv.Value.QueueFree();

		_sprites.Clear();
	}
}
