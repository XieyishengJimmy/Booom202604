using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using ClosedXML.Excel;

static string? FindProjectDirectory()
{
	string? d = AppContext.BaseDirectory;
	for (int depth = 0; depth < 12 && !string.IsNullOrEmpty(d); depth++)
	{
		if (File.Exists(Path.Combine(d!, "BOOOM202604.csproj")))
			return d;
		d = Directory.GetParent(d)?.FullName;
	}

	d = Directory.GetCurrentDirectory();
	for (int depth = 0; depth < 12 && !string.IsNullOrEmpty(d); depth++)
	{
		if (File.Exists(Path.Combine(d!, "BOOOM202604.csproj")))
			return d;
		d = Directory.GetParent(d)?.FullName;
	}

	return null;
}

/// <summary>首行标题 → 列号（从 1 开始）。兼容 BOM、空格。</summary>
static Dictionary<string, int> ReadHeaderColumnMap(IXLRow headerRow)
{
	var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
	for (int c = 1; c <= 64; c++)
	{
		string raw = headerRow.Cell(c).GetString()?.Trim() ?? "";
		if (string.IsNullOrEmpty(raw))
			continue;
		raw = raw.TrimStart('\ufeff'); // BOM
		if (!map.ContainsKey(raw))
			map[raw] = c;
	}

	return map;
}

static string GetCellTrim(IXLWorksheet ws, int rowNum, Dictionary<string, int> map, string header)
{
	if (!map.TryGetValue(header, out int col))
		return "";
	IXLCell cell = ws.Cell(rowNum, col);
	if (cell.IsEmpty())
		return "";
	string s = cell.GetFormattedString()?.Trim() ?? "";
	if (!string.IsNullOrEmpty(s))
		return s;
	// 避免 Convert.ToString(cell.Value) 经 IConvertible / 隐式 DateTime 等路径抛出
	return cell.Value.ToString(CultureInfo.InvariantCulture)?.Trim() ?? "";
}

/// <summary>长表头（单元格内含换行的说明文字）时用首行作为主键别名，便于 Required 校验与读取。</summary>
static void AddFirstLineHeaderAliases(Dictionary<string, int> map)
{
	foreach ((string key, int col) in map.ToList())
	{
		int cut = key.IndexOfAny(['\r', '\n']);
		if (cut <= 0)
			continue;
		string first = key[..cut].Trim();
		if (first.Length == 0 || map.ContainsKey(first))
			continue;
		map[first] = col;
	}
}

/// <summary>BOSS 表中数字枚举格；空白或非整数为 0。</summary>
static int GetBossEnumIntCell(IXLWorksheet ws, int rowNum, Dictionary<string, int> cmap, string headerKey)
{
	if (!cmap.TryGetValue(headerKey, out int col))
		return 0;
	IXLCell cell = ws.Cell(rowNum, col);
	if (cell.IsEmpty())
		return 0;
	if (cell.TryGetValue(out double dNum) && Math.Abs(dNum - Math.Floor(dNum)) < 1e-9 && dNum >= 0 &&
	    dNum <= int.MaxValue)
		return (int)Math.Floor(dNum);
	string s = GetCellTrim(ws, rowNum, cmap, headerKey);
	return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : 0;
}

/// <summary>技能具体效果列：整格为 1～99 的整数时记入枚举；否则整块视为自由文案。</summary>
static void ParseBossSkillEffectCell(IXLWorksheet ws, int rowNum, Dictionary<string, int> cmap,
	string headerKey, out int effectEnum, out string detailText)
{
	effectEnum = 0;
	detailText = "";
	if (!cmap.TryGetValue(headerKey, out _))
		return;
	string raw = GetCellTrim(ws, rowNum, cmap, headerKey);
	if (string.IsNullOrWhiteSpace(raw))
		return;
	raw = raw.Trim();
	if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int k) &&
	    k is >= 1 and <= 99)
		effectEnum = k;
	else
		detailText = raw;
}

