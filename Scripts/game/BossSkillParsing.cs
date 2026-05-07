using System.Text.RegularExpressions;

namespace Booom202604;

/// <summary>从 BOSS 表 skill_detail / skill_description 推断「迷雾 + 刷怪」技能参数。</summary>
public static class BossSkillParsing
{
	public struct FogMonsterSpec
	{
		/// <summary>要刷出的战斗怪物事件数量。</summary>
		public int MonsterSpawnCount;

		/// <summary>
		/// null：预警锁定的<strong>全部</strong>格铺迷雾；
		/// 正数：仅从锁定格子中<strong>随机挑这么多个</strong>铺迷雾。
		/// </summary>
		public int? FogRandomSubsetFromLocked;
	}

	static readonly Regex RxFogRandomCells = new(@"随机\s*(\d+)\s*个格子", RegexOptions.Compiled);
	static readonly Regex RxMonsterRefresh = new(@"刷新\s*(\d+)\s*个怪物", RegexOptions.Compiled);
	static readonly Regex RxMonsterGenerateTail = new(@"生成\s*(\d+)\s*个怪物", RegexOptions.Compiled);

	/// <summary>若表中描述为迷雾类且可解析刷怪数量，返回 true。</summary>
	public static bool TryParseFogMonsterSkill(string detail, string description, int bossTableId,
		out FogMonsterSpec spec)
	{
		spec = default;

		string blob = $"{detail}\n{description}";
		if (!(blob.Contains("雾") || blob.Contains("迷雾")))
			return false;

		bool fogAllTilesInLocked = blob.Contains("所有格子");

		int? subset = null;

		if (!fogAllTilesInLocked)
		{
			Match mFog = RxFogRandomCells.Match(blob);
			if (mFog.Success && int.TryParse(mFog.Groups[1].Value, out int fk) && fk > 0)
				subset = fk;
		}

		int monsters = 0;
		Match mMonster = RxMonsterRefresh.Match(blob);
		if (!(mMonster.Success && int.TryParse(mMonster.Groups[1].Value, out monsters) && monsters > 0))
		{
			mMonster = RxMonsterGenerateTail.Match(blob);
			if (mMonster.Success && int.TryParse(mMonster.Groups[1].Value, out int ng) && ng > 0)
				monsters = ng;
		}

		if (monsters <= 0)
			monsters = FallbackMonsterCount(bossTableId);

		if (monsters <= 0)
			return false;

		spec = new FogMonsterSpec
		{
			MonsterSpawnCount = monsters,
			FogRandomSubsetFromLocked = subset,
		};

		return true;
	}

	static int FallbackMonsterCount(int bossTableId)
	{
		return bossTableId switch
		{
			1001 => 1,
			1002 => 2,
			1003 => 2,
			1004 => 3,
			1005 => 4,
			1006 => 4,
			_ => 0,
		};
	}
}
