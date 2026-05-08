using System.Collections.Generic;
using Godot;

namespace Booom202604;

public partial class BlockLayer : Node2D
{
	const string ObstacleIconPath = "res://Art/Icon/Obstacle.png";

	TileMapLayer? _terrain;
	readonly Dictionary<string, Sprite2D> _sprites = [];
	static Texture2D? _obstacleTex;

	static Texture2D? ObstacleTexture()
	{
		if (_obstacleTex != null)
			return _obstacleTex;
		if (ResourceLoader.Exists(ObstacleIconPath))
			_obstacleTex = GD.Load<Texture2D>(ObstacleIconPath);
		return _obstacleTex;
	}

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

		Texture2D? tex = ObstacleTexture();
		if (tex == null)
		{
			var img = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
			img.Fill(new Color(0.82f, 0.18f, 0.22f, 0.35f));
			tex = ImageTexture.CreateFromImage(img);
		}

		float sc = HexEventMarker.EventIconSpriteScale;
		var s = new Sprite2D
		{
			Texture = tex,
			Centered = true,
			Position = _terrain.MapToLocal(cell),
			Scale = new Vector2(sc, sc),
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
