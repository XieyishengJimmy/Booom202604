using Godot;

namespace Booom202604;

public static class TerrainTilesetFactory
{
	/// <summary>与 <c>res://Art/Map</c> 下 1.png～5.png 对应，写入关卡 JSON：<c>terrain_variant</c>。</summary>
	public const string TerrainVariantDictKey = "terrain_variant";

	public static readonly string[] MapTexturePaths =
	[
		"res://Art/Map/1.png",
		"res://Art/Map/2.png",
		"res://Art/Map/3.png",
		"res://Art/Map/4.png",
		"res://Art/Map/5.png",
	];

	public static int ClampTerrainVariant(int variant1To5)
	{
		return Mathf.Clamp(variant1To5, 1, MapTexturePaths.Length);
	}

	public static int ResolveTerrainVariantFromLevel(Godot.Collections.Dictionary d)
	{
		if (!d.TryGetValue(TerrainVariantDictKey, out Variant v))
			return 1;

		return v.VariantType switch
		{
			Variant.Type.Int => ClampTerrainVariant(v.AsInt32()),
			Variant.Type.Float => ClampTerrainVariant((int)v.AsDouble()),
			Variant.Type.String when int.TryParse(v.AsString().Trim(),
				System.Globalization.NumberStyles.Integer,
				System.Globalization.CultureInfo.InvariantCulture, out int p) =>
				ClampTerrainVariant(p),
			_ => 1,
		};
	}

	static Texture2D LoadMapTexturePrimaryOrFallback(string path)
	{
		if (ResourceLoader.Exists(path))
		{
			Texture2D? tex = GD.Load<Texture2D>(path);
			if (tex != null)
				return tex;
		}

		GD.PushWarning($"TerrainTilesetFactory: 无法加载 {path}，回退到 {MapTexturePaths[0]}");
		return GD.Load<Texture2D>(MapTexturePaths[0])!;
	}

	public static void ApplyTerrainPresentation(TileMapLayer terrain)
	{
		terrain.YSortEnabled = true;
	}

	/// <param name="terrainVariant1To5">1～5，对应 Art/Map/1.png～5.png。</param>
	public static TileSet CreateHexTileset(int terrainVariant1To5 = 1)
	{
		int i = ClampTerrainVariant(terrainVariant1To5) - 1;
		string path = MapTexturePaths[i];

		var tex = LoadMapTexturePrimaryOrFallback(path);

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

		TileData data = source.GetTileData(Vector2I.Zero, 0);
		data.YSortOrigin = Mathf.RoundToInt(tileSize.Y * 0.45f);

		ts.AddSource(source, 0);

		return ts;
	}
}
