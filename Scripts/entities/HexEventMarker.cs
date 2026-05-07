using Godot;

namespace Booom202604;

[Tool]
public partial class HexEventMarker : Node2D
{
	public enum Kind
	{
		MonsterStr,
		MonsterMag,
		Treasure,
		Altar,
		Grass,
		Corpse,
		Ruins,
	}

	public const string IconAltarUseful = "res://Art/Icon/Altar_Useful.png";
	public const string IconAltarUsed = "res://Art/Icon/Altar_Uesless.png";

	/// <summary>场景里事件 Sprite2D 的缩放（相对贴图源尺寸）。编辑器与 Gameplay 共用。</summary>
	public const float EventIconSpriteScale = 0.765f;

	static Texture2D? LoadIcon(string resPath) =>
		ResourceLoader.Exists(resPath) ? GD.Load<Texture2D>(resPath) : null;

	static readonly Texture2D? TexMonster = LoadIcon("res://Art/Icon/monster.png");
	static readonly Texture2D? TexTreasure = LoadIcon("res://Art/Icon/TreasureChest.png");
	static readonly Texture2D? TexAltar = LoadIcon(IconAltarUseful);
	static readonly Texture2D? TexGrass = LoadIcon("res://Art/Icon/GrassPatch1.png");
	static readonly Texture2D? TexCorpse = LoadIcon("res://Art/Icon/Grave.png");
	static readonly Texture2D? TexRuins = LoadIcon("res://Art/Icon/AbandonedShrine.png");

	public static Texture2D? TextureFor(Kind kind)
	{
		return kind switch
		{
			Kind.MonsterStr or Kind.MonsterMag => TexMonster,
			Kind.Treasure => TexTreasure,
			Kind.Altar => TexAltar,
			Kind.Grass => TexGrass,
			Kind.Corpse => TexCorpse,
			Kind.Ruins => TexRuins,
			_ => null,
		};
	}

	static bool EventDictBool(Godot.Collections.Dictionary ev, string key)
	{
		if (!ev.TryGetValue(key, out Variant v))
			return false;
		return v switch
		{
			{ VariantType: Variant.Type.Bool } => v.AsBool(),
			{ VariantType: Variant.Type.Int } => v.AsInt32() != 0,
			{ VariantType: Variant.Type.Float } => !Mathf.IsZeroApprox(v.AsSingle()),
			_ => false,
		};
	}

	/// <summary>Loads per-event tile icon when <c>icon</c> path is stored in level JSON.</summary>
	public static Texture2D? TextureForEventDict(Godot.Collections.Dictionary ev)
	{
		string ty = "";
		if (ev.TryGetValue("type", out Variant tVar) && tVar.VariantType == Variant.Type.String)
			ty = tVar.AsString();

		// 祭坛：已使用后固定为「耗尽」贴图；编辑/未使用则尊重关卡里的 icon（默认可用）。
		if (ty == "altar" && EventDictBool(ev, "altar_used"))
			return LoadIcon(IconAltarUsed) ?? TexAltar;

		if (ev.TryGetValue("icon", out Variant pathVar) && pathVar.VariantType == Variant.Type.String)
		{
			string p = pathVar.AsString();
			if (!string.IsNullOrEmpty(p) && ResourceLoader.Exists(p))
				return GD.Load<Texture2D>(p);
		}

		return TextureFor(StringToKind(ty));
	}

	public static Kind StringToKind(string s)
	{
		return s switch
		{
			"monster_str" => Kind.MonsterStr,
			"monster_mag" => Kind.MonsterMag,
			"treasure" => Kind.Treasure,
			"altar" => Kind.Altar,
			"grass" => Kind.Grass,
			"corpse" => Kind.Corpse,
			"ruins" => Kind.Ruins,
			_ => Kind.Grass,
		};
	}

	public static string KindToString(Kind kind)
	{
		return kind switch
		{
			Kind.MonsterStr => "monster_str",
			Kind.MonsterMag => "monster_mag",
			Kind.Treasure => "treasure",
			Kind.Altar => "altar",
			Kind.Grass => "grass",
			Kind.Corpse => "corpse",
			Kind.Ruins => "ruins",
			_ => "grass",
		};
	}

	[Export] public TileMapLayer? TileMapLayer { get; set; }

	[Export(PropertyHint.Range, "1,99")]
	public int CombatValue { get; set; } = 2;

	[Export] public bool AltarUsed { get; set; }

	Vector2I _cell;
	Kind _kind = Kind.MonsterStr;
	bool _suppress;
	Sprite2D? _sprite;

	[Export]
	public Vector2I CellProp
	{
		get => _cell;
		set
		{
			_cell = value;
			SyncPosFromCell();
		}
	}

	[Export]
	public Kind KindProp
	{
		get => _kind;
		set
		{
			_kind = value;
			if (IsInsideTree())
				RefreshIcon();
		}
	}

	public override void _EnterTree()
	{
		if (_sprite == null)
		{
			_sprite = new Sprite2D { Name = "Icon", Centered = true };
			AddChild(_sprite);
		}

		RefreshIcon();
		SyncPosFromCell();
	}

	public override void _Ready()
	{
		RefreshIcon();
		SyncPosFromCell();
	}

	void RefreshIcon()
	{
		if (_sprite == null)
			return;

		var tex = TextureFor(_kind);
		_sprite.Texture = tex;
		if (tex != null)
		{
			_sprite.Scale = new Vector2(EventIconSpriteScale, EventIconSpriteScale);
			_sprite.Position = new Vector2(0f, -tex.GetHeight() * 0.12f);
		}
	}

	void SyncPosFromCell()
	{
		if (TileMapLayer == null || !TileMapLayer.IsInsideTree())
			return;

		_suppress = true;
		Position = TileMapLayer.MapToLocal(_cell);
		_suppress = false;
	}

	void SyncCellFromPos()
	{
		if (TileMapLayer == null)
			return;

		Vector2 local = TileMapLayer.ToLocal(GlobalPosition);
		_cell = TileMapLayer.LocalToMap(local);
		SyncPosFromCell();
	}

	public override void _Notification(int what)
	{
		if (what == NotificationTransformChanged && Engine.IsEditorHint() && !_suppress)
			SyncCellFromPos();
	}

	public Godot.Collections.Dictionary ToEventDict()
	{
		return new Godot.Collections.Dictionary
		{
			["type"] = KindToString(_kind),
			["x"] = _cell.X,
			["y"] = _cell.Y,
			["value"] = CombatValue,
			["altar_used"] = AltarUsed,

		};

	}



}
