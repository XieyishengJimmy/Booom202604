using Godot;

namespace Booom202604;

public static class TerrainTilesetFactory
{
	private static readonly Texture2D MapTex = GD.Load<Texture2D>("res://Art/Map/1.png");

	public static void ApplyTerrainPresentation(TileMapLayer terrain)
	{
		terrain.YSortEnabled = true;
	}

	public static TileSet CreateHexTileset()
	{
		var tex = MapTex!;
		Vector2I imgSize = new(tex.GetWidth(), tex.GetHeight());
		const float PackTightFactor = 0.9f;
		Vector2I tileSize = new(
			Mathf.Max(8, (int)Mathf.Floor(imgSize.X * PackTightFactor)),
			Mathf.Max(8, (int)Mathf.Floor(imgSize.Y * PackTightFactor)));

		var ts = new TileSet
		{
			TileShape = TileSet.TileShapeEnum.Hexagon,
			TileLayout = TileSet.TileLayoutEnum.StairsRight,
			TileOffsetAxis = TileSet.TileOffsetAxisEnum.Horizontal,
			TileSize = tileSize
		};

		var source = new TileSetAtlasSource
		{
			Texture = tex,
			TextureRegionSize = imgSize,

		};


		source.CreateTile(Vector2I.Zero);

		// 伪立体侧墙会从格子中心向下延伸；用“靠屏幕下方”的深度点排序，才能把前排顶面盖住后排侧墙。
		TileData data = source.GetTileData(Vector2I.Zero, 0);
		data.YSortOrigin = Mathf.RoundToInt(tileSize.Y * 0.45f);



		ts.AddSource(source, 0);



		return ts;



	}







}
