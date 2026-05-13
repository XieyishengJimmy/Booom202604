using Godot;

namespace Booom202604;

/// <summary>
/// Autoload：一次性音效。每种 clip 使用<strong>独立</strong>的 <see cref="AudioStreamPlayer"/> 与<strong>独立加载</strong>的流，
/// 避免池化或多路复用同一 <see cref="AudioStream"/> 时在部分平台上仅首段可播的问题。
/// </summary>
public partial class GameSfx : Node
{
	public static GameSfx? Instance { get; private set; }

	/// <summary>音效相对满幅的线性比例；0.5 ≈ −6 dB，即「音量降低 50%」。</summary>
	const float SfxOutputLinear = 0.5f;

	AudioStreamPlayer? _walk;
	AudioStreamPlayer? _attack;
	AudioStreamPlayer? _lose;
	AudioStreamPlayer? _skill;
	AudioStreamPlayer? _bossSkill;

	public override void _EnterTree()
	{
		Instance = this;
	}

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;
		_walk = CreateClipPlayer("walk");
		_attack = CreateClipPlayer("attack");
		_lose = CreateClipPlayer("lose");
		_skill = CreateClipPlayer("skill");
		_bossSkill = CreateClipPlayer("bossSkill");
	}

	public override void _ExitTree()
	{
		if (Instance == this)
			Instance = null;
		base._ExitTree();
	}

	static string PathFor(string clipBaseName) => $"res://Audio/{clipBaseName}.wav";

	AudioStreamPlayer? CreateClipPlayer(string clipBaseName)
	{
		string path = PathFor(clipBaseName);
		if (!ResourceLoader.Exists(path))
		{
			GD.PushWarning($"GameSfx: missing {path}");
			return null;
		}

		AudioStream? stream = GD.Load<AudioStream>(path);
		if (stream == null)
		{
			GD.PushWarning($"GameSfx: failed to load {path}");
			return null;
		}

		var p = new AudioStreamPlayer
		{
			Name = $"Sfx_{clipBaseName}",
			Bus = "Master",
			ProcessMode = ProcessModeEnum.Always,
			Stream = stream,
			VolumeDb = Mathf.LinearToDb(SfxOutputLinear),
		};
		AddChild(p);
		return p;
	}

	static void PlayOn(AudioStreamPlayer? player)
	{
		if (player == null || player.Stream == null)
			return;
		player.Stop();
		player.Play();
	}

	static GameSfx? Resolve()
	{
		if (Instance != null && IsInstanceValid(Instance))
			return Instance;
		if (Engine.GetMainLoop() is SceneTree tree && tree.Root.GetNodeOrNull<GameSfx>("/root/GameSfx") is { } n)
			return n;
		return null;
	}

	public static void PlayWalk() => PlayOn(Resolve()?._walk);

	public static void PlayAttack() => PlayOn(Resolve()?._attack);

	public static void PlayLose() => PlayOn(Resolve()?._lose);

	public static void PlaySkill() => PlayOn(Resolve()?._skill);

	public static void PlayBossSkill() => PlayOn(Resolve()?._bossSkill);
}
