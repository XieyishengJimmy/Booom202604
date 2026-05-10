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

	/// <summary>在六角邻接图上对 <paramref name="allowed"/> 内的格子做无权 BFS。</summary>
	public static Dictionary<Vector2I, int> BfsStepsFrom(HashSet<Vector2I> allowed, TileMapLayer layer, Vector2I start)
	{
		var depths = new Dictionary<Vector2I, int>();
		if (!allowed.Contains(start))
			return depths;

		var queue = new Queue<Vector2I>();
		depths[start] = 0;
		queue.Enqueue(start);

		while (queue.Count > 0)
		{
			Vector2I c = queue.Dequeue();
			int next = depths[c] + 1;
			foreach (Vector2I n in Neighbors(layer, c))
			{
				if (!allowed.Contains(n) || depths.ContainsKey(n))
					continue;
				depths[n] = next;
				queue.Enqueue(n);
			}
		}

		return depths;
	}
}
