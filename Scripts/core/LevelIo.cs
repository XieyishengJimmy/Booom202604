using Godot;

namespace Booom202604;

public static class LevelIo
{
	public const int Version = 1;

	public static Error SaveToFile(string path, Godot.Collections.Dictionary data)
	{
		data["version"] = Version;
		string json = Json.Stringify(Variant.From(data), "\t");
		using Godot.FileAccess f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
		if (f == null)
		{
			GD.PushError($"LevelIo: cannot write {path}");
			return Godot.FileAccess.GetOpenError();
		}

		f.StoreString(json);

		return Error.Ok;
	}

	public static Godot.Collections.Dictionary LoadFromFile(string path)
	{
		if (!Godot.FileAccess.FileExists(path))
		{
			GD.PushWarning($"LevelIo: missing {path}");
			return new Godot.Collections.Dictionary();
		}

		using Godot.FileAccess f = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
		if (f == null)
		{
			GD.PushError($"LevelIo: cannot read {path}");
			return new Godot.Collections.Dictionary();
		}

		string text = f.GetAsText();
		var parser = new Json();
		Error err = parser.Parse(text);
		if (err != Error.Ok)
		{
			GD.PushError($"LevelIo: json parse failed {path}");
			return new Godot.Collections.Dictionary();
		}

		var any = parser.Data;
		if (any.VariantType != Variant.Type.Dictionary)
		{
			GD.PushError($"LevelIo: invalid json object {path}");
			return new Godot.Collections.Dictionary();
		}

		return any.AsGodotDictionary();
	}

}
