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
		if (!string.IsNullOrEmpty(ln))
			return $"{ln}  [{stem}]";

		return stem;
	}
}