static string BossSkillAiHint(int skillTarget) => skillTarget switch
{
	1 => "AI：中心锁定玩家所在格。",
	2 => "AI：范围仅需包含玩家所在格即可。",
	3 => "AI：可于全地图任意位置施放。",
	_ => "",
};

static string BossFallbackSkillSummary(int skillTarget, int skillArea, int skillEffect, string skillDetail)
{
	if (!string.IsNullOrWhiteSpace(skillDetail))
		return skillDetail;
	if (skillTarget == 0 && skillArea == 0 && skillEffect == 0)
		return "";
	return $"BOSS 技能枚举：定位 {skillTarget} · 范围 {skillArea} · 效果 {(skillEffect == 0 ? "—" : skillEffect.ToString(CultureInfo.InvariantCulture))}";
}

static bool TryParsePositiveId(IXLWorksheet ws, int rowNum, Dictionary<string, int> cmap, string header, out int id)
{
	id = 0;
	string txt = GetCellTrim(ws, rowNum, cmap, header);
	if (string.IsNullOrWhiteSpace(txt) || txt.StartsWith('#'))
		return false;
	if (int.TryParse(txt.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out id) &&
	    id > 0)
		return true;

	if (!cmap.TryGetValue(header, out int col))
		return false;

	IXLCell cell = ws.Cell(rowNum, col);
	if (cell.IsEmpty())
		return false;

	if (!cell.TryGetValue(out double dNum) || dNum <= 0 || dNum > int.MaxValue)
		return false;

	if (Math.Abs(dNum - Math.Floor(dNum)) >= 1e-9)
		return false;

	id = (int)Math.Floor(dNum);
	return id > 0;
}

static int RunMonsters(string rootDir)
{
	string xlPath = Path.Combine(rootDir, "excel", "monsters.xlsx");
	string jsonPath = Path.Combine(rootDir, "Data", "monsters.json");

	if (!File.Exists(xlPath))
	{
		Console.Error.WriteLine($"缺少工作簿：{xlPath}（可把旧 CSV 放 excel 目录后运行「dotnet run --project Tools/MonsterCsvToJson migrate」迁移）");
		return 1;
	}

	Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);

	using var workbook = new XLWorkbook(xlPath);
	var ws = workbook.Worksheet(1);
	if (ws.FirstCellUsed() == null || ws.LastRowUsed() == null)
	{
		Console.Error.WriteLine($"monsters 工作簿无数据：{xlPath}");
		return 1;
	}

	const int hr = 1;
	Dictionary<string, int> cmap = ReadHeaderColumnMap(ws.Row(hr));
	string[] req = ["ID", "怪物名", "怪物描述", "怪物属性", "怪物战斗力", "怪物图片路径"];
	foreach (string r in req)
	{
		if (!cmap.ContainsKey(r))
		{
			Console.Error.WriteLine($"monsters 表缺少列「{r}」。首行必须为中文列名（与模板一致）。");
			return 1;
		}
	}

	int lastRow = ws.LastRowUsed()!.RowNumber();
	var arr = new JsonArray();
	var idsUsed = new HashSet<int>();
	for (int r = hr + 1; r <= lastRow; r++)
	{
		if (!TryParsePositiveId(ws, r, cmap, "ID", out int idNum))
			continue;

		if (!idsUsed.Add(idNum))
		{
			Console.Error.WriteLine($"monsters 第 {r} 行重复 ID「{idNum}」，已跳过。");
			continue;
		}

		string name = GetCellTrim(ws, r, cmap, "怪物名");
		string desc = GetCellTrim(ws, r, cmap, "怪物描述");
		string kindZh = GetCellTrim(ws, r, cmap, "怪物属性");
		string pw = GetCellTrim(ws, r, cmap, "怪物战斗力");
		if (!int.TryParse(pw, out int power))
			power = 1;
		string icon = GetCellTrim(ws, r, cmap, "怪物图片路径");

		var o = new JsonObject
		{
			["id"] = idNum,
			["name"] = name,
			["description"] = desc,
			["kind"] = MonsterKindToJsonToken(kindZh),
			["power"] = power,
			["icon"] = icon,
		};
		arr.Add(o);
	}

	var doc = new JsonObject
	{
		["version"] = 2,
		["monsters"] = arr,
	};
	var opt = new System.Text.Json.JsonSerializerOptions
	{
		WriteIndented = true,
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};
	File.WriteAllText(jsonPath, doc.ToJsonString(opt), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
	Console.WriteLine($"已导出 {arr.Count} 条怪物 → {jsonPath}");
	return 0;
}

