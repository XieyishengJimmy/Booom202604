using System.Collections.Generic;
using Godot;

namespace Booom202604;

/// <summary>根据 BOSS 表枚举计算「本次预警锁定」的格子集合；在预警进入时调用一次。</summary>
public static class BossSkillPlanner
{
	static readonly TileSet.CellNeighbor[] NeighborOrder =
	[
		TileSet.CellNeighbor.RightSide,
		TileSet.CellNeighbor.TopRightSide,
		TileSet.CellNeighbor.TopLeftSide,
		TileSet.CellNeighbor.LeftSide,
		TileSet.CellNeighbor.BottomLeftSide,
		TileSet.CellNeighbor.BottomRightSide,
	];

	/// <param name="debugCenterSet">BOSS 在进入预警时为本次技能选定的中心格（便于日志）。</param>
	public static HashSet<string> ResolveLockedCellKeys(TileMapLayer terrain, Godot.Collections.Dictionary validCells,
		Vector2I playerCell, int skillTarget, int skillArea, out Vector2I debugCenterSet)
	{
		var keys = new HashSet<string>();
		debugCenterSet = default;
		if (terrain == null)
			return keys;

		Vector2I center = skillTarget switch
		{
			3 => PickRandomCenterAnywhere(validCells),
			2 => PickRandomCenterCoveringPlayer(terrain, validCells, playerCell, skillArea),
			_ => playerCell,
		};

		debugCenterSet = center;

		foreach (Vector2I c in ExpandShapeFromCenter(terrain, validCells, center, Mathf.Max(skillArea, 0)))
			keys.Add(HexGridUtil.CellKey(c));

		return keys;
	}

	static Vector2I PickRandomCenterAnywhere(Godot.Collections.Dictionary validCells)
	{
		List<Vector2I> candidates = GatherAllValid(validCells);
		if (candidates.Count == 0)
			return default;

		return candidates[(int)(GD.Randi() % candidates.Count)];
	}

	static Vector2I PickRandomCenterCoveringPlayer(TileMapLayer terrain, Godot.Collections.Dictionary validCells,
		Vector2I playerCell, int skillArea)
	{
		List<Vector2I> candidates = GatherAllValid(validCells);
		var ok = new List<Vector2I>();

		foreach (Vector2I center in candidates)
		{
			HashSet<Vector2I> shape = ExpandShapeFromCenter(terrain, validCells, center, Mathf.Max(skillArea, 0));
			if (shape.Contains(playerCell))
				ok.Add(center);
		}

		if (ok.Count == 0)
			return playerCell;

		return ok[(int)(GD.Randi() % ok.Count)];
	}

	static List<Vector2I> GatherAllValid(Godot.Collections.Dictionary validCells)
	{
		var list = new List<Vector2I>();

		foreach (Variant vk in validCells.Keys)
			list.Add(HexGridUtil.ParseKey(vk.AsString()));

		return list;
	}

	static HashSet<Vector2I> ExpandShapeFromCenter(TileMapLayer terrain, Godot.Collections.Dictionary validCells,
		Vector2I center, int skillAreaShape)
	{
		int code = Mathf.Max(skillAreaShape, 0);
		switch (code)
		{
			case 4:
				return CellsAllValid(validCells);
			case 3:
				return CellsTripleRay(terrain, validCells, center);
			default:
			{
				int radius = code <= 0 ? 1 : code;

				return CellsHexDisk(terrain, validCells, center, radius);
			}
		}
	}

	static bool IsValidCoord(Godot.Collections.Dictionary validCells, Vector2I c)
	{
		string k = HexGridUtil.CellKey(c);

		return validCells.ContainsKey(k);
	}

	static HashSet<Vector2I> CellsAllValid(Godot.Collections.Dictionary validCells)
	{
		var set = new HashSet<Vector2I>();

		foreach (Variant vk in validCells.Keys)
			set.Add(HexGridUtil.ParseKey(vk.AsString()));

		return set;
	}

	static HashSet<Vector2I> CellsHexDisk(TileMapLayer terrain, Godot.Collections.Dictionary validCells, Vector2I center,
		int radius)
	{
		var set = new HashSet<Vector2I>();
		if (!IsValidCoord(validCells, center))
			return set;

		set.Add(center);
		var dist = new Dictionary<Vector2I, int> { [center] = 0 };
		var q = new Queue<Vector2I>();

		q.Enqueue(center);

		while (q.Count > 0)
		{
			Vector2I c = q.Dequeue();

			if (dist[c] >= radius)
				continue;

			foreach (Vector2I n in HexGridUtil.Neighbors(terrain, c))
			{
				if (!IsValidCoord(validCells, n))
					continue;
				if (dist.ContainsKey(n))
					continue;

				dist[n] = dist[c] + 1;
				q.Enqueue(n);
				set.Add(n);
			}
		}

		return set;
	}

	static HashSet<Vector2I> CellsTripleRay(TileMapLayer terrain, Godot.Collections.Dictionary validCells,
		Vector2I center)
	{
		var set = new HashSet<Vector2I>();

		if (IsValidCoord(validCells, center))
			set.Add(center);

		for (int i = 0; i < 6; i += 2)
		{
			TileSet.CellNeighbor dir = NeighborOrder[i];
			Vector2I cur = terrain.GetNeighborCell(center, dir);

			while (IsValidCoord(validCells, cur))
			{
				set.Add(cur);
				cur = terrain.GetNeighborCell(cur, dir);
			}
		}

		return set;
	}
}
