using System.Collections.Generic;
using Godot;

namespace Booom202604;

/// <summary>
/// 根据 BOSS 表枚举计算「本次预警锁定」的格子集合；在预警进入时调用一次。
/// <para>
/// 六角轴向<strong>线段</strong>：<strong>连续的 L 格</strong>。策划离散标号见 <c>LineLengthCellsFromSkillAreaCode</c>（如标号 5 ⇒ 3 格）。
/// </para>
/// <para>
/// <c>skill_area == <see cref="SkillAreaFullAxialLineThroughPlayer"/></c>（当前为 <c>10</c>）且 <c>skill_target == 2</c>：
/// 取一条六角轴向<strong>横穿可走区域</strong>的直线（越过玩家格向两侧延伸，直到离开可走格或撞到障碍格），用于「独角仙」式全图直线预警。
/// </para>
/// <para>
/// <c>skill_target == 3</c>（全图随机落中心）仍按<strong>六角圆盘半径 = skill_area 数值</strong>。
/// </para>
/// <para>
/// <c>blockedState</c> 与本体战斗一致：障碍格既不可作为 BOSS 范围落点，也不可作为直线穿行格。
/// </para>
/// </summary>
public static class BossSkillPlanner
{
	/// <summary>
	/// 与 <c>skill_target == 2</c> 配合：整条六角轴向直径线（可走范围内），必含玩家格。
	/// </summary>
	public const int SkillAreaFullAxialLineThroughPlayer = 10;

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
		Godot.Collections.Dictionary blockedState, Vector2I playerCell, int skillTarget, int skillArea, int bossTableId,
		out Vector2I debugCenterSet)
	{
		var keys = new HashSet<string>();
		debugCenterSet = default;

		if (terrain == null)
			return keys;

		HashSet<Vector2I> shapeCells = ResolveShapeCellsLocal(terrain, validCells, blockedState, playerCell, skillTarget,
			skillArea, bossTableId, out debugCenterSet);

		foreach (Vector2I c in shapeCells)
			keys.Add(HexGridUtil.CellKey(c));

		return keys;
	}

	static HashSet<Vector2I> ResolveShapeCellsLocal(TileMapLayer terrain, Godot.Collections.Dictionary validCells,
		Godot.Collections.Dictionary blockedState, Vector2I playerCell, int skillTarget, int skillArea, int bossTableId,
		out Vector2I debugRepresentativeCell)
	{
		debugRepresentativeCell = default;
		int rawCode = Mathf.Max(skillArea, 0);

		// 独角仙（1003）策划描述为横穿地图直线；表里曾用离散标号 3（短线）。若仍为 3，按全直径直线处理以免导表退回旧数据。
		if (bossTableId == 1003 && skillTarget == 2 && rawCode == 3)
			rawCode = SkillAreaFullAxialLineThroughPlayer;

		if (rawCode == 4)
			return CellsAllWalkable(validCells, blockedState);

		// Target 3：旧数据按「六角圆盘半径」理解巫妖范围；不改变 skill_area 的数值语义。
		if (skillTarget == 3)
		{
			Vector2I pick = PickRandomCenterAnywhere(validCells, blockedState);
			debugRepresentativeCell = pick;
			int radius = rawCode <= 0 ? 1 : rawCode;

			return CellsHexDisk(terrain, validCells, blockedState, pick, radius);
		}

		if (skillTarget == 2 && rawCode == SkillAreaFullAxialLineThroughPlayer)
		{
			debugRepresentativeCell = playerCell;
			return CollectFullAxialDiameterThroughPlayer(terrain, validCells, blockedState, playerCell);
		}

		if (skillTarget == 2)
			return ResolveLineTargetingPlayerCoverage(terrain, validCells, blockedState, playerCell, rawCode,
				out debugRepresentativeCell);

		debugRepresentativeCell = playerCell;

		return ResolveLineAnchoredAtPlayerRandomDir(terrain, validCells, blockedState, playerCell, rawCode);
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
			// 独角仙国王等沿用了「长直线」字面 6；
			6 => 6,
			// 兜底：未知的正标号按其数值当作线段长度；
			_ => skillAreaCode < 1 ? 1 : skillAreaCode,
		};

	static bool IsWalkable(Godot.Collections.Dictionary validCells, Godot.Collections.Dictionary blockedState, Vector2I c)
	{
		string ck = HexGridUtil.CellKey(c);
		if (!validCells.ContainsKey(ck))
			return false;
		return !(blockedState.ContainsKey(ck) && blockedState[ck].AsBool());
	}

	static Vector2I PickRandomCenterAnywhere(Godot.Collections.Dictionary validCells,
		Godot.Collections.Dictionary blockedState)
	{
		List<Vector2I> candidates = GatherAllWalkable(validCells, blockedState);
		if (candidates.Count == 0)
			return default;

		return candidates[(int)(GD.Randi() % candidates.Count)];
	}

	static List<Vector2I> GatherAllWalkable(Godot.Collections.Dictionary validCells,
		Godot.Collections.Dictionary blockedState)
	{
		var list = new List<Vector2I>();

		foreach (Variant vk in validCells.Keys)
		{
			Vector2I c = HexGridUtil.ParseKey(vk.AsString());
			if (IsWalkable(validCells, blockedState, c))
				list.Add(c);
		}

		return list;
	}

	static HashSet<Vector2I> CellsAllWalkable(Godot.Collections.Dictionary validCells,
		Godot.Collections.Dictionary blockedState)
	{
		var set = new HashSet<Vector2I>();

		foreach (Variant vk in validCells.Keys)
		{
			Vector2I c = HexGridUtil.ParseKey(vk.AsString());
			if (IsWalkable(validCells, blockedState, c))
				set.Add(c);
		}

		return set;
	}

	static HashSet<Vector2I> CellsHexDisk(TileMapLayer terrain, Godot.Collections.Dictionary validCells,
		Godot.Collections.Dictionary blockedState, Vector2I center, int radius)
	{
		var set = new HashSet<Vector2I>();
		if (!IsWalkable(validCells, blockedState, center))
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
				if (!IsWalkable(validCells, blockedState, n))
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
		Godot.Collections.Dictionary blockedState, Vector2I startAnchor, TileSet.CellNeighbor dir, int segmentLengthCells,
		HashSet<Vector2I> sink)
	{
		sink.Clear();
		Vector2I cur = startAnchor;

		for (int i = 0; i < segmentLengthCells; i++)
		{
			if (!IsWalkable(validCells, blockedState, cur))
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

	/// <summary>
	/// 经过玩家格的六角轴向直线：在 6 个朝向中选取<strong>可走连续格数最多</strong>的一条（相同长度则随机），
	/// 避免随机轴在窄通道上两侧第一步都落空而退化成单格。
	/// </summary>
	static HashSet<Vector2I> CollectFullAxialDiameterThroughPlayer(TileMapLayer terrain,
		Godot.Collections.Dictionary validCells, Godot.Collections.Dictionary blockedState, Vector2I playerCell)
	{
		if (!IsWalkable(validCells, blockedState, playerCell))
			return [];

		int best = 0;
		var tied = new List<HashSet<Vector2I>>();

		for (int d = 0; d < 6; d++)
		{
			HashSet<Vector2I> line = BuildOneAxialDiameterLine(terrain, validCells, blockedState, playerCell,
				NeighborOrder[d]);
			int n = line.Count;
			if (n < 1)
				continue;
			if (n > best)
			{
				best = n;
				tied.Clear();
				tied.Add(line);
			}
			else if (n == best)
				tied.Add(line);
		}

		if (tied.Count == 0)
			return [playerCell];

		return tied[(int)(GD.Randi() % tied.Count)];
	}

	static HashSet<Vector2I> BuildOneAxialDiameterLine(TileMapLayer terrain, Godot.Collections.Dictionary validCells,
		Godot.Collections.Dictionary blockedState, Vector2I playerCell, TileSet.CellNeighbor forward)
	{
		var line = new HashSet<Vector2I>();

		Vector2I cur = InverseStep(terrain, playerCell, forward);
		while (IsWalkable(validCells, blockedState, cur))
		{
			line.Add(cur);
			cur = InverseStep(terrain, cur, forward);
		}

		line.Add(playerCell);

		cur = terrain.GetNeighborCell(playerCell, forward);
		while (IsWalkable(validCells, blockedState, cur))
		{
			line.Add(cur);
			cur = terrain.GetNeighborCell(cur, forward);
		}

		return line;
	}

	static HashSet<Vector2I> ResolveLineAnchoredAtPlayerRandomDir(TileMapLayer terrain,
		Godot.Collections.Dictionary validCells, Godot.Collections.Dictionary blockedState, Vector2I playerAnchor,
		int skillAreaCode)
	{
		int l = LineLengthCellsFromSkillAreaCode(skillAreaCode);

		var acc = new HashSet<Vector2I>();
		TileSet.CellNeighbor[] dirs = NeighborOrder;
		int startDir = (int)(GD.Randi() % 6);

		for (int t = 0; t < 6; t++)
		{
			int di = (startDir + t) % 6;

			if (TryBuildAxialRay(terrain, validCells, blockedState, playerAnchor, dirs[di], l, acc))
				return acc;
		}

		acc.Clear();
		if (IsWalkable(validCells, blockedState, playerAnchor))
			acc.Add(playerAnchor);

		return acc;
	}

	static HashSet<Vector2I> ResolveLineTargetingPlayerCoverage(TileMapLayer terrain,
		Godot.Collections.Dictionary validCells, Godot.Collections.Dictionary blockedState, Vector2I playerCell,
		int skillAreaCode, out Vector2I debugRayStartAnchor)
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

				if (!TryBuildAxialRay(terrain, validCells, blockedState, start, dir, l, probe))
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

			TryBuildAxialRay(terrain, validCells, blockedState, pickedStart, NeighborOrder[pickedDir], l, ray);

			return ray;
		}

		var fallback = new HashSet<Vector2I>();

		if (IsWalkable(validCells, blockedState, playerCell))
			fallback.Add(playerCell);

		return fallback;
	}
}
