using System.Buffers.Binary;
using System.Text;

/// <summary>
/// 将 Godot 4.x 无法导入的 WAV（WAVE_FORMAT_EXTENSIBLE、24-bit 等）转为标准 PCM 16-bit LE（format tag 1），便于引擎导入。
/// </summary>
static class Program
{
	const ushort WFormatPcm = 1;
	const ushort WFormatExtensible = 0xFFFE;

	static int Main(string[] args)
	{
		string dir = args.Length > 0
			? Path.GetFullPath(args[0])
			: Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Audio"));
		if (!Directory.Exists(dir))
		{
			Console.Error.WriteLine($"目录不存在: {dir}");
			return 1;
		}

		int fixedCount = 0;
		foreach (string path in Directory.GetFiles(dir, "*.wav"))
		{
			if (TryConvertToLegacyPcm16(path, out string msg))
			{
				Console.WriteLine(msg);
				fixedCount++;
			}
			else if (msg.Length > 0)
				Console.WriteLine($"{Path.GetFileName(path)}: {msg}");
		}

		Console.WriteLine(fixedCount > 0 ? $"完成，已重写 {fixedCount} 个文件。请在 Godot 中重新打开项目或点「重新导入」。" : "无需修改（已是标准 PCM 或无法解析）。");
		return 0;
	}

	static bool TryConvertToLegacyPcm16(string path, out string msg)
	{
		msg = "";
		byte[] src = File.ReadAllBytes(path);
		if (src.Length < 44)
		{
			msg = "文件过小";
			return false;
		}

		if (Encoding.ASCII.GetString(src, 0, 4) != "RIFF" || Encoding.ASCII.GetString(src, 8, 4) != "WAVE")
		{
			msg = "非 RIFF WAVE";
			return false;
		}

		if (!TryParseFmtAndData(src, out Fmt fmt, out int dataOffset, out int dataSize))
		{
			msg = "无法解析 fmt/data";
			return false;
		}

		if (fmt.AudioFormat == WFormatPcm && fmt.BitsPerSample == 16 && fmt.ExtraSize == 0)
		{
			msg = "已是标准 PCM16";
			return false;
		}

		if (fmt.Channels is < 1 or > 2)
		{
			msg = $"不支持的声道数 {fmt.Channels}";
			return false;
		}

		ReadOnlySpan<byte> pcm = src.AsSpan(dataOffset, dataSize);
		byte[] outPcm;
		if (fmt.AudioFormat == WFormatPcm && fmt.BitsPerSample == 16 && fmt.ExtraSize > 0)
		{
			// 扩展头下的 16-bit PCM：直接按块拷贝
			outPcm = pcm.ToArray();
		}
		else if (fmt.AudioFormat == WFormatExtensible && fmt.BitsPerSample == 16 && fmt.IsPcmSubFormat)
		{
			outPcm = pcm.ToArray();
		}
		else if (fmt is { AudioFormat: WFormatExtensible, BitsPerSample: 24 } && fmt.IsPcmSubFormat)
		{
			int frames = pcm.Length / fmt.BlockAlign;
			outPcm = new byte[frames * fmt.Channels * 2];
			int dst = 0;
			for (int f = 0; f < frames; f++)
			{
				int baseOff = f * fmt.BlockAlign;
				for (int c = 0; c < fmt.Channels; c++)
				{
					int s24 = ReadInt24Le(pcm, baseOff + c * 3);
					short s16 = ClampToInt16(s24 * (1.0 / 256.0));
					BinaryPrimitives.WriteInt16LittleEndian(outPcm.AsSpan(dst), s16);
					dst += 2;
				}
			}
		}
		else
		{
			msg = $"未处理的组合 format={fmt.AudioFormat} bits={fmt.BitsPerSample} pcmSub={fmt.IsPcmSubFormat}";
			return false;
		}

		int sampleRate = fmt.SampleRate;
		short channels = (short)fmt.Channels;
		byte[] file = BuildPcm16WavFile(sampleRate, channels, outPcm);
		string bak = path + ".bak";
		if (!File.Exists(bak))
			File.Copy(path, bak, overwrite: false);
		File.WriteAllBytes(path, file);
		msg = $"已转换: {Path.GetFileName(path)} → PCM16 {sampleRate}Hz {channels}ch ({outPcm.Length} 样本字节)";
		return true;
	}

