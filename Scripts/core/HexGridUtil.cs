using System.Collections.Generic;
using Godot;

namespace Booom202604;

public static class HexGridUtil
{
	public static string CellKey(Vector2I c) => $"{c.X},{c.Y}";

	public static Vector2I ParseKey(string key)
	{
		string[] p = key.Split(',');
		return new Vector2I(int.Parse(p[0]), int.Parse(p[1]));
	}

	public static bool IsSameCell(Vector2I a, Vector2I b) => a.X == b.X && a.Y == b.Y;

	public static List<Vector2I> Neighbors(TileMapLayer? layer, Vector2I c)
	{
		var o = new List<Vector2I>();
		if (layer == null)
			return o;

		TileSet.CellNeighbor[] neigh =
		[
			TileSet.CellNeighbor.RightSide,
			TileSet.CellNeighbor.LeftSide,
			TileSet.CellNeighbor.TopRightSide,
			TileSet.CellNeighbor.TopLeftSide,
			TileSet.CellNeighbor.BottomRightSide,
			TileSet.CellNeighbor.BottomLeftSide,
		];

		foreach (TileSet.CellNeighbor n in neigh)
			o.Add(layer.GetNeighborCell(c, n));

		return o;
	}

	/// <summary>
	/// 若为从 <paramref name="from"/> 到相邻格 <paramref name="to"/> 的一步，返回所使用的六角邻接方向。
	/// </summary>
	public static bool TryGetNeighborStepDirection(TileMapLayer layer, Vector2I from, Vector2I to,
		out TileSet.CellNeighbor direction)
	{
		direction = default;

		TileSet.CellNeighbor[] order =
		[
			TileSet.CellNeighbor.RightSide,
			TileSet.CellNeighbor.LeftSide,
			TileSet.CellNeighbor.TopRightSide,
			TileSet.CellNeighbor.TopLeftSide,
			TileSet.CellNeighbor.BottomRightSide,
			TileSet.CellNeighbor.BottomLeftSide,
		];

		foreach (TileSet.CellNeighbor d in order)
		{
			if (IsSameCell(layer.GetNeighborCell(from, d), to))
			{
				direction = d;
				return true;
			}
		}

		return false;
	}
}
