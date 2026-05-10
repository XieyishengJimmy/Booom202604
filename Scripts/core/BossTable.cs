using System.Collections.Generic;
using Godot;

namespace Booom202604;

/// <summary>BOSS 表（由 excel/bosses.xlsx 经 Tools 导出为 Data/bosses.json）。</summary>
public static class BossTable
{
	public const string DefaultResourcePath = "res://Data/bosses.json";

	public sealed class Row
	{
		public required int Id;
		public required string Name;
		public int ChargeMeter;
		public int WarnMeter;
		public int GainPerTurn;
		public required string SkillDescription;
		/// <summary>策划配置的 AI / 目标选择意图说明；可与将来数据驱动 BOSS 行为对接。</summary>
		public required string AiDescription;
		public int SkillTarget;
		public int SkillArea;
		public int SkillEffect;
		public string SkillDetail = "";
		/// <summary>Excel「怪物ID配置」导出为 <c>summon_monster_ids</c>；BOSS 在迷雾中招怪时仅从该池中随机。</summary>
		public List<int> SummonMonsterIds = [];
	}

	static readonly Dictionary<int, Row> ById = [];

	public static int MeterTotal(Row row) =>
		Mathf.Max(0, row.WarnMeter) + Mathf.Max(0, row.ChargeMeter);

	public static bool TryGet(int id, out Row? row) => ById.TryGetValue(id, out row);

	public static IEnumerable<Row> EnumerateSorted()
	{
		var list = new List<Row>(ById.Values);
		list.Sort((a, b) => a.Id.CompareTo(b.Id));
		return list;
	}

	public static void Reload(string jsonPath)
	{
		ById.Clear();

		if (!Godot.FileAccess.FileExists(jsonPath))
		{
			GD.PushWarning($"BossTable: 缺失 {jsonPath}，请运行「dotnet run --project Tools/MonsterCsvToJson -- all」从 excel/bosses.xlsx 导出。");
			return;
		}

		using Godot.FileAccess f = Godot.FileAccess.Open(jsonPath, Godot.FileAccess.ModeFlags.Read);
		if (f == null)
		{
			GD.PushError($"BossTable: 无法读取 {jsonPath}");
			return;
		}

		Variant parsed = Json.ParseString(f.GetAsText());
		if (parsed.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError("BossTable: JSON 根应为对象。");
			return;
		}

		Godot.Collections.Dictionary root = parsed.AsGodotDictionary();
		if (!root.ContainsKey("bosses") || root["bosses"].VariantType != Variant.Type.Array)
		{
			GD.PushError("BossTable: 缺少 bosses 数组。");
			return;
		}

		foreach (Variant v in root["bosses"].AsGodotArray())
		{
			if (v.VariantType != Variant.Type.Dictionary)
				continue;
			Godot.Collections.Dictionary d = v.AsGodotDictionary();
			if (!TryReadId(d, "id", out int idNum) || idNum <= 0)
				continue;
			if (ById.ContainsKey(idNum))
			{
				GD.PushWarning($"BossTable: 重复 ID {idNum}，已跳过靠后的条目。");
				continue;
			}

			var row = new Row
			{
				Id = idNum,
				Name = GetStr(d, "name"),
				ChargeMeter = GetInt(d, "charge_meter", 0),
				WarnMeter = GetInt(d, "warn_meter", 0),
				GainPerTurn = GetInt(d, "gain_per_turn", 1),
				SkillDescription = GetStr(d, "skill_description"),
				AiDescription = GetStr(d, "ai_description"),
				SkillTarget = LooseInt(d, "skill_target", 0),
				SkillArea = LooseInt(d, "skill_area", 0),
				SkillEffect = LooseInt(d, "skill_effect", 0),
				SkillDetail = GetStr(d, "skill_detail"),
				SummonMonsterIds = ReadSummonMonsterIds(d),
			};
			if (row.GainPerTurn < 1)
				row.GainPerTurn = 1;
			if (MeterTotal(row) < 1)
			{
				row.WarnMeter = 50;
				row.ChargeMeter = 50;
			}

			ById[row.Id] = row;
		}

		GD.Print($"BossTable: 已加载 {ById.Count} 条 BOSS。");
	}

	static bool TryReadId(Godot.Collections.Dictionary d, string key, out int id)
	{
		id = 0;
		if (!d.TryGetValue(key, out Variant v))
			return false;
		switch (v.VariantType)
		{
			case Variant.Type.Int:
				id = v.AsInt32();
				return true;
			case Variant.Type.Float:
				id = (int)v.AsDouble();
				return true;
			case Variant.Type.String:
				return int.TryParse(v.AsString().Trim(), System.Globalization.NumberStyles.Integer,
					System.Globalization.CultureInfo.InvariantCulture, out id);
			default:
				return false;
		}
	}

	static string GetStr(Godot.Collections.Dictionary d, string key) =>
		d.TryGetValue(key, out Variant v) ? v.AsString() : "";

	static int GetInt(Godot.Collections.Dictionary d, string key, int def) =>
		d.TryGetValue(key, out Variant v) ? v.AsInt32() : def;

	/// <summary>Excel 导出数字常为 float/string，避免 AsInt32 断言失败。</summary>
	static int LooseInt(Godot.Collections.Dictionary d, string key, int def)
	{
		if (!d.TryGetValue(key, out Variant v))
			return def;

		return v.VariantType switch
		{
			Variant.Type.Int => v.AsInt32(),
			Variant.Type.Float => (int)v.AsDouble(),
			Variant.Type.String when int.TryParse(v.AsString().Trim(),
				System.Globalization.NumberStyles.Integer,
				System.Globalization.CultureInfo.InvariantCulture, out int p) =>
				p,
			_ => def,
		};
	}

	static List<int> ReadSummonMonsterIds(Godot.Collections.Dictionary d)
	{
		var ids = new List<int>();
		if (!d.TryGetValue("summon_monster_ids", out Variant v))
			return ids;
		if (v.VariantType != Variant.Type.Array)
			return ids;
		foreach (Variant item in v.AsGodotArray())
		{
			int nid = LooseIntFromVariantSummon(item);
			if (nid > 0)
				ids.Add(nid);
		}

		return ids;
	}

	static int LooseIntFromVariantSummon(Variant v) =>
		v.VariantType switch
		{
			Variant.Type.Int => v.AsInt32(),
			Variant.Type.Float => (int)v.AsDouble(),
			Variant.Type.String when int.TryParse(v.AsString().Trim(), System.Globalization.NumberStyles.Integer,
				System.Globalization.CultureInfo.InvariantCulture, out int p) => p,
			_ => 0,
		};
}
