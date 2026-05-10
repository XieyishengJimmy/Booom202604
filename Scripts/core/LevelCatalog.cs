using System;
using System.Collections.Generic;
using System.IO;
using Godot;

namespace Booom202604;

/// <summary>扫描 <c>res://levels</c> 下的关卡 JSON，供主菜单与关卡编辑器共用。</summary>
public static class LevelCatalog
{
	public const string ResourceDir = "res://levels";

	public static void EnsureDirectoryExists()
	{
		string abs = ProjectSettings.GlobalizePath(ResourceDir);
		if (!DirAccess.DirExistsAbsolute(abs))
			DirAccess.MakeDirRecursiveAbsolute(abs);
	}

	/// <summary>返回形如 <c>res://levels/foo.json</c> 的路径，已排序。</summary>
	public static List<string> EnumerateLevelJsonPathsSortedAscending()
	{
		EnsureDirectoryExists();
		List<string> paths = [];

		DirAccess? dir = DirAccess.Open(ResourceDir);

		if (dir == null)
			return paths;

		dir.ListDirBegin();

		string e;

		while ((e = dir.GetNext()) != "")
		{
			if (dir.CurrentIsDir())
				continue;
			if (!e.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
				continue;
			if (e.StartsWith("."))
				continue;
			paths.Add($"{ResourceDir}/{e}");
		}

		dir.ListDirEnd();
		paths.Sort(StringComparer.OrdinalIgnoreCase);

		return paths;
	}

	/// <summary>
	/// 主菜单与关卡编辑器下拉：先按 <see cref="CampaignOrderIndexKey"/> 升序；未配置或未参与闯关的关卡排在最后，其间按文件名排序。
	/// </summary>
	public static List<string> EnumerateLevelJsonPathsSortedByCampaignOrderThenStem()
	{
		List<string> paths = EnumerateLevelJsonPathsSortedAscending();
		var withOrder = new List<(string Path, int Order, string Stem)>();
		var withoutOrder = new List<(string Path, string Stem)>();

		foreach (string raw in paths)
		{
			string p = NormalizeResPath(raw);
			Godot.Collections.Dictionary d = LevelIo.LoadFromFile(p);

			if (d.Count == 0)
			{
				withoutOrder.Add((p, FileStemFromResPath(p)));
				continue;
			}

			int order = ReadCampaignOrderIndex(d);
			if (order == CampaignOrderUnset)
				withoutOrder.Add((p, FileStemFromResPath(p)));
			else
				withOrder.Add((p, order, FileStemFromResPath(p)));
		}

		withOrder.Sort((a, b) =>
		{
			int c = a.Order.CompareTo(b.Order);
			if (c != 0)
				return c;
			return string.Compare(a.Stem, b.Stem, StringComparison.OrdinalIgnoreCase);
		});

		withoutOrder.Sort((a, b) =>
			string.Compare(a.Stem, b.Stem, StringComparison.OrdinalIgnoreCase));

		var result = new List<string>(withOrder.Count + withoutOrder.Count);
		foreach (var row in withOrder)
			result.Add(row.Path);
		foreach (var row in withoutOrder)
			result.Add(row.Path);

		return result;
	}

	public static string FileStemFromResPath(string resPath)
	{
		if (string.IsNullOrEmpty(resPath))
			return "";

		try
		{
			return Path.GetFileNameWithoutExtension(ProjectSettings.GlobalizePath(resPath));
		}
		catch
		{
			return "";
		}
	}

	public static string GetDropdownLabel(string resPath)
	{
		Godot.Collections.Dictionary d = LevelIo.LoadFromFile(resPath);
		if (d.Count == 0)
			return FileStemFromResPath(resPath);

		string ln = "";
		if (d.TryGetValue("level_name", out Variant v) && v.VariantType == Variant.Type.String)
			ln = v.AsString().Trim();

		string stem = FileStemFromResPath(resPath);
		int ord = ReadCampaignOrderIndex(d);
		if (!string.IsNullOrEmpty(ln))
			return ord != CampaignOrderUnset ? $"{ln}  [{stem}] · 闯关#{ord}" : $"{ln}  [{stem}]";

		return ord != CampaignOrderUnset ? $"{stem} · 闯关#{ord}" : stem;
	}

	/// <summary>闯关顺序键：数值越小越早；必须唯一可在编辑器保存时校验。缺省不参与排序末尾。</summary>
	public const string CampaignOrderIndexKey = "campaign_order";

	/// <summary>缺省闯关序号（未写字段时使用，保证旧关卡不参与主线顺序）。</summary>
	public const int CampaignOrderUnset = int.MaxValue;

	/// <summary>闯关序号 ≥ 此值视为「实训/实力关」：不参与传送门<strong>主线</strong>跳转与通关庆祝（如 starter_level 的 999）。</summary>
	public const int PracticeCampaignOrderMinInclusive = 999;

	public static string NormalizeResPath(string? resPath)
	{
		if (string.IsNullOrWhiteSpace(resPath))
			return "";
		string t = resPath.Trim().Replace('\\', '/');
		if (!t.StartsWith("res://", StringComparison.Ordinal))
			t = $"{ResourceDir}/{t}";
		return t;
	}

	public static int ReadCampaignOrderIndex(Godot.Collections.Dictionary d)
	{
		if (!d.TryGetValue(CampaignOrderIndexKey, out Variant v))
			return CampaignOrderUnset;
		return v.VariantType switch
		{
			Variant.Type.Int => Mathf.Clamp(v.AsInt32(), 1, 999_999),
			Variant.Type.Float => Mathf.Clamp((int)v.AsDouble(), 1, 999_999),
			Variant.Type.String when int.TryParse(v.AsString().Trim(),
				System.Globalization.NumberStyles.Integer,
				System.Globalization.CultureInfo.InvariantCulture, out int parsed) =>
				Mathf.Clamp(parsed, 1, 999_999),
			_ => CampaignOrderUnset,
		};
	}

	/// <summary>按闯关序号升序的路径列表（仅含可读 JSON）；同序号按路径字典序次要排序。</summary>
	public static List<string> EnumerateCampaignLevelPathsOrdered()
	{
		List<string> paths = EnumerateLevelJsonPathsSortedAscending();
		var rows = new List<(string Path, int Order, string Stem)>();

		foreach (string raw in paths)
		{
			string p = NormalizeResPath(raw);
			Godot.Collections.Dictionary d = LevelIo.LoadFromFile(p);
			if (d.Count == 0)
				continue;

			int order = ReadCampaignOrderIndex(d);
			if (order == CampaignOrderUnset)
				continue;

			rows.Add((p, order, FileStemFromResPath(p)));
		}

		rows.Sort((a, b) =>
		{
			int c = a.Order.CompareTo(b.Order);
			if (c != 0)
				return c;
			return string.Compare(a.Stem, b.Stem, StringComparison.OrdinalIgnoreCase);
		});

		List<string> outList = [];

		HashSet<string> seen = [];

		foreach (var row in rows)
		{
			if (seen.Add(row.Path))
				outList.Add(row.Path);
		}

		return outList;
	}

	/// <summary>主线闯关路径（剔除未配置序号与「实力/实训」序号）；排序规则同 <see cref="EnumerateCampaignLevelPathsOrdered"/>。</summary>
	public static List<string> EnumerateMainCampaignPathsOrdered()
	{
		List<string> paths = EnumerateLevelJsonPathsSortedAscending();
		var rows = new List<(string Path, int Order, string Stem)>();

		foreach (string raw in paths)
		{
			string p = NormalizeResPath(raw);
			Godot.Collections.Dictionary d = LevelIo.LoadFromFile(p);
			if (d.Count == 0)
				continue;

			int order = ReadCampaignOrderIndex(d);
			if (order == CampaignOrderUnset || order >= PracticeCampaignOrderMinInclusive)
				continue;

			rows.Add((p, order, FileStemFromResPath(p)));
		}

		rows.Sort((a, b) =>
		{
			int c = a.Order.CompareTo(b.Order);
			if (c != 0)
				return c;
			return string.Compare(a.Stem, b.Stem, StringComparison.OrdinalIgnoreCase);
		});

		List<string> outMain = [];

		HashSet<string> seen = [];

		foreach (var row in rows)
		{
			if (seen.Add(row.Path))
				outMain.Add(row.Path);
		}

		return outMain;
	}

	/// <summary>主线中当前关之后的下一关；无主线下一关或未处于主线序列时返回 <c>null</c>。</summary>
	public static string? ResolveNextMainCampaignLevelPath(string currentLevelResPath)
	{
		List<string> ordered = EnumerateMainCampaignPathsOrdered();

		string curPath = NormalizeResPath(currentLevelResPath);
		if (string.IsNullOrEmpty(curPath) || ordered.Count == 0)
			return null;

		int ix = IndexOfNormalizedPath(ordered, curPath);
		if (ix < 0)
			return null;

		int nextIx = ix + 1;

		return nextIx < ordered.Count ? ordered[nextIx] : null;
	}

	/// <summary>是否为当前主线排序中的<strong>最后一关</strong>（通关传送门且无下一主线关时弹出庆祝界面）。</summary>
	public static bool IsTerminalMainCampaignLevel(string completedLevelResPath)
	{
		List<string> main = EnumerateMainCampaignPathsOrdered();
		if (main.Count == 0)
			return false;

		string cur = NormalizeResPath(completedLevelResPath);
		string last = NormalizeResPath(main[^1]);

		return string.Equals(cur, last, StringComparison.OrdinalIgnoreCase);
	}

	static int IndexOfNormalizedPath(List<string> ordered, string currentNormalized)
	{
		string cur = NormalizeResPath(currentNormalized);
		for (int i = 0; i < ordered.Count; i++)
			if (string.Equals(NormalizeResPath(ordered[i]), cur, StringComparison.OrdinalIgnoreCase))
				return i;
		return -1;
	}

	/// <summary>当前关卡之后按闯关序号排定的下一关 <c>res://</c> 路径；无则返回 <c>null</c>。</summary>
	public static string? ResolveNextCampaignLevelPath(string currentLevelResPath)
	{
		List<string> ordered = EnumerateCampaignLevelPathsOrdered();

		string curPath = NormalizeResPath(currentLevelResPath);
		if (string.IsNullOrEmpty(curPath) || ordered.Count == 0)
			return null;

		int ix = IndexOfNormalizedPath(ordered, curPath);
		if (ix < 0)
			return null;

		int nextIx = ix + 1;

		return nextIx < ordered.Count ? ordered[nextIx] : null;
	}

	/// <summary>枚举其他关卡中与 <paramref name="candidateOrder"/> 冲突的路径（不包含 <paramref name="excludeResPath"/>）。</summary>
	public static List<string> FindDuplicateCampaignOrderConflictsElsewhere(string excludeResPath, int candidateOrder)
	{
		List<string> paths = EnumerateLevelJsonPathsSortedAscending();
		var conflicts = new List<string>();

		string excl = NormalizeResPath(excludeResPath);

		foreach (string raw in paths)
		{
			string p = NormalizeResPath(raw);
			if (string.Equals(p, excl, StringComparison.OrdinalIgnoreCase))
				continue;

			Godot.Collections.Dictionary d = LevelIo.LoadFromFile(p);
			if (d.Count == 0)
				continue;

			if (ReadCampaignOrderIndex(d) == candidateOrder)
				conflicts.Add(p);

		}

		return conflicts;
	}
}