static int RunBosses(string rootDir)
{
	string xlPath = Path.Combine(rootDir, "excel", "bosses.xlsx");
	string jsonPath = Path.Combine(rootDir, "Data", "bosses.json");

	if (!File.Exists(xlPath))
	{
		Console.Error.WriteLine($"缺少工作簿：{xlPath}");
		return 1;
	}

	Directory.CreateDirectory(Path.GetDirectoryName(jsonPath)!);

	using var workbook = new XLWorkbook(xlPath);
	var ws = workbook.Worksheet(1);
	if (ws.FirstCellUsed() == null || ws.LastRowUsed() == null)
	{
		Console.Error.WriteLine($"bosses 工作簿无数据：{xlPath}");
		return 1;
	}

	const int hr = 1;
	Dictionary<string, int> cmap = ReadHeaderColumnMap(ws.Row(hr));
	AddFirstLineHeaderAliases(cmap);
	string[] req =
	[
		"ID", "BOSS名", "蓄力行动条", "预警行动条", "每回合增长",
		BossExcelHeaders.SkillTarget, BossExcelHeaders.SkillArea, BossExcelHeaders.SkillEffect,
	];
	foreach (string x in req)
	{
		if (!cmap.ContainsKey(x))
		{
			Console.Error.WriteLine(
				$"BOSS 表缺少列「{x}」（若表头为带换行长文案，请保证首行含此短标题）。请与 excel 模板首行一致。");
			return 1;
		}
	}

	int lastRow = ws.LastRowUsed()!.RowNumber();
	var arr = new JsonArray();
	var idsUsed = new HashSet<int>();
	for (int r = hr + 1; r <= lastRow; r++)
	{
		if (!TryParsePositiveId(ws, r, cmap, "ID", out int idNum))
			continue;

		if (!idsUsed.Add(idNum))
		{
			Console.Error.WriteLine($"bosses 第 {r} 行重复 ID「{idNum}」，已跳过。");
			continue;
		}

		string name = GetCellTrim(ws, r, cmap, "BOSS名");
		if (!int.TryParse(GetCellTrim(ws, r, cmap, "蓄力行动条"), out int charge))
			charge = 1;
		if (!int.TryParse(GetCellTrim(ws, r, cmap, "预警行动条"), out int warn))
			warn = 1;
		if (!int.TryParse(GetCellTrim(ws, r, cmap, "每回合增长"), out int gain))
			gain = 22;

		int skillTarget = GetBossEnumIntCell(ws, r, cmap, BossExcelHeaders.SkillTarget);
		int skillArea = GetBossEnumIntCell(ws, r, cmap, BossExcelHeaders.SkillArea);
		ParseBossSkillEffectCell(ws, r, cmap, BossExcelHeaders.SkillEffect, out int skillEffect,
			out string skillDetail);

		string skillDesc = BossFallbackSkillSummary(skillTarget, skillArea, skillEffect, skillDetail);
		string aiDesc = BossSkillAiHint(skillTarget);

		var o = new JsonObject
		{
			["id"] = idNum,
			["name"] = name,
			["charge_meter"] = charge,
			["warn_meter"] = warn,
			["gain_per_turn"] = gain,
			["skill_target"] = skillTarget,
			["skill_area"] = skillArea,
			["skill_effect"] = skillEffect,
			["skill_detail"] = skillDetail,
			["skill_description"] = skillDesc,
			["ai_description"] = aiDesc,
		};
		arr.Add(o);
	}

	var doc = new JsonObject
	{
		["version"] = 3,
		["bosses"] = arr,
	};
	var opt = new System.Text.Json.JsonSerializerOptions
	{
		WriteIndented = true,
		Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
	};
	File.WriteAllText(jsonPath, doc.ToJsonString(opt), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
	Console.WriteLine($"已导出 {arr.Count} 条 BOSS → {jsonPath}");
	return 0;
}

static string MonsterKindToJsonToken(string raw)
{
	string z = raw.Trim();
	if (z.Contains('魔') || z.Contains("精神"))
		return "魔力";
	if (z.Contains("力量") || z.Contains('蛮'))
		return "力量";
	string s = z.ToLowerInvariant();
	if (s is "mag" or "magic" or "spirit")
		return "魔力";
	return "力量";
}

static List<string> ParseCsvRecord(string line)
{
	var fields = new List<string>();
	var cur = new System.Text.StringBuilder();
	bool q = false;
	for (int i = 0; i < line.Length; i++)
	{
		char c = line[i];
		if (q)
		{
			if (c == '"')
			{
				if (i + 1 < line.Length && line[i + 1] == '"')
				{
					cur.Append('"');
					i++;
				}
				else q = false;
			}
			else cur.Append(c);
		}
		else
		{
			if (c == '"') q = true;
			else if (c == ',') { fields.Add(cur.ToString()); cur.Clear(); }
			else cur.Append(c);
		}
	}
	fields.Add(cur.ToString());
	return fields;
}

static void XlsxWriteMonsterRowFromCsvFields(IXLWorksheet ws, int rowIdx, List<string> cols)
{
	for (int c = 0; c < cols.Count && c < 6; c++)
		ws.Cell(rowIdx, c + 1).SetValue(cols[c]?.Trim() ?? "");
}

/// <summary>从 UTF-8 CSV 生成同名 xlsx（首行仍为列标题），用于迁移旧工程。</summary>
static int MigrateCsvToXlsx(string rootDir)
{
	string monstersCsv = Path.Combine(rootDir, "excel", "monsters.csv");
	string monstersXl = Path.Combine(rootDir, "excel", "monsters.xlsx");
	if (File.Exists(monstersCsv))
	{
		using var wb = new XLWorkbook();
		var ws = wb.AddWorksheet("monsters");
		int r = 0;
		using (var sr = new StreamReader(File.OpenRead(monstersCsv), new UTF8Encoding(false)))
		{
			string? ln;
			while ((ln = sr.ReadLine()) != null)
			{
				if (string.IsNullOrWhiteSpace(ln))
					continue;
				r++;
				List<string> cols = ParseCsvRecord(ln.TrimEnd('\r'));
				for (int c = 0; c < cols.Count; c++)
					ws.Cell(r, c + 1).SetValue(cols[c]);
			}
		}
		wb.SaveAs(monstersXl);
		Console.WriteLine($"已迁移 → {monstersXl}");
	}
	else Console.WriteLine($"未找到 monsters.csv（跳过 monsters 迁移）：{monstersCsv}");

	string bossesCsv = Path.Combine(rootDir, "excel", "bosses.csv");
	string bossesXl = Path.Combine(rootDir, "excel", "bosses.xlsx");
	if (File.Exists(bossesCsv))
	{
		using var wb = new XLWorkbook();
		var ws = wb.AddWorksheet("bosses");
		int r = 0;
		using (var sr = new StreamReader(File.OpenRead(bossesCsv), new UTF8Encoding(false)))
		{
			string? ln;
			while ((ln = sr.ReadLine()) != null)
			{
				if (string.IsNullOrWhiteSpace(ln))
					continue;
				r++;
				List<string> cols = ParseCsvRecord(ln.TrimEnd('\r'));
				for (int c = 0; c < cols.Count; c++)
					ws.Cell(r, c + 1).SetValue(cols[c]);
			}
		}
		wb.SaveAs(bossesXl);
		Console.WriteLine($"已迁移 → {bossesXl}");
	}
	else Console.WriteLine($"未找到 bosses.csv（跳过 bosses 迁移）：{bossesCsv}");

	return 0;
}

static int EnsureStarterXlsx(string rootDir, bool forceOverwrite)
{
	Directory.CreateDirectory(Path.Combine(rootDir, "excel"));
	string mx = Path.Combine(rootDir, "excel", "monsters.xlsx");
	if (forceOverwrite || !File.Exists(mx))
	{
		using var wb = new XLWorkbook();
		var ws = wb.AddWorksheet("monsters");
		ws.Cell(1, 1).Value = "ID";
		ws.Cell(1, 2).Value = "怪物名";
		ws.Cell(1, 3).Value = "怪物描述";
		ws.Cell(1, 4).Value = "怪物属性";
		ws.Cell(1, 5).Value = "怪物战斗力";
		ws.Cell(1, 6).Value = "怪物图片路径";
		XlsxWriteMonsterRowFromCsvFields(ws, 2, ["1", "蛮力鼠", "穴居杂兵靠蛮力扑咬。", "力量", "2", "res://Art/Icon/monster.png"]);
		XlsxWriteMonsterRowFromCsvFields(ws, 3, ["2", "巨骸", "废墟里晃动的巨大骨架力量惊人。", "力量", "6", "res://Art/Icon/ruins.png"]);
		XlsxWriteMonsterRowFromCsvFields(ws, 4, ["3", "残响", "无声徘徊的残影偏精神干扰。", "魔力", "4", "res://Art/Icon/corpse.png"]);
		XlsxWriteMonsterRowFromCsvFields(ws, 5, ["4", "魔晶孢", "结晶孢子用魔力构型攻击。", "魔力", "5", "res://Art/Icon/treasure.png"]);
		wb.SaveAs(mx);
		Console.WriteLine(forceOverwrite ? $"已覆盖怪物表模板：{mx}" : $"已生成模板：{mx}");
	}

	string bx = Path.Combine(rootDir, "excel", "bosses.xlsx");
	if (forceOverwrite || !File.Exists(bx))
	{
		using var wb = new XLWorkbook();
		var ws = wb.AddWorksheet("bosses");
		ws.Cell(1, 1).Value = "ID";
		ws.Cell(1, 2).Value = "BOSS名";
		ws.Cell(1, 3).Value = "蓄力行动条";
		ws.Cell(1, 4).Value = "预警行动条";
		ws.Cell(1, 5).Value = "每回合增长";
		ws.Cell(1, 6).Value = BossExcelHeaders.SkillTarget;
		ws.Cell(1, 7).Value = BossExcelHeaders.SkillArea;
		ws.Cell(1, 8).Value = BossExcelHeaders.SkillEffect;
		ws.Cell(2, 1).Value = 1;
		ws.Cell(2, 2).Value = "雾牢典狱（演示）";
		ws.Cell(2, 3).Value = 50;
		ws.Cell(2, 4).Value = 50;
		ws.Cell(2, 5).Value = 22;
		ws.Cell(2, 6).Value = 1;
		ws.Cell(2, 7).Value = 1;
		ws.Cell(2, 8).Value = 1;
		wb.SaveAs(bx);
		Console.WriteLine(forceOverwrite ? $"已覆盖 BOSS 表模板：{bx}" : $"已生成模板：{bx}");
	}

	return 0;
}

/// <summary>导出 <c>excel</c> 目录下已注册的表。新增表：在本函数内 <c>tableExporters</c> 追加 <c>(Stem, RunXxx)</c> 并实现 Run。</summary>
static int ExportAllWorkbooks(string rootDir)
{
	var tableExporters = new (string Stem, Func<string, int> Run)[]
	{
		("monsters", RunMonsters),
		("bosses", RunBosses),
	};

	string excelDir = Path.Combine(rootDir, "excel");
	if (!Directory.Exists(excelDir))
	{
		Console.Error.WriteLine($"缺少目录：{excelDir}");
		return 1;
	}

	HashSet<string> registered = new(tableExporters.Select(t => t.Stem), StringComparer.OrdinalIgnoreCase);

	foreach (string fp in Directory.EnumerateFiles(excelDir, "*.xlsx", SearchOption.TopDirectoryOnly))
	{
		string fn = Path.GetFileName(fp);
		if (fn.StartsWith("~$", StringComparison.Ordinal))
			continue;
		string stem = Path.GetFileNameWithoutExtension(fn);
		if (!registered.Contains(stem))
		{
			Console.WriteLine(
				$"提示：excel/{fn} 尚未配置导出器。请在 Tools/MonsterCsvToJson/Program.cs → ExportAllWorkbooks 内 tableExporters 中为「{stem}」追加映射并实现 Run 函数。");
		}
	}

	int exit = 0;
	foreach ((string stem, Func<string, int> run) in tableExporters)
	{
		string xl = Path.Combine(excelDir, stem + ".xlsx");
		if (!File.Exists(xl))
		{
			Console.WriteLine($"跳过（文件不存在）：{stem}.xlsx");
			continue;
		}

		Console.WriteLine($"--- 导出 {stem}.xlsx ---");
		int c = run(rootDir);
		if (c != 0)
			exit = c;
	}

	if (exit == 0)
		Console.WriteLine("export-all：全部成功。");

	return exit;
}

// --- main ---
var argv = Environment.GetCommandLineArgs();
bool forceStarter = argv.Skip(1).Any(a =>
	string.Equals(a, "--force", StringComparison.OrdinalIgnoreCase));
string cmd = argv
	.Skip(1)
	.FirstOrDefault(a => !string.IsNullOrWhiteSpace(a) &&
		a.Length > 0 &&
		a[0] != '-' &&
		!string.Equals(a, "--force", StringComparison.OrdinalIgnoreCase) &&
		!a.Equals("dotnet", StringComparison.OrdinalIgnoreCase) &&
		!a.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
		!a.Equals("MonsterCsvToJson.csproj", StringComparison.OrdinalIgnoreCase))
	?.Trim()
	.ToLowerInvariant() ?? "all";

var rootDir = FindProjectDirectory();
if (rootDir == null)
{
	Console.Error.WriteLine("找不到 BOOOM202604.csproj（请在 Booom202604 根目录执行 dotnet run）。");
	return 1;
}

switch (cmd)
{
	case "migrate":
		MigrateCsvToXlsx(rootDir);
		Console.WriteLine("迁移完成（若仍存在 .csv.import，可在 Godot 中删除对已删除 CSV 的引用）。");
		return 0;
	case "templates":
	case "template":
	case "init":
		EnsureStarterXlsx(rootDir, forceStarter);
		return 0;
	case "monsters":
		return RunMonsters(rootDir);
	case "bosses":
	case "boss":
		return RunBosses(rootDir);
	case "export-all":
	case "exportall":
		return ExportAllWorkbooks(rootDir);
	case "all":
	default:
		EnsureStarterXlsx(rootDir, false);
		int a = RunMonsters(rootDir);
		int b = RunBosses(rootDir);
		return a != 0 ? a : b;
}

static class BossExcelHeaders
{
	internal const string SkillTarget = "BOSS技能定位（使用位置）";
	internal const string SkillArea = "BOSS技能范围定义（生效范围）";
	internal const string SkillEffect = "技能具体效果";
}
