using Godot;

namespace Booom202604;

/// <summary>
/// 世界地图上事件图标。怪物布局与 <c>res://Scenes/map.tscn</c> 一致：地砖「2」原点为格心参考，
/// 怪物贴图 2001 为 <c>(0,-143)</c>，Physic / Magic 框为 <c>(-187,-356)</c>、<c>(174,-361)</c>。
/// 怪物整组在 <see cref="HexEventMarker.EventIconSpriteScale"/> 基础上再乘 <see cref="MonsterGroupExtraScale"/>（在约 +50% 上再缩小约 10%）。
/// </summary>
public static class EventWorldIconFactory
{
	/// <summary>怪物贴图 + 力量/魔法框相对基础事件缩放的额外倍率（约 +50% 后再 ×0.9）。</summary>
	public const float MonsterGroupExtraScale = 1.5f * 0.9f;

	/// <summary>框内数值字号（在原先 22 基础上放大 3 倍）。</summary>
	public const int BadgeValueFontSize = 66;

	/// <summary>力量数值颜色 <c>#4a2a10</c>。</summary>
	public static readonly Color PhysicValueColor = new(0x4a / 255f, 0x2a / 255f, 0x10 / 255f);

	/// <summary>魔法数值颜色 <c>#5e0128</c>。</summary>
	public static readonly Color MagicValueColor = new(0x5e / 255f, 0x01 / 255f, 0x28 / 255f);

	public const string MonsterBodyNodeName = "MonsterBody";
	public const string PhysBadgeNodeName = "PhysBadge";
	public const string MagBadgeNodeName = "MagBadge";
	public const string FlatSpriteNodeName = "EvIconSprite";
	public const string ValueLabelName = "Value";

	/// <summary>事件图标节点上用于关联格子 key 的 meta 名（与 Gameplay 一致）。</summary>
	public static readonly StringName CellKeyMetaName = new("cell_key");

	/// <summary>挂在 overlay 上的 Phys/Mag 框用此 meta 区分类型（避免 Godot 对重名兄弟自动改名后 <c>Name</c> 不再是 PhysBadge）。</summary>
	public static readonly StringName StatBadgeKindMeta = new("stat_badge");

	/// <summary><see cref="StatBadgeKindMeta"/> 取值：力量框。</summary>
	public const string StatBadgeKindPhysValue = "phys";

	/// <summary><see cref="StatBadgeKindMeta"/> 取值：魔法框。</summary>
	public const string StatBadgeKindMagValue = "mag";

	static readonly Texture2D? TexPhysicFrame = TryLoad("res://Art/UI/monster/Physic.png");
	static readonly Texture2D? TexMagicFrame = TryLoad("res://Art/UI/monster/Magic.png");

	/// <summary>map.tscn 中 2001 相对格心（与地砖「2」同原点）的位移（像素）。</summary>
	public static readonly Vector2 MapMonsterBodyPosition = new(0f, -143f);

	/// <summary>map.tscn 中 Physic 框相对格心位移。</summary>
	public static readonly Vector2 MapPhysBadgePosition = new(-187f, -356f);

	/// <summary>map.tscn 中 Magic 框相对格心位移。</summary>
	public static readonly Vector2 MapMagBadgePosition = new(174f, -361f);

	static Texture2D? TryLoad(string path) =>
		ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;

	static string DictStr(Godot.Collections.Dictionary d, string key, string def = "")
	{
		if (!d.TryGetValue(key, out Variant v) || v.VariantType != Variant.Type.String)
			return def;
		return v.AsString();
	}

