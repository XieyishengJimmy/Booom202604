using System.Collections.Generic;
using Godot;

namespace Booom202604;

/// <summary>怪物表（由 excel/monsters.xlsx 经 Tools 导出为 Data/monsters.json）。</summary>
public static class MonsterTable
{
	public const string DefaultResourcePath = "res://Data/monsters.json";

	public sealed class Row
	{
		public required int Id;
		public required string Name;
		public required string Description;
		public bool IsMagic;
		public int Power;
		public required string IconPath;
	}

	static readonly Dictionary<int, Row> ById = new();
	static readonly List<int> BossSummonMonsterIds = [];
	static readonly List<Row> BossSummonCandidateRows = [];
	static readonly List<Row> Order = [];

	public static IReadOnlyList<Row> All => Order;

	/// <summary>BOSS 刷新的战斗怪仅从该池中随机（来自 monsters.json · boss_summon_monster_ids）。空则等价于<code>All</code>。</summary>
	public static IReadOnlyList<Row> BossSummonCandidates =>
		BossSummonCandidateRows.Count > 0 ? BossSummonCandidateRows : Order;

	public static Row? PickBossSummonMonsterRow()
	{
		IReadOnlyList<Row> src = BossSummonCandidates;
		return src.Count == 0 ? null : src[System.Random.Shared.Next(src.Count)];
	}

	public static void Reload(string jsonPath)
	{
		ById.Clear();
		Order.Clear();
		BossSummonMonsterIds.Clear();
		BossSummonCandidateRows.Clear();

		if (!Godot.FileAccess.FileExists(jsonPath))
		{
			GD.PushWarning($"MonsterTable: 缺失 {jsonPath}，请运行「dotnet run --project Tools/MonsterCsvToJson -- all」从 excel/monsters.xlsx 导出。");
			return;
		}

		using Godot.FileAccess f = Godot.FileAccess.Open(jsonPath, Godot.FileAccess.ModeFlags.Read);
		if (f == null)
		{
			GD.PushError($"MonsterTable: 无法读取 {jsonPath}");
			return;
		}

		string text = f.GetAsText();
		Variant parsed = Json.ParseString(text);
		if (parsed.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError("MonsterTable: JSON 格式错误（根应为对象）。");
			return;
		}

		Godot.Collections.Dictionary root = parsed.AsGodotDictionary();
		if (!root.ContainsKey("monsters") || root["monsters"].VariantType != Variant.Type.Array)
		{
			GD.PushError("MonsterTable: 缺少 monsters 数组。");
			return;
		}

		Godot.Collections.Array arr = root["monsters"].AsGodotArray();
		foreach (Variant v in arr)
		{
			if (v.VariantType != Variant.Type.Dictionary)
				continue;
			Godot.Collections.Dictionary d = v.AsGodotDictionary();
			if (!TryReadId(d, "id", out int idNum) || idNum <= 0)
				continue;
			if (ById.ContainsKey(idNum))
			{
				GD.PushWarning($"MonsterTable: 重复 ID {idNum}，已跳过靠后的条目。");
				continue;
			}

			var row = new Row
			{
				Id = idNum,
				Name = GetStr(d, "name"),
				Description = GetStr(d, "description"),
				IsMagic = ParseKind(GetStr(d, "kind")),
				Power = GetInt(d, "power", 1),
				IconPath = GetStr(d, "icon"),
			};
			if (row.Power < 1)
				row.Power = 1;
			if (row.Power > 99)
				row.Power = 99;
			ById[row.Id] = row;
			Order.Add(row);
		}

		if (root.TryGetValue("boss_summon_monster_ids", out Variant poolVar) &&
		    poolVar.VariantType == Variant.Type.Array)
		{
			foreach (Variant it in poolVar.AsGodotArray())
			{
				int nid = LooseIntFromVariant(it);
				if (nid <= 0 || BossSummonMonsterIds.Contains(nid))
					continue;
				BossSummonMonsterIds.Add(nid);
			}

			foreach (int pid in BossSummonMonsterIds)
			{
				if (TryGet(pid, out Row? pr) && pr != null)
					BossSummonCandidateRows.Add(pr);
				else
					GD.PushWarning($"MonsterTable: boss_summon_monster_ids 中 ID「{pid}」不存在于 monsters 列表，已忽略。");
			}

			if (BossSummonMonsterIds.Count > 0 && BossSummonCandidateRows.Count == 0)
				GD.PushWarning("MonsterTable: boss_summon_monster_ids 均未解析到合法怪物行，BOSS 刷怪退化为 monsters 全员随机。");
		}

		GD.Print($"MonsterTable: 已加载 {Order.Count} 条怪物；BOSS刷怪池 {BossSummonMonsterIds.Count} 个引用 ID → 可选 {BossSummonCandidateRows.Count} 行。");
	}

	static string GetStr(Godot.Collections.Dictionary d, string key) =>
		d.TryGetValue(key, out Variant v) ? v.AsString() : "";

	static int GetInt(Godot.Collections.Dictionary d, string key, int def) =>
		d.TryGetValue(key, out Variant v) ? v.AsInt32() : def;

	static int LooseIntFromVariant(Variant v) =>
		v.VariantType switch
		{
			Variant.Type.Int => v.AsInt32(),
			Variant.Type.Float => (int)v.AsDouble(),
			Variant.Type.String when int.TryParse(v.AsString().Trim(), System.Globalization.NumberStyles.Integer,
				System.Globalization.CultureInfo.InvariantCulture, out int p) =>
				p,
			_ => 0,
		};

	static bool ParseKind(string raw)
	{
		string z = raw.Trim();
		if (string.IsNullOrEmpty(z))
			return false;
		if (z.Contains("魔") || z.Contains("魔法") || z.Contains("精神"))
			return true;
		if (z.Contains("力量") || z.Contains("蛮力"))
			return false;
		string s = z.ToLowerInvariant();
		if (s is "mag" or "magic" or "spirit")
			return true;
		if (s is "str" or "strength")
			return false;
		return false;
	}

	public static bool TryGet(int id, out Row? row) => ById.TryGetValue(id, out row);

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

	static bool TryReadMonsterRef(Godot.Collections.Dictionary ev, out int id)
	{
		if (TryReadId(ev, "monster_id", out id) && id > 0)
			return true;
		return TryReadId(ev, "unit_id", out id) && id > 0;
	}

	/// <summary>关卡里的事件若带 monster_id（或旧字段 unit_id），从表补全 type/value/icon/name/description。ID 为 int。</summary>
	public static void EnrichMonsterEvent(Godot.Collections.Dictionary ev)
	{
		if (!TryReadMonsterRef(ev, out int idNum) || idNum <= 0)
			return;

		if (!TryGet(idNum, out Row? row) || row is null)
		{
			GD.PushWarning($"MonsterTable: 未知怪物 ID「{idNum}」，请检查 Data/monsters.json。");
			return;
		}

		ev["monster_id"] = row.Id;
		ev["type"] = row.IsMagic ? "monster_mag" : "monster_str";
		ev["value"] = row.Power;
		ev["icon"] = row.IconPath;
		ev["name"] = row.Name;
		ev["description"] = row.Description;
	}
}
