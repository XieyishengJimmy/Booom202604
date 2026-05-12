using Godot;

namespace Booom202604;

/// <summary>与 <see cref="FogLayer"/> 一致：叠图按地砖 atlas 区域像素与 TileMap 绘制对齐（整块 atlas 纹素 ≈ 世界单位）；横向/纵向可独立缩放。</summary>
public static class HexMapIndicatorOverlay
{
	public static Vector2 ComputeSpriteScaleMatchTilemap(TileSet? tileSet, Texture2D tex, float coverageMul = 1f)
	{
		int tw = tex.GetWidth();
		int th = tex.GetHeight();

		if (tw <= 0 || th <= 0)
			return new Vector2(0.52f * coverageMul, 0.52f * coverageMul);

		if (tileSet == null)
			return new Vector2(0.52f * coverageMul, 0.52f * coverageMul);

		if (!TerrainTilesetFactory.TryGetPrimaryAtlasTileDrawablePixelSize(tileSet, out Vector2I atlas))
		{
			float sx = (float)tileSet.TileSize.X / tw;
			float sy = (float)tileSet.TileSize.Y / th;
			float vf = Mathf.Min(sx, sy) * coverageMul;

			return new Vector2(vf, vf);
		}

		float sx2 = (float)atlas.X / tw * coverageMul;
		float sy2 = (float)atlas.Y / th * coverageMul;

		return new Vector2(sx2, sy2);
	}

	/// <summary>与迷雾层一致：<c>map_to_local - texture_origin</c>。</summary>
	public static Vector2 HexCellAnchorWorld(TileMapLayer terrain, Vector2I cell)
	{
		Vector2 off = TerrainTilesetFactory.TryGetPrimaryTileTextureOriginPx(terrain.TileSet!, out Vector2I o)
			? new Vector2(o.X, o.Y)
			: Vector2.Zero;

		return terrain.MapToLocal(cell) - off;
	}

	/// <summary>
	/// 在 <c>HexCellAnchorWorld</c> 之后的额外平移，与 <c>Scenes/map.tscn</c> 中「ClickIndicator.position − 同格地砖.position」一致（移动/交互指示参考）。
	/// 当前 map 中三枚指示与地砖同心；若按六角行再微调，可改为按 <c>(cell.X &amp; 1, cell.Y &amp; 1)</c> 查表。
	/// </summary>
	public static Vector2 ClickIndicatorOffsetForHexCell(Vector2I _) =>
		Vector2.Zero;

	/// <summary>与 TileMap 单格绘制锚点一致，再叠加 <see cref="ClickIndicatorOffsetForHexCell"/>（与 map.tscn 手工微调一致）。</summary>
	public static Vector2 ClickIndicatorWorldPosition(TileMapLayer terrain, Vector2I cell) =>
		HexCellAnchorWorld(terrain, cell) + ClickIndicatorOffsetForHexCell(cell);
}
