using System.Collections.Generic;
using Godot;

namespace Booom202604;

/// <summary>
/// 根据 BOSS 表枚举计算「本次预警锁定」的格子集合；在预警进入时调用一次。
/// <para>
/// 「技能范围」不是图上的 BFS 层数绕圈：在与玩家对齐的六角格地图中，任选一条<strong>六角轴向射线</strong>，
/// <strong>连续的 L 格</strong>在同一直线上。<c>BOSS技能范围定义</c>列是离散标号（例如<strong>标号 5 ⇒ 直线 3 格</strong>），
/// 具体 <c>L</c> 见 <c>LineLengthCellsFromSkillAreaCode</c>。
/// </para>
/// <para>
/// <c>skill_target == 3</c>（全图随机落中心）的旧「巫妖」式技能仍沿用<strong>六角圆盘半径 = skill_area 数值</strong>，
/// 以便锁住足够多格子供文案中的「随机 N 格子」取样。
/// </para>
/// </summary>
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

	/// <param name="debugCenterSet">BOSS 在本次技能中取定的「轴向直线」的起点格（线段第一格）；全图随机中心目标则为本次随机中心。</param>
	public static HashSet<string> ResolveLockedCellKeys(TileMapLayer terrain, Godot.Collections.Dictionary validCells,
		Vector2I playerCell, int skillTarget, int skillArea, out Vector2I debugCenterSet)
	{
		var keys = new HashSet<string>();
		debugCenterSet = default;

		if (terrain == null)
			return keys;

		HashSet<Vector2I> shapeCells = ResolveShapeCellsLocal(terrain, validCells, playerCell, skillTarget, skillArea,
			out debugCenterSet);

		foreach (Vector2I c in shapeCells)
			keys.Add(HexGridUtil.CellKey(c));

		return keys;
	}

	static HashSet<Vector2I> ResolveShapeCellsLocal(TileMapLayer terrain, Godot.Collections.Dictionary validCells,
		Vector2I playerCell, int skillTarget, int skillArea, out Vector2I debugRepresentativeCell)
	{
		debugRepresentativeCell = default;
		int rawCode = Mathf.Max(skillArea, 0);

		if (rawCode == 4)
			return CellsAllValid(validCells);

		// Target 3：旧数据按「六角圆盘半径」理解巫妖范围；不改变 skill_area 的数值语义。
		if (skillTarget == 3)
		{
			Vector2I pick = PickRandomCenterAnywhere(validCells);
			debugRepresentativeCell = pick;
			int radius = rawCode <= 0 ? 1 : rawCode;

			return CellsHexDisk(terrain, validCells, pick, radius);
		}

		if (skillTarget == 2)
			return ResolveLineTargetingPlayerCoverage(terrain, validCells, playerCell, rawCode, out debugRepresentativeCell);

		debugRepresentativeCell = playerCell;

		return ResolveLineAnchoredAtPlayerRandomDir(terrain, validCells, playerCell, rawCode);
	}

	/// <summary>标号 ⇒ 轴向直线连续的格子数。<c>5</c> 策划定义为「直线 3 格」。</summary>
	static int LineLengthCellsFromSkillAreaCode(int skillAreaCode) =>
		skillAreaCode switch
		{
			0 => 1,
			1 => 1,
			2 => 2,
			3 => 3,
			// 离散标号 ≠ 字面长度：
			5 => 3,
			// 独角兽国王等沿用了「长直线」字面 6；
			6 => 6,
			// 兜底：未知的正标号按其数值当作线段长度；
			_ => skillAreaCode < 1 ? 1 : skillAreaCode,
		};

	static Vector2I PickRandomCenterAnywhere(Godot.Collections.Dictionary validCells)
	{
		List<Vector2I> candidates = GatherAllValid(validCells);
		if (candidates.Count == 0)
			return default;

		return candidates[(int)(GD.Randi() % candidates.Count)];
	}

	static List<Vector2I> GatherAllValid(Godot.Collections.Dictionary validCells)
	{
		var list = new List<Vector2I>();

		foreach (Variant vk in validCells.Keys)
			list.Add(HexGridUtil.ParseKey(vk.AsString()));

		return list;
	}

	static bool IsValidCoord(Godot.Collections.Dictionary validCells, Vector2I c) =>
		validCells.ContainsKey(HexGridUtil.CellKey(c));

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

	static bool TryBuildAxialRay(TileMapLayer terrain, Godot.Collections.Dictionary validCells,
		Vector2I startAnchor, TileSet.CellNeighbor dir, int segmentLengthCells, HashSet<Vector2I> sink)
	{
		sink.Clear();
		Vector2I cur = startAnchor;

		for (int i = 0; i < segmentLengthCells; i++)
		{
			if (!IsValidCoord(validCells, cur))
				return false;

			sink.Add(cur);

			if (i == segmentLengthCells - 1)
				break;

			cur = terrain.GetNeighborCell(cur, dir);
		}

		return true;
	}

	static TileSet.CellNeighbor OppositeNeighbor(TileSet.CellNeighbor d) =>
		d switch
		{
			TileSet.CellNeighbor.RightSide => TileSet.CellNeighbor.LeftSide,
			TileSet.CellNeighbor.LeftSide => TileSet.CellNeighbor.RightSide,
			TileSet.CellNeighbor.TopRightSide => TileSet.CellNeighbor.BottomLeftSide,
			TileSet.CellNeighbor.BottomLeftSide => TileSet.CellNeighbor.TopRightSide,
			TileSet.CellNeighbor.TopLeftSide => TileSet.CellNeighbor.BottomRightSide,
			TileSet.CellNeighbor.BottomRightSide => TileSet.CellNeighbor.TopLeftSide,
			_ => TileSet.CellNeighbor.LeftSide,
		};

	static Vector2I InverseStep(TileMapLayer terrain, Vector2I from, TileSet.CellNeighbor forwardDir)
	{
		TileSet.CellNeighbor backDir = OppositeNeighbor(forwardDir);

		return terrain.GetNeighborCell(from, backDir);
	}

	static HashSet<Vector2I> ResolveLineAnchoredAtPlayerRandomDir(TileMapLayer terrain,
		Godot.Collections.Dictionary validCells, Vector2I playerAnchor, int skillAreaCode)
	{
		int l = LineLengthCellsFromSkillAreaCode(skillAreaCode);

		var acc = new HashSet<Vector2I>();
		TileSet.CellNeighbor[] dirs = NeighborOrder;
		int startDir = (int)(GD.Randi() % 6);

		for (int t = 0; t < 6; t++)
		{
			int di = (startDir + t) % 6;

			if (TryBuildAxialRay(terrain, validCells, playerAnchor, dirs[di], l, acc))
				return acc;
		}

		acc.Clear();
		if (IsValidCoord(validCells, playerAnchor))
			acc.Add(playerAnchor);

		return acc;
	}

	static HashSet<Vector2I> ResolveLineTargetingPlayerCoverage(TileMapLayer terrain,
		Godot.Collections.Dictionary validCells, Vector2I playerCell, int skillAreaCode, out Vector2I debugRayStartAnchor)
	{
		debugRayStartAnchor = playerCell;

		int l = LineLengthCellsFromSkillAreaCode(skillAreaCode);
		var candidateStarts = new List<(Vector2I start, int dirIx)>();
		var probe = new HashSet<Vector2I>();

		for (int dirIx = 0; dirIx < 6; dirIx++)
		{
			TileSet.CellNeighbor dir = NeighborOrder[dirIx];

			for (int k = 0; k < l; k++)
			{
				Vector2I start = playerCell;

				for (int step = 0; step < k; step++)
					start = InverseStep(terrain, start, dir);

				if (!TryBuildAxialRay(terrain, validCells, start, dir, l, probe))
					continue;

				if (!probe.Contains(playerCell))
					continue;

				candidateStarts.Add((start, dirIx));
			}
		}

		if (candidateStarts.Count > 0)
		{
			(Vector2I pickedStart, int pickedDir) = candidateStarts[(int)(GD.Randi() % candidateStarts.Count)];
			debugRayStartAnchor = pickedStart;
			HashSet<Vector2I> ray = [];

			TryBuildAxialRay(terrain, validCells, pickedStart, NeighborOrder[pickedDir], l, ray);

			return ray;
		}

		var fallback = new HashSet<Vector2I>();

		if (IsValidCoord(validCells, playerCell))
			fallback.Add(playerCell);

		return fallback;
	}
}