	static short ClampToInt16(double v)
	{
		if (v > 32767) return 32767;
		if (v < -32768) return -32768;
		return (short)Math.Round(v);
	}

	static int ReadInt24Le(ReadOnlySpan<byte> b, int o)
	{
		int lo = b[o] | (b[o + 1] << 8) | (b[o + 2] << 16);
		if ((lo & 0x800000) != 0)
			lo |= unchecked((int)0xFF000000);
		return lo;
	}

	static bool TryParseFmtAndData(ReadOnlySpan<byte> file, out Fmt fmt, out int dataOffset, out int dataSize)
	{
		fmt = default;
		dataOffset = 0;
		dataSize = 0;
		int pos = 12;
		while (pos + 8 <= file.Length)
		{
			string id = Encoding.ASCII.GetString(file.Slice(pos, 4));
			int size = BinaryPrimitives.ReadInt32LittleEndian(file.Slice(pos + 4, 4));
			int bodyStart = pos + 8;
			if (bodyStart + size > file.Length)
				return false;
			ReadOnlySpan<byte> body = file.Slice(bodyStart, size);
			if (id == "fmt ")
			{
				if (!TryParseFmtBody(body, out fmt))
					return false;
			}
			else if (id == "data")
			{
				dataOffset = bodyStart;
				dataSize = size;
			}

			pos = bodyStart + size + (size & 1);
		}

		return dataOffset > 0 && dataSize > 0 && fmt.SampleRate > 0;
	}

	static bool TryParseFmtBody(ReadOnlySpan<byte> body, out Fmt fmt)
	{
		fmt = default;
		if (body.Length < 16)
			return false;
		fmt.AudioFormat = BinaryPrimitives.ReadUInt16LittleEndian(body);
		fmt.Channels = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(2));
		fmt.SampleRate = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(4));
		fmt.ByteRate = BinaryPrimitives.ReadInt32LittleEndian(body.Slice(8));
		fmt.BlockAlign = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(12));
		fmt.BitsPerSample = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(14));
		fmt.ExtraSize = 0;
		fmt.IsPcmSubFormat = fmt.AudioFormat == WFormatPcm;
		if (body.Length > 16)
		{
			fmt.ExtraSize = BinaryPrimitives.ReadUInt16LittleEndian(body.Slice(16));
			if (fmt.AudioFormat == WFormatExtensible && body.Length >= 40)
			{
				// SubFormat GUID at end of 40-byte extensible
				ReadOnlySpan<byte> guid = body.Slice(24, 16);
				fmt.IsPcmSubFormat = guid.SequenceEqual(PcmSubFormatGuid);
			}
		}

		return true;
	}

	/// <summary>KSDATAFORMAT_SUBTYPE_PCM</summary>
	static ReadOnlySpan<byte> PcmSubFormatGuid =>
	[
		0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x10, 0x00,
		0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71
	];

	static byte[] BuildPcm16WavFile(int sampleRate, short channels, ReadOnlySpan<byte> pcm16Interleaved)
	{
		int dataChunkSize = pcm16Interleaved.Length;
		int byteRate = sampleRate * channels * 2;
		short blockAlign = (short)(channels * 2);
		int riffSize = 4 + (8 + 16) + (8 + dataChunkSize);
		using var ms = new MemoryStream(8 + 4 + riffSize);
		var w = new BinaryWriter(ms);
		w.Write(Encoding.ASCII.GetBytes("RIFF"));
		w.Write(riffSize);
		w.Write(Encoding.ASCII.GetBytes("WAVE"));
		w.Write(Encoding.ASCII.GetBytes("fmt "));
		w.Write(16);
		w.Write(WFormatPcm);
		w.Write(channels);
		w.Write(sampleRate);
		w.Write(byteRate);
		w.Write(blockAlign);
		w.Write((short)16);
		w.Write(Encoding.ASCII.GetBytes("data"));
		w.Write(dataChunkSize);
		w.Write(pcm16Interleaved);
		return ms.ToArray();
	}

	struct Fmt
	{
		public ushort AudioFormat;
		public ushort Channels;
		public int SampleRate;
		public int ByteRate;
		public ushort BlockAlign;
		public ushort BitsPerSample;
		public ushort ExtraSize;
		public bool IsPcmSubFormat;
	}
}
