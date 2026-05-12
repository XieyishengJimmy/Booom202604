using Godot;

namespace Booom202604;

/// <summary>
/// 叠在六角地砖上的 UI 精灵缩放：<c>TileMap</c> 将整块地砖贴图绘制进 <see cref="TileSet.TileSize"/> 的逻辑六角格（与 <see cref="TerrainTilesetFactory.HexTileSizeFromMapSceneReference"/> 一致），
/// 叠图世界尺度须对齐该<strong>逻辑格</strong>，而不是 atlas 整图像素（<c>Scenes/map.tscn</c> 里地砖用 <c>Sprite2D</c> 1:1 像素摆场，与关卡内 <c>TileMapLayer</c> 的缩放语义不同）。
/// 使用均匀 <c>(k,k)</c>，且 <c>k = min(TileSize.x/tw, TileSize.y/th)</c>，使叠图完整落在六角格包围盒内、宽度不再按整张贴图被放大。
/// </summary>
public static class HexMapIndicatorOverlay
{
	public static Vector2 ComputeSpriteScaleMatchTilemap(TileSet? tileSet, Texture2D tex, float coverageMul = 1f)
	{
		int tw = tex.GetWidth();
		int th = tex.GetHeight();

		if (tw <= 0 || th <= 0 || tileSet == null)
			return new Vector2(0.52f * coverageMul, 0.52f * coverageMul);

		Vector2I logical = tileSet.TileSize;
		float kx = (float)logical.X / tw * coverageMul;
		float ky = (float)logical.Y / th * coverageMul;
		float k = Mathf.Min(kx, ky);

		return new Vector2(k, k);
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
	/// <c>Scenes/map.tscn</c> 中 ClickIndicator2 / ClickIndicator / ClickIndicator3 相对地砖格心 (0,0)、(550,0)、(275,385) 的位移，
	/// 与 <see cref="TerrainTilesetFactory.MapSceneReferenceNeighborDeltaX"/> 等参考布局一致；对应格坐标 (0,0)、(1,0)、(0,1) 的奇偶推广。
	/// 与迷雾锚点不同：<c>ClickIndicator</c> 美术以格心为基准再平移，故<strong>不减</strong> <c>TextureOrigin</c>。
	/// </summary>
	public static Vector2 ClickIndicatorOffsetForHexCell(Vector2I cell)
	{
		float ox = (cell.X & 1) != 0 ? 3f : 0f;
		float oy = (cell.Y & 1) != 0 ? -25f : -34f;
		return new Vector2(ox, oy);
	}

	/// <summary>与 <see cref="ClickIndicatorOffsetForHexCell"/> 及 <c>map.tscn</c> 示意一致的世界坐标。</summary>
	public static Vector2 ClickIndicatorWorldPosition(TileMapLayer terrain, Vector2I cell) =>
		terrain.MapToLocal(cell) + ClickIndicatorOffsetForHexCell(cell);
}
