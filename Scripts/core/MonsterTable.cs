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
		/// <summary>与力量型玩家属性对标的怪物力量战力（非 0 语义上的「另一侧」由 <see cref="PowerMag"/> 表示）。</summary>
		public int PowerStr;
		/// <summary>与魔法型玩家属性对标的怪物魔法战力。</summary>
		public int PowerMag;
		public required string IconPath;

		/// <summary>表定义的主检定类型对应的战力（HUD/编辑器摘要）。</summary>
		public int DominantCombatPower => IsMagic ? PowerMag : PowerStr;
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

	/// <summary>BOSS 行「summon_monster_ids」池中随机选一只有效怪物；为空或均无效则用全局 BOSS 刷怪池。</summary>
	public static Row? PickBossSummonMonsterRowFromBossSummonIds(IReadOnlyList<int> summonIds)
	{
		if (summonIds == null || summonIds.Count == 0)
			return PickBossSummonMonsterRow();

		var valid = new List<int>();
		var seen = new HashSet<int>();
		foreach (int id in summonIds)
		{
			if (id <= 0 || !seen.Add(id))
				continue;
			if (TryGet(id, out Row? rowOk) && rowOk != null)
				valid.Add(id);
		}


		if (valid.Count == 0)
		{
			GD.PushWarning(
				"MonsterTable: BOSS 配置的 summon_monster_ids 均无有效怪物行，BOSS 迷雾招怪改用全局 boss_summon_monster_ids / 全员池。");
			return PickBossSummonMonsterRow();
		}

		int pickId = valid[(int)(GD.Randi() % valid.Count)];
		TryGet(pickId, out Row? row);
		return row;
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

			string monsterName = GetStr(d, "name");
			int legacyPower = ClampMonsterPower(GetInt(d, "power", 1));
			int powerStr = d.TryGetValue("power_str", out Variant vps)
				? ClampMonsterPower(LooseIntFromVariant(vps, legacyPower))
				: legacyPower;
			int powerMag = d.TryGetValue("power_mag", out Variant vpm)
				? ClampMonsterPower(LooseIntFromVariant(vpm, legacyPower))
				: legacyPower;

			var row = new Row
			{
				Id = idNum,
				Name = monsterName,
				Description = GetStr(d, "description"),
				IsMagic = ParseKind(GetStr(d, "kind"), monsterName),
				PowerStr = powerStr,
				PowerMag = powerMag,
				IconPath = GetStr(d, "icon"),
			};
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

	static int LooseIntFromVariant(Variant v, int fallback)
	{
		int x = LooseIntFromVariant(v);
		return x > 0 ? x : fallback;
	}

	static int ClampMonsterPower(int v) => Mathf.Clamp(v, 1, 99);

	/// <summary>
	/// 判定怪物为「魔法检定」还是「力量检定」。优先根据 <paramref name="monsterName"/> 前缀纠正常见的
	/// 「怪物名以魔法开头但 kind 误填力量」表格错误；再解析 kind 字段。
	/// </summary>
	static bool ParseKind(string kindRaw, string monsterName)
	{
		string n = monsterName.Trim();
		if (n.StartsWith("魔法", System.StringComparison.Ordinal) ||
		    n.StartsWith("魔力", System.StringComparison.Ordinal))
			return true;
		if (n.StartsWith("力量", System.StringComparison.Ordinal))
			return false;

		string z = kindRaw.Trim();
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
		ev["value_str"] = row.PowerStr;
		ev["value_mag"] = row.PowerMag;
		ev["icon"] = row.IconPath;
		ev["name"] = row.Name;
		ev["description"] = row.Description;
		SyncMonsterEventFightValue(ev);
	}

	/// <summary>
	/// 根据当前 <c>type</c>（monster_str / monster_mag）把 <c>value</c> 设为对应的怪物战力；
	/// 力量对决只读 <c>value_str</c>，魔法对决只读 <c>value_mag</c>，互不当作 0 混算。
	/// </summary>
	public static void SyncMonsterEventFightValue(Godot.Collections.Dictionary ev)
	{
		if (!ev.TryGetValue("type", out Variant tyVar) || tyVar.VariantType != Variant.Type.String)
			return;
		string ty = tyVar.AsString();
		if (ty is not ("monster_str" or "monster_mag"))
			return;

		int fb = ReadEvInt(ev, "value", 1);
		int vs = ReadEvInt(ev, "value_str", fb);
		int vm = ReadEvInt(ev, "value_mag", fb);
		ev["value"] = ty == "monster_mag" ? vm : vs;
	}

	static int ReadEvInt(Godot.Collections.Dictionary ev, string key, int def)
	{
		if (!ev.TryGetValue(key, out Variant v))
			return def;
		return v.VariantType switch
		{
			Variant.Type.Int => v.AsInt32(),
			Variant.Type.Float => (int)v.AsDouble(),
			Variant.Type.String when int.TryParse(v.AsString().Trim(), System.Globalization.NumberStyles.Integer,
				System.Globalization.CultureInfo.InvariantCulture, out int p) =>
				p,
			_ => def,
		};
	}
}
