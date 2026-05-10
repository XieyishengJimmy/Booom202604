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

	/// <summary>
	/// 与 <c>res://Scenes/map.tscn</c> 地砖参考一致：地块「2」在原点，「1」在 <c>(550,0)</c>，「3」在 <c>(275,385)</c>；
	/// <c>Cloud1/2/3</c> 与同名字块中心重合、同 <c>Scale (1,1)</c>。
	/// </summary>
	public const int MapSceneReferenceNeighborDeltaX = 550;

	/// <summary>map.tscn：第三块相对原点的 Y 向像素差；(0,0)→(0,1) 格心屏移的 Y 分量应为 <c>tile_size.y × 0.75</c>。</summary>
	public const int MapSceneReferenceNeighborDownY = 385;

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

	/// <summary>
	/// Godot 4 六角 + <see cref="TileSet.TileLayoutEnum.StairsRight"/> + 横向半偏移下，
	/// <c>map_to_local</c> 邻格 (1,0) 与 (0,0) 中心差为 <c>(tile_size.x, 0)</c>，
	/// 邻格 (0,1) 与 (0,0) 为 <c>(tile_size.x/2, tile_size.y * 3/4)</c>（源码中 y 先乘 0.75 再乘 tile_size）。
	/// 由此：<c>tile_size.x = 550</c>，<c>tile_size.y = round(385 / 0.75) = 513</c>（与 Fog 参照场景中心距一致）。
	/// </summary>
	public static Vector2I HexTileSizeFromMapSceneReference()
	{
		int tx = Mathf.Max(8, MapSceneReferenceNeighborDeltaX);
		int ty = Mathf.Max(8, Mathf.RoundToInt(MapSceneReferenceNeighborDownY / 0.75f));
		return new Vector2I(tx, ty);
	}

	/// <param name="terrainVariant1To5">1～5，对应 Art/Map/1.png～5.png。</param>
	public static TileSet CreateHexTileset(int terrainVariant1To5 = 1)
	{
		int i = ClampTerrainVariant(terrainVariant1To5) - 1;
		string path = MapTexturePaths[i];

		var tex = LoadMapTexturePrimaryOrFallback(path);

		Vector2I imgSize = new(tex.GetWidth(), tex.GetHeight());
		Vector2I tileSize = HexTileSizeFromMapSceneReference();

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

		data.YSortOrigin = Mathf.RoundToInt(tileSize.Y * 0.58f);

		ts.AddSource(source, 0);

		return ts;
	}

	public static bool TryGetPrimaryAtlasTexturePixelSize(TileSet tileSet, out Vector2I sizePx)
	{
		sizePx = Vector2I.Zero;

		for (int i = 0; i < tileSet.GetSourceCount(); i++)
		{
			int sid = tileSet.GetSourceId(i);
			TileSetSource raw = tileSet.GetSource(sid);
			if (raw is TileSetAtlasSource atlas && atlas.Texture != null)
			{
				sizePx = new Vector2I(atlas.Texture.GetWidth(), atlas.Texture.GetHeight());
				return true;
			}
		}

		return false;
	}


	/// <summary>单格格子对应的 atlas 区域尺寸（整块地砖含高度层）；与迷雾/角色缩放对齐。</summary>
	public static bool TryGetPrimaryAtlasTileDrawablePixelSize(TileSet tileSet, out Vector2I regionPx)
	{
		regionPx = Vector2I.Zero;

		for (int i = 0; i < tileSet.GetSourceCount(); i++)
		{
			int sid = tileSet.GetSourceId(i);
			TileSetSource raw = tileSet.GetSource(sid);
			if (raw is TileSetAtlasSource atlas)
			{
				regionPx = atlas.TextureRegionSize;
				return regionPx.X > 0 && regionPx.Y > 0;
			}
		}

		return false;
	}

	/// <summary>
	/// 六角格逻辑包围盒相对 atlas 尺寸的均匀比：<c>min(tileSize.x/atlas.x, tileSize.y/atlas.y)</c>（便于估算「塞进六角」缩放）。
	/// 注意：<c>TileMapLayer</c> 实际绘制整块 atlas 时使用区域像素宽高作目标矩形（约 1 纹素/世界单位），未必等于本比值。
	/// </summary>
	public static bool TryComputeHexAtlasUniformFitScale(TileSet tileSet, out float k)
	{
		k = 1f;

		if (!TryGetPrimaryAtlasTileDrawablePixelSize(tileSet, out Vector2I atlas))
			return false;

		Vector2I bbox = tileSet.TileSize;
		k = Mathf.Min((float)bbox.X / atlas.X, (float)bbox.Y / atlas.Y);
		return k > 1e-5f;
	}

	/// <summary>
	/// 与 <c>TileMapLayer</c> 绘制一致：<see cref="TileData.TextureOrigin"/> 从格心锚点扣除（见 Godot <c>compute_transformed_tile_dest_rect</c>）。
	/// </summary>
	public static bool TryGetPrimaryTileTextureOriginPx(TileSet tileSet, out Vector2I originPx)
	{
		originPx = Vector2I.Zero;

		for (int i = 0; i < tileSet.GetSourceCount(); i++)
		{
			int sid = tileSet.GetSourceId(i);
			if (tileSet.GetSource(sid) is not TileSetAtlasSource atlas)
				continue;

			if (!atlas.HasTile(Vector2I.Zero))
				continue;

			TileData data = atlas.GetTileData(Vector2I.Zero, 0);
			originPx = data.TextureOrigin;
			return true;
		}

		return false;
	}

	/// <summary>
	/// 玩家世界 Sprite2D 缩放：与六角格绘制使用同一「纹素像素 → 棋盘格坐标」倍数，
	/// 使 480×480 站立图相对 600×600 地砖图在实际尺寸上保留 480:600。
	/// </summary>
	public static float PlayerSpriteScaleMatchingTerrainPixels(TileSet? tileSet, float fallbackLegacy = 0.63f)
	{
		if (tileSet == null || !TryGetPrimaryAtlasTileDrawablePixelSize(tileSet, out Vector2I drawablePx))
			return fallbackLegacy;

		int drawH = drawablePx.Y;
		if (drawH <= 0)
			return fallbackLegacy;

		float tileH = tileSet.TileSize.Y;
		return tileH / drawH;
	}
}