	static int DictInt(Godot.Collections.Dictionary d, string key, int def)
	{
		if (!d.TryGetValue(key, out Variant v))
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

	/// <summary>创建事件图标根节点（怪物为 Node2D 组，其它事件为单层 Sprite 包在 Node2D 内）。</summary>
	public static Node2D BuildIconRoot(Godot.Collections.Dictionary ev, float eventIconSpriteScale)
	{
		string ty = DictStr(ev, "type", "");
		if (ty is "monster_str" or "monster_mag")
			return BuildMonsterIconRoot(ev, ty, eventIconSpriteScale);

		var wrap = new Node2D { Name = "EvFlatWrap" };
		var spr = new Sprite2D { Name = FlatSpriteNodeName };
		spr.Texture = HexEventMarker.TextureForEventDict(ev);
		if (spr.Texture != null)
		{
			spr.Scale = new Vector2(eventIconSpriteScale, eventIconSpriteScale);
			spr.Offset = new Vector2(0f, -spr.Texture.GetHeight() * 0.05f);
		}

		wrap.AddChild(spr);
		return wrap;
	}

	static Node2D BuildMonsterIconRoot(Godot.Collections.Dictionary ev, string ty, float s)
	{
		var root = new Node2D { Name = "EvMonster" };
		float g = s * MonsterGroupExtraScale;
		root.Scale = new Vector2(g, g);

		var body = new Sprite2D
		{
			Name = MonsterBodyNodeName,
			Texture = HexEventMarker.TextureForEventDict(ev),
			Centered = true,
			Position = MapMonsterBodyPosition,
		};
		root.AddChild(body);

		int vs = DictInt(ev, "value_str", DictInt(ev, "value", 1));
		int vm = DictInt(ev, "value_mag", DictInt(ev, "value", 1));

		Node2D phys = BuildStatBadge(TexPhysicFrame, vs, PhysicValueColor);
		phys.Name = PhysBadgeNodeName;
		phys.Position = MapPhysBadgePosition;
		root.AddChild(phys);

		Node2D mag = BuildStatBadge(TexMagicFrame, vm, MagicValueColor);
		mag.Name = MagBadgeNodeName;
		mag.Position = MapMagBadgePosition;
		root.AddChild(mag);

		bool isMag = ty == "monster_mag";
		phys.Visible = !isMag;
		mag.Visible = isMag;

		return root;
	}

	static Node2D BuildStatBadge(Texture2D? frameTex, int value, Color valueColor)
	{
		var holder = new Node2D();
		var frame = new Sprite2D
		{
			Texture = frameTex,
			Centered = true,
		};
		holder.AddChild(frame);

		var lbl = new Label
		{
			Name = ValueLabelName,
			Text = value.ToString(),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		lbl.AddThemeFontSizeOverride("font_size", BadgeValueFontSize);
		lbl.AddThemeColorOverride("font_color", valueColor);

		Vector2 fs = frameTex != null
			? frameTex.GetSize()
			: new Vector2(48f, 32f);
		float boxW = Mathf.Max(fs.X * 2.4f, BadgeValueFontSize * 1.35f);
		float boxH = Mathf.Max(fs.Y * 2f, BadgeValueFontSize * 1.2f);
		var box = new Vector2(boxW, boxH);
		lbl.Position = -box * 0.5f;
		lbl.Size = box;
		holder.AddChild(lbl);
		return holder;
	}

	/// <summary>将力量/魔法框挂到世界顶层 overlay，保证绘制在迷雾、主角、高亮等之上（与 <see cref="RefreshIconFromEvent"/> 配套）。</summary>
	public static void ReparentMonsterStatBadges(Node2D monsterRoot, Node2D overlay, StringName cellKeyMeta, string cellKey)
	{
		Node2D? phys = monsterRoot.GetNodeOrNull<Node2D>(PhysBadgeNodeName);
		Node2D? mag = monsterRoot.GetNodeOrNull<Node2D>(MagBadgeNodeName);
		if (phys == null || mag == null)
			return;

		phys.SetMeta(cellKeyMeta, cellKey);
		mag.SetMeta(cellKeyMeta, cellKey);
		phys.SetMeta(StatBadgeKindMeta, StatBadgeKindPhysValue);
		mag.SetMeta(StatBadgeKindMeta, StatBadgeKindMagValue);
		phys.Reparent(overlay);
		mag.Reparent(overlay);
		// 画布绝对层级：须高于 FogLayer 吸收锁（ZIndex=2），否则锁会盖住属性框。
		phys.ZAsRelative = false;
		mag.ZAsRelative = false;
		phys.ZIndex = 30;
		mag.ZIndex = 31;
	}

	static (Node2D? Phys, Node2D? Mag) FindStatBadgesOnOverlay(Node2D overlay, StringName cellKeyMeta, string cellKey)
	{
		Node2D? p = null;
		Node2D? m = null;
		foreach (Node ch in overlay.GetChildren())
		{
			if (!ch.HasMeta(cellKeyMeta) || ch.GetMeta(cellKeyMeta).AsString() != cellKey)
				continue;
			string kind = ch.HasMeta(StatBadgeKindMeta) ? ch.GetMeta(StatBadgeKindMeta).AsString() : "";
			if (kind == StatBadgeKindPhysValue || ch.Name == PhysBadgeNodeName)
				p = ch as Node2D;
			else if (kind == StatBadgeKindMagValue || ch.Name == MagBadgeNodeName)
				m = ch as Node2D;
		}

		return (p, m);
	}

	/// <summary>BOSS 改事件后刷新已有节点（贴图、数值、力量/魔法框显隐）。</summary>
	public static void RefreshIconFromEvent(Node2D root, Godot.Collections.Dictionary ev) =>
		RefreshIconFromEvent(root, ev, null, CellKeyMetaName, "");

	/// <param name="badgeOverlay">非空且 <paramref name="cellKey"/> 非空时，在 overlay 上按 meta 查找已拆出的框节点。</param>
	public static void RefreshIconFromEvent(Node2D root, Godot.Collections.Dictionary ev,
		Node2D? badgeOverlay, StringName cellKeyMeta, string? cellKey)
	{
		var flat = root.GetNodeOrNull<Sprite2D>(FlatSpriteNodeName);
		if (flat != null)
		{
			flat.Texture = HexEventMarker.TextureForEventDict(ev);
			return;
		}

		var body = root.GetNodeOrNull<Sprite2D>(MonsterBodyNodeName);
		if (body != null)
			body.Texture = HexEventMarker.TextureForEventDict(ev);

		string ty = DictStr(ev, "type", "");
		if (ty is not ("monster_str" or "monster_mag"))
			return;

		int vs = DictInt(ev, "value_str", DictInt(ev, "value", 1));
		int vm = DictInt(ev, "value_mag", DictInt(ev, "value", 1));

		Node2D? phys;
		Node2D? mag;
		if (badgeOverlay != null && !string.IsNullOrEmpty(cellKey))
		{
			(phys, mag) = FindStatBadgesOnOverlay(badgeOverlay, cellKeyMeta, cellKey);
			if (phys == null && mag == null)
			{
				phys = root.GetNodeOrNull<Node2D>(PhysBadgeNodeName);
				mag = root.GetNodeOrNull<Node2D>(MagBadgeNodeName);
			}
		}
		else
		{
			phys = root.GetNodeOrNull<Node2D>(PhysBadgeNodeName);
			mag = root.GetNodeOrNull<Node2D>(MagBadgeNodeName);
		}

		SetBadgeValueLabel(phys, vs);
		SetBadgeValueLabel(mag, vm);
		if (phys != null)
			phys.Visible = ty == "monster_str";
		if (mag != null)
			mag.Visible = ty == "monster_mag";
	}

	static void SetBadgeValueLabel(Node2D? badgeHolder, int value)
	{
		var lbl = badgeHolder?.GetNodeOrNull<Label>(ValueLabelName);
		if (lbl != null)
			lbl.Text = value.ToString();
	}
}
