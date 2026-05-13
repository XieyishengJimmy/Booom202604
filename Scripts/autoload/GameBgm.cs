using Godot;

namespace Booom202604;

/// <summary>
/// Autoload：自游戏进程启动起循环播放 <c>res://Audio/BGM.wav</c>，直至进程退出；切场景不中断。
/// </summary>
public partial class GameBgm : Node
{
	const string BgmPath = "res://Audio/BGM.wav";

	/// <summary>项目设置键：<c>project.godot</c> → <c>[audio]</c> → <c>bgm_volume_db</c>（分贝）。</summary>
	public const string BgmVolumeDbSetting = "audio/bgm_volume_db";

	AudioStreamPlayer? _player;

	public override void _Ready()
	{
		ProcessMode = ProcessModeEnum.Always;

		float volumeDb = ReadBgmVolumeDbFromProject();

		_player = new AudioStreamPlayer
		{
			Name = "BgmPlayer",
			Bus = "Master",
			ProcessMode = ProcessModeEnum.Always,
			VolumeDb = volumeDb,
		};
		AddChild(_player);

		if (!ResourceLoader.Exists(BgmPath))
		{
			GD.PushWarning($"GameBgm: stream not found: {BgmPath}");
			return;
		}

		AudioStream? stream = GD.Load<AudioStream>(BgmPath);
		if (stream == null)
		{
			GD.PushWarning($"GameBgm: failed to load {BgmPath}");
			return;
		}

		double len = stream.GetLength();
		if (len <= 0.0)
			GD.PushWarning(
				$"GameBgm: loaded stream length is {len} — WAV 可能导入异常（见 Godot #85466），请用编辑器重导或另存为标准 WAV。");

		// 不要使用 Duplicate(false)：部分环境下 WAV 浅拷贝不携带 PCM 数据，会导致整段静音。
		// 循环以 <c>Audio/BGM.wav.import</c> 的 <c>edit/loop_mode</c> 为准；若未循环则靠 <see cref="OnBgmFinished"/> 重播。
		_player.Stream = stream;

		int masterIdx = AudioServer.GetBusIndex("Master");
		if (masterIdx < 0)
			GD.PushWarning(
				"GameBgm: 未找到名为 Master 的音频总线。请在 项目 → 项目设置 → 音频 → 输出 检查「默认总线布局」是否指向有效的 default_bus_layout.tres。");
		else if (AudioServer.IsBusMute(masterIdx))
			GD.PushWarning("GameBgm: Master 总线处于静音状态（编辑器底部「音频」停靠栏）。");

		_player.Finished += OnBgmFinished;

		// 等音频服务器与节点树就绪后再播；部分设备上首帧 Play 会无效。
		Callable.From(StartBgmDeferred).CallDeferred();
	}

	void StartBgmDeferred()
	{
		if (!IsInstanceValid(_player) || _player.Stream == null)
			return;
		_player.StreamPaused = false;
		TryPlayBgm("deferred");
		Callable.From(RetryPlayNextFrame).CallDeferred();
	}

	void RetryPlayNextFrame()
	{
		if (!IsInstanceValid(_player) || _player.Stream == null)
			return;
		if (!_player.Playing)
			TryPlayBgm("retry-frame");
	}

	void TryPlayBgm(string reason)
	{
		if (!IsInstanceValid(_player) || _player.Stream == null)
			return;
		_player.Play();
		if (!_player.Playing)
			GD.PushWarning($"GameBgm: AudioStreamPlayer.Play() 未进入播放状态（{reason}）。请检查音频设备与其它 AudioStreamPlayer 是否独占。");
	}

	void OnBgmFinished()
	{
		if (!IsInstanceValid(_player) || _player.Stream == null)
			return;
		_player.Play();
	}

	public override void _ExitTree()
	{
		if (_player != null)
		{
			_player.Finished -= OnBgmFinished;
			_player = null;
		}

		base._ExitTree();
	}

	/// <summary>读取 <see cref="BgmVolumeDbSetting"/>；未配置时默认约 -15 dB（比 0 dB 明显更轻）。</summary>
	static float ReadBgmVolumeDbFromProject()
	{
		const float defaultDb = -15f;
		Variant v = ProjectSettings.GetSetting(BgmVolumeDbSetting, defaultDb);
		if (v.VariantType == Variant.Type.Nil)
			return defaultDb;
		float db = v.VariantType switch
		{
			Variant.Type.Int => v.AsInt32(),
			Variant.Type.Float => (float)v.AsDouble(),
			Variant.Type.String when float.TryParse(v.AsString().Trim(), System.Globalization.NumberStyles.Float,
				System.Globalization.CultureInfo.InvariantCulture, out float parsed) =>
				parsed,
			_ => defaultDb,
		};
		return Mathf.Clamp(db, -80f, 24f);
	}
}
